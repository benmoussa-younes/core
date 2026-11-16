// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers;
using Core.Results;

namespace Core.Primitives;

/// <summary>
/// A compiled glob pattern matched against forward-slash separated path segments.
/// </summary>
/// <remarks>
/// Supported syntax: <c>*</c> matches any sequence except <c>/</c>; <c>?</c> matches one character
/// except <c>/</c>; <c>[abc]</c>, <c>[a-z]</c> and <c>[!a-z]</c> match a character class; <c>**</c> as a
/// whole segment matches zero or more segments; <c>\</c> escapes the next character.
/// </remarks>
public sealed class PathPattern
{
    private const int MaxLength = 4096;
    private const int StackMemoLimit = 2048;

    private readonly Segment[] _segments;
    private readonly int _globStarCount;

    private PathPattern(string pattern, Segment[] segments, bool ignoreCase)
    {
        Pattern = pattern;
        _segments = segments;
        IgnoreCase = ignoreCase;

        foreach (var segment in segments)
        {
            if (segment.IsGlobStar)
            {
                _globStarCount++;
            }
        }
    }

    /// <summary>
    /// Gets the original pattern text.
    /// </summary>
    public string Pattern { get; }

    /// <summary>
    /// Gets a value indicating whether matching ignores character casing.
    /// </summary>
    public bool IgnoreCase { get; }

    /// <summary>
    /// Compiles a glob pattern.
    /// </summary>
    /// <param name="pattern">The pattern to compile.</param>
    /// <returns>The compiled pattern, or a <see cref="CoreError"/> when the pattern is invalid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is <see langword="null"/>.</exception>
    public static Result<PathPattern> Create(string pattern)
    {
        return Create(pattern, ignoreCase: false);
    }

    /// <summary>
    /// Compiles a glob pattern with the given casing behavior.
    /// </summary>
    /// <param name="pattern">The pattern to compile.</param>
    /// <param name="ignoreCase">Whether matching ignores character casing.</param>
    /// <returns>The compiled pattern, or a <see cref="CoreError"/> when the pattern is invalid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is <see langword="null"/>.</exception>
    public static Result<PathPattern> Create(string pattern, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (pattern.Length > MaxLength || !TryCompile(pattern, out var segments))
        {
            return Result<PathPattern>.Failure(
                new CoreError("pathpattern.invalid", $"Invalid path pattern: '{pattern}'."));
        }

        return Result<PathPattern>.Success(new PathPattern(pattern, segments, ignoreCase));
    }

    /// <summary>
    /// Determines whether a path matches the pattern.
    /// </summary>
    /// <param name="path">The forward-slash separated path to test.</param>
    /// <returns><see langword="true"/> when the path matches; otherwise <see langword="false"/>.</returns>
    public bool IsMatch(ReadOnlySpan<char> path)
    {
        if (path.Length > MaxLength)
        {
            return false;
        }

        if (_globStarCount == 0)
        {
            return MatchWithoutGlobStar(path);
        }

        if (_globStarCount == 1)
        {
            return MatchSingleGlobStar(path);
        }

        var segmentCount = path.IsEmpty ? 0 : 1;
        foreach (var character in path)
        {
            if (character == '/')
            {
                segmentCount++;
            }
        }

        Span<int> starts = stackalloc int[segmentCount + 1];
        var start = 0;
        var index = 0;
        for (var position = 0; position < path.Length; position++)
        {
            if (path[position] == '/')
            {
                starts[index++] = start;
                start = position + 1;
            }
        }

        starts[index] = start;

        var width = segmentCount + 1;
        var size = (_segments.Length + 1) * width;

        if (size <= StackMemoLimit)
        {
            Span<sbyte> memo = stackalloc sbyte[size];
            return Match(0, 0, path, starts, segmentCount, memo, width);
        }

        var rented = ArrayPool<sbyte>.Shared.Rent(size);

        try
        {
            Array.Clear(rented, 0, size);
            return Match(0, 0, path, starts, segmentCount, rented.AsSpan(), width);
        }
        finally
        {
            ArrayPool<sbyte>.Shared.Return(rented);
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Pattern;
    }

    private bool MatchWithoutGlobStar(ReadOnlySpan<char> path)
    {
        if (_segments.Length == 1)
        {
            return path.IndexOf('/') < 0 && _segments[0].Matches(this, path);
        }

        var remaining = path;

        for (var index = 0; index < _segments.Length; index++)
        {
            var slash = remaining.IndexOf('/');
            var current = slash < 0 ? remaining : remaining[..slash];

            if (!_segments[index].Matches(this, current))
            {
                return false;
            }

            if (slash < 0)
            {
                return index == _segments.Length - 1;
            }

            remaining = remaining[(slash + 1)..];
        }

        return remaining.IsEmpty;
    }

    private bool MatchSingleGlobStar(ReadOnlySpan<char> path)
    {
        var globStarIndex = -1;
        for (var index = 0; index < _segments.Length; index++)
        {
            if (_segments[index].IsGlobStar)
            {
                globStarIndex = index;
                break;
            }
        }

        var prefixLength = globStarIndex;
        var suffixLength = _segments.Length - globStarIndex - 1;

        // Match the prefix from the left and the suffix from the right, so the
        // segment count and the starts array are not built at all.
        var offset = 0;

        for (var index = 0; index < prefixLength; index++)
        {
            var slash = path[offset..].IndexOf('/');
            ReadOnlySpan<char> segment;

            if (slash < 0)
            {
                segment = path[offset..];
                offset = path.Length;
            }
            else
            {
                segment = path[offset..(offset + slash)];
                offset += slash + 1;
            }

            if (!_segments[index].Matches(this, segment))
            {
                return false;
            }
        }

        // `**/<suffix>`: the tail decides both the suffix match and the
        // non-overlap rule, so the backward boundary search is skipped.
        if (suffixLength == 1 && _segments[^1].TryMatchTail(this, path[offset..], out var tail))
        {
            return tail;
        }

        var end = path.Length;
        var suffixStart = path.Length;

        for (var index = 0; index < suffixLength; index++)
        {
            var slash = path[..end].LastIndexOf('/');
            ReadOnlySpan<char> segment;

            if (slash < 0)
            {
                segment = path[..end];
                suffixStart = 0;
                end = 0;
            }
            else
            {
                segment = path[(slash + 1)..end];
                suffixStart = slash + 1;
                end = slash;
            }

            if (!_segments[_segments.Length - 1 - index].Matches(this, segment))
            {
                return false;
            }
        }

        // The globstar matches the segments between the two regions; they must
        // not overlap (offset is the middle start, suffixStart the suffix start).
        return offset <= suffixStart;
    }

    private bool Match(
        int patternIndex,
        int segmentIndex,
        ReadOnlySpan<char> path,
        ReadOnlySpan<int> starts,
        int segmentCount,
        Span<sbyte> memo,
        int width)
    {
        var memoIndex = (patternIndex * width) + segmentIndex;
        if (memo[memoIndex] != 0)
        {
            return memo[memoIndex] == 1;
        }

        var result = false;

        if (patternIndex == _segments.Length)
        {
            result = segmentIndex == segmentCount;
        }
        else
        {
            var segment = _segments[patternIndex];

            if (segment.IsGlobStar)
            {
                if (patternIndex + 1 == _segments.Length)
                {
                    result = true;
                }
                else
                {
                    for (var next = segmentIndex; next <= segmentCount; next++)
                    {
                        if (Match(patternIndex + 1, next, path, starts, segmentCount, memo, width))
                        {
                            result = true;
                            break;
                        }
                    }
                }
            }
            else if (segmentIndex < segmentCount)
            {
                var start = starts[segmentIndex];
                var end = segmentIndex + 1 < segmentCount ? starts[segmentIndex + 1] - 1 : path.Length;

                if (segment.Matches(this, path[start..end]))
                {
                    result = Match(patternIndex + 1, segmentIndex + 1, path, starts, segmentCount, memo, width);
                }
            }
        }

        memo[memoIndex] = (sbyte)(result ? 1 : 2);
        return result;
    }

    private bool SegmentEquals(ReadOnlySpan<char> text, string value)
    {
        if (text.Length != value.Length)
        {
            return false;
        }

        return IgnoreCase
            ? text.Equals(value.AsSpan(), StringComparison.OrdinalIgnoreCase)
            : OrdinalEquals(text, value);
    }

    private bool SegmentStartsWith(ReadOnlySpan<char> text, string value)
    {
        if (text.Length < value.Length)
        {
            return false;
        }

        return IgnoreCase
            ? text.StartsWith(value.AsSpan(), StringComparison.OrdinalIgnoreCase)
            : OrdinalStartsWith(text, value);
    }

    private bool SegmentEndsWith(ReadOnlySpan<char> text, string value)
    {
        if (text.Length < value.Length)
        {
            return false;
        }

        return IgnoreCase
            ? text.EndsWith(value.AsSpan(), StringComparison.OrdinalIgnoreCase)
            : OrdinalEndsWith(text, value);
    }

    // Scalar ordinal compares: the vectorised span helpers have a per-call setup
    // that loses on the short segments a glob matches (measured 2026-09-29).
    private static bool OrdinalEquals(ReadOnlySpan<char> text, string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (text[index] != value[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool OrdinalStartsWith(ReadOnlySpan<char> text, string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (text[index] != value[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool OrdinalEndsWith(ReadOnlySpan<char> text, string value)
    {
        var offset = text.Length - value.Length;

        // Glob suffixes are short and mostly the same few lengths (".txt", ".log"):
        // the constant offsets let AOT drop the loop counter and the per-iteration
        // branch, which was 4.98 % of the workload profile.
        switch (value.Length)
        {
            case 4:
                return text[offset] == value[0]
                    && text[offset + 1] == value[1]
                    && text[offset + 2] == value[2]
                    && text[offset + 3] == value[3];
            case 3:
                return text[offset] == value[0]
                    && text[offset + 1] == value[1]
                    && text[offset + 2] == value[2];
            case 2:
                return text[offset] == value[0]
                    && text[offset + 1] == value[1];
            case 1:
                return text[offset] == value[0];
            default:
                for (var index = 0; index < value.Length; index++)
                {
                    if (text[offset + index] != value[index])
                    {
                        return false;
                    }
                }

                return true;
        }
    }

    private bool MatchTokens(Token[] tokens, ReadOnlySpan<char> text)
    {
        int tokenIndex = 0, textIndex = 0, starTokenIndex = -1, starTextIndex = 0;

        while (textIndex < text.Length)
        {
            if (tokenIndex < tokens.Length && tokens[tokenIndex].Kind != TokenKind.Star &&
                tokens[tokenIndex].Matches(text[textIndex], IgnoreCase))
            {
                tokenIndex++;
                textIndex++;
            }
            else if (tokenIndex < tokens.Length && tokens[tokenIndex].Kind == TokenKind.Star)
            {
                starTokenIndex = tokenIndex;
                starTextIndex = textIndex;
                tokenIndex++;
            }
            else if (starTokenIndex >= 0)
            {
                tokenIndex = starTokenIndex + 1;
                textIndex = ++starTextIndex;
            }
            else
            {
                return false;
            }
        }

        while (tokenIndex < tokens.Length && tokens[tokenIndex].Kind == TokenKind.Star)
        {
            tokenIndex++;
        }

        return tokenIndex == tokens.Length;
    }

    private static bool TryCompile(string pattern, out Segment[] segments)
    {
        var parts = pattern.Split('/');
        segments = new Segment[parts.Length];

        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            if (part == "**")
            {
                segments[index] = Segment.GlobStar;
                continue;
            }

            if (!TryCompileSegment(part, out var tokens))
            {
                segments = [];
                return false;
            }

            segments[index] = Segment.Of(tokens);
        }

        return true;
    }

    private static bool TryCompileSegment(string segment, out Token[] tokens)
    {
        var result = new List<Token>(segment.Length);
        var index = 0;

        while (index < segment.Length)
        {
            var character = segment[index];

            if (character == '*')
            {
                while (index < segment.Length && segment[index] == '*')
                {
                    index++;
                }

                result.Add(Token.Star);
            }
            else if (character == '?')
            {
                result.Add(Token.Any);
                index++;
            }
            else if (character == '[')
            {
                if (!TryCompileClass(segment, ref index, out var characterClass))
                {
                    tokens = [];
                    return false;
                }

                result.Add(Token.CharacterClass(characterClass));
            }
            else if (character == '\\')
            {
                index++;
                if (index >= segment.Length)
                {
                    tokens = [];
                    return false;
                }

                result.Add(Token.Literal(segment[index]));
                index++;
            }
            else
            {
                result.Add(Token.Literal(character));
                index++;
            }
        }

        tokens = result.ToArray();
        return true;
    }

    private static bool TryCompileClass(string segment, ref int index, out CharacterClass characterClass)
    {
        characterClass = null!;
        index++;

        var negated = false;
        if (index < segment.Length && (segment[index] == '!' || segment[index] == '^'))
        {
            negated = true;
            index++;
        }

        var ranges = new List<(char Start, char End)>();
        var first = true;

        while (index < segment.Length)
        {
            var character = segment[index];

            if (character == ']' && !first)
            {
                index++;
                characterClass = new CharacterClass(negated, ranges.ToArray());
                return true;
            }

            first = false;

            if (character == '\\' && index + 1 < segment.Length)
            {
                character = segment[index + 1];
                index += 2;
            }
            else
            {
                index++;
            }

            if (index + 1 < segment.Length && segment[index] == '-' && segment[index + 1] != ']')
            {
                var end = segment[index + 1];
                if (end == '\\' && index + 2 < segment.Length)
                {
                    end = segment[index + 2];
                    index += 3;
                }
                else
                {
                    index += 2;
                }

                if (character > end)
                {
                    return false;
                }

                ranges.Add((character, end));
            }
            else
            {
                ranges.Add((character, character));
            }
        }

        return false;
    }

    private static string BuildLiteral(Token[] tokens, int start, int end)
    {
        var length = end - start;
        if (length == 0)
        {
            return string.Empty;
        }

        return string.Create(
            length,
            (tokens, start),
            static (span, state) =>
            {
                for (var index = 0; index < span.Length; index++)
                {
                    span[index] = state.tokens[state.start + index].LiteralValue;
                }
            });
    }

    private static bool AllLiteral(Token[] tokens, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (tokens[index].Kind != TokenKind.Literal)
            {
                return false;
            }
        }

        return true;
    }

    private static int IndexOfStar(Token[] tokens)
    {
        for (var index = 0; index < tokens.Length; index++)
        {
            if (tokens[index].Kind == TokenKind.Star)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>One compiled path segment; its type is its match shape.</summary>
    /// <remarks>
    /// <para>
    /// The compiled shape is a subclass, not an enum field read by a <c>switch</c>
    /// at every match: matching is one virtual dispatch straight to the shape the
    /// pattern compiled to, with no kind reload and no branch cascade. That
    /// dispatch was the largest library frame of the workload profile (8.9 % of
    /// the total instructions).
    /// </para>
    /// <para>
    /// The shape owns its comparison rather than calling back into the pattern:
    /// the pattern carries the casing rule, so it is passed in, and the constant
    /// lengths are baked into the shape's own method.
    /// </para>
    /// </remarks>
    private abstract class Segment
    {
        protected Segment(Token[] tokens) => Tokens = tokens;

        /// <summary>A <c>**</c> segment: the globstar walks resolve it, never <see cref="Matches"/>.</summary>
        public static Segment GlobStar { get; } = new GlobStarSegment();

        public Token[] Tokens { get; }

        public virtual bool IsGlobStar => false;

        /// <summary>Matches this segment against one path segment.</summary>
        /// <param name="pattern">The owning pattern; supplies the casing rule.</param>
        /// <param name="text">The path segment.</param>
        /// <returns>Whether the segment matches.</returns>
        public abstract bool Matches(PathPattern pattern, ReadOnlySpan<char> text);

        /// <summary>
        /// Tries the trailing-only fast path for the last suffix segment of a
        /// single-globstar pattern.
        /// </summary>
        /// <param name="pattern">The owning pattern; supplies the casing rule.</param>
        /// <param name="path">The path from the globstar's middle start to its end.</param>
        /// <param name="isMatch">The result of the fast path when it is supported.</param>
        /// <returns>
        /// <see langword="true"/> when this shape can decide the match from the path tail alone;
        /// otherwise <see langword="false"/>, and the caller walks the segments backwards.
        /// </returns>
        /// <remarks>
        /// A shape returns <see langword="false"/> when it cannot prove that ending the path
        /// with its literal implies matching the last <c>/</c>-delimited segment.
        /// </remarks>
        public virtual bool TryMatchTail(PathPattern pattern, ReadOnlySpan<char> path, out bool isMatch)
        {
            isMatch = false;
            return false;
        }

        /// <summary>Classifies the shape from its compiled tokens.</summary>
        /// <param name="tokens">The segment's tokens.</param>
        /// <returns>The matching shape.</returns>
        public static Segment Of(Token[] tokens)
        {
            if (tokens.Length == 0 || AllLiteral(tokens, 0, tokens.Length))
            {
                return new LiteralSegment(BuildLiteral(tokens, 0, tokens.Length), tokens);
            }

            if (tokens[0].Kind == TokenKind.Star && AllLiteral(tokens, 1, tokens.Length))
            {
                return new StarSuffixSegment(BuildLiteral(tokens, 1, tokens.Length), tokens);
            }

            if (tokens[^1].Kind == TokenKind.Star && AllLiteral(tokens, 0, tokens.Length - 1))
            {
                return new PrefixStarSegment(BuildLiteral(tokens, 0, tokens.Length - 1), tokens);
            }

            var starIndex = IndexOfStar(tokens);
            if (starIndex > 0 && starIndex < tokens.Length - 1 &&
                AllLiteral(tokens, 0, starIndex) && AllLiteral(tokens, starIndex + 1, tokens.Length))
            {
                return new PrefixStarSuffixSegment(
                    BuildLiteral(tokens, 0, starIndex),
                    BuildLiteral(tokens, starIndex + 1, tokens.Length),
                    tokens);
            }

            // A leading character class followed by a star and a literal tail,
            // e.g. [a-z]* or [!x]*.log: the class matches the first character,
            // the star absorbs the middle and the tail is a suffix.
            if (tokens.Length >= 2 &&
                tokens[0].Kind == TokenKind.Class &&
                tokens[1].Kind == TokenKind.Star &&
                AllLiteral(tokens, 2, tokens.Length))
            {
                return new ClassStarSegment(BuildLiteral(tokens, 2, tokens.Length), tokens);
            }

            return new TokensSegment(tokens);
        }
    }

    private sealed class LiteralSegment(string value, Token[] tokens) : Segment(tokens)
    {
        public override bool Matches(PathPattern pattern, ReadOnlySpan<char> text) =>
            pattern.SegmentEquals(text, value);
    }

    private sealed class StarSuffixSegment(string suffix, Token[] tokens) : Segment(tokens)
    {
        public override bool Matches(PathPattern pattern, ReadOnlySpan<char> text) =>
            pattern.SegmentEndsWith(text, suffix);

        public override bool TryMatchTail(PathPattern pattern, ReadOnlySpan<char> path, out bool isMatch)
        {
            if (suffix.Length == 0)
            {
                // `**/*` has no literal, so the tail proves nothing about the
                // path's last segment: keep the backwards walk.
                isMatch = false;
                return false;
            }

            isMatch = pattern.SegmentEndsWith(path, suffix);
            return true;
        }
    }

    private sealed class PrefixStarSegment(string prefix, Token[] tokens) : Segment(tokens)
    {
        public override bool Matches(PathPattern pattern, ReadOnlySpan<char> text) =>
            pattern.SegmentStartsWith(text, prefix);
    }

    private sealed class PrefixStarSuffixSegment(string prefix, string suffix, Token[] tokens) : Segment(tokens)
    {
        public override bool Matches(PathPattern pattern, ReadOnlySpan<char> text) =>
            text.Length >= prefix.Length + suffix.Length &&
            pattern.SegmentStartsWith(text, prefix) &&
            pattern.SegmentEndsWith(text, suffix);
    }

    private sealed class ClassStarSegment(string suffix, Token[] tokens) : Segment(tokens)
    {
        public override bool Matches(PathPattern pattern, ReadOnlySpan<char> text) =>
            text.Length >= 1 + suffix.Length &&
            Tokens[0].ClassValue!.Matches(text[0], pattern.IgnoreCase) &&
            (suffix.Length == 0 || pattern.SegmentEndsWith(text, suffix));
    }

    private sealed class TokensSegment(Token[] tokens) : Segment(tokens)
    {
        public override bool Matches(PathPattern pattern, ReadOnlySpan<char> text) =>
            pattern.MatchTokens(Tokens, text);
    }

    /// <summary>A <c>**</c> segment: the globstar walks resolve it, never a text match.</summary>
    /// <remarks>
    /// Every walk answers it from <see cref="IsGlobStar"/> (zero or more path
    /// segments) and never reaches <see cref="Matches"/>. The body is unreachable by
    /// design; it exists only because the abstract contract has to be implemented.
    /// </remarks>
    private sealed class GlobStarSegment() : Segment([])
    {
        public override bool IsGlobStar => true;

        // Unreachable by design: no walk calls it (it answers from IsGlobStar).
        // Excluded so the 100 % floor keeps meaning "the reachable code is covered";
        // the exclusion is the type, not the file, and the type has no other member.
        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        public override bool Matches(PathPattern pattern, ReadOnlySpan<char> text) =>
            throw new InvalidOperationException("A globstar segment is resolved by the walk, never matched as text.");
    }

    private sealed class CharacterClass
    {
        private readonly (char Start, char End)[] _ranges;

        public CharacterClass(bool negated, (char Start, char End)[] ranges)
        {
            Negated = negated;
            _ranges = ranges;
        }

        public bool Negated { get; }

        public bool Matches(char character, bool ignoreCase)
        {
            var matched = false;

            foreach (var (start, end) in _ranges)
            {
                if (character >= start && character <= end)
                {
                    matched = true;
                    break;
                }

                if (ignoreCase)
                {
                    var lower = char.ToLowerInvariant(character);
                    var upper = char.ToUpperInvariant(character);
                    if ((lower >= start && lower <= end) || (upper >= start && upper <= end))
                    {
                        matched = true;
                        break;
                    }
                }
            }

            return Negated ? !matched : matched;
        }
    }

    private enum TokenKind
    {
        Literal,
        Any,
        Star,
        Class,
    }

    private readonly struct Token
    {
        private Token(TokenKind kind, char literal, CharacterClass? characterClass)
        {
            Kind = kind;
            LiteralValue = literal;
            ClassValue = characterClass;
        }

        public TokenKind Kind { get; }

        public char LiteralValue { get; }

        public CharacterClass? ClassValue { get; }

        public static Token Literal(char value) => new(TokenKind.Literal, value, null);

        public static Token Any => new(TokenKind.Any, '\0', null);

        public static Token Star => new(TokenKind.Star, '\0', null);

        public static Token CharacterClass(CharacterClass characterClass) => new(TokenKind.Class, '\0', characterClass);

        public bool Matches(char character, bool ignoreCase)
        {
            if (Kind == TokenKind.Any)
            {
                return true;
            }

            if (Kind == TokenKind.Literal)
            {
                return ignoreCase
                    ? char.ToLowerInvariant(character) == char.ToLowerInvariant(LiteralValue)
                    : character == LiteralValue;
            }

            return ClassValue!.Matches(character, ignoreCase);
        }
    }
}
