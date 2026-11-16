// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;
using System.Text.RegularExpressions;

namespace Core.Text;

/// <summary>
/// The pure find/replace engine: text in, matches or edited text out. The
/// regular-expression path is never compiled, because <c>RegexOptions.Compiled</c>
/// needs runtime code generation and the shipping form is Native AOT; every
/// pattern gets a match timeout so a pathological query cannot hang a window.
/// </summary>
/// <remarks>
/// For a pattern that comes from the operator, call <see cref="Validate"/> first
/// and show its message; a valid but catastrophic pattern still reaches the
/// timeout, which every search method surfaces as
/// <see cref="RegexMatchTimeoutException"/>.
/// </remarks>
public static class TextSearch
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Checks a query before it is used.</summary>
    /// <param name="query">The query.</param>
    /// <param name="options">The search rules.</param>
    /// <returns>An error message, or <see langword="null"/> when the query is usable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static string? Validate(string query, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.UseRegex || query.Length == 0)
        {
            return null;
        }

        try
        {
            _ = BuildRegex(query, options);
            return null;
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }
    }

    /// <summary>Finds every match.</summary>
    /// <param name="text">The text.</param>
    /// <param name="query">The query.</param>
    /// <param name="options">The search rules.</param>
    /// <returns>The matches, in order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/>, <paramref name="query"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="RegexMatchTimeoutException">The pattern exceeded its match timeout.</exception>
    public static IReadOnlyList<TextMatch> FindAll(string text, string query, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);

        if (query.Length == 0)
        {
            return [];
        }

        return options.UseRegex ? FindAllRegex(text, query, options) : FindAllLiteral(text, query, options);
    }

    /// <summary>Finds the first match at or after an index.</summary>
    /// <param name="text">The text.</param>
    /// <param name="query">The query.</param>
    /// <param name="options">The search rules.</param>
    /// <param name="from">The index to search from.</param>
    /// <returns>The match, or <see langword="null"/> when there is none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/>, <paramref name="query"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="RegexMatchTimeoutException">The pattern exceeded its match timeout.</exception>
    public static TextMatch? FindFrom(string text, string query, SearchOptions options, int from)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);

        foreach (var match in FindAll(text, query, options))
        {
            if (match.Start >= from)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>Replaces every match.</summary>
    /// <param name="text">The text.</param>
    /// <param name="query">The query.</param>
    /// <param name="replacement">The replacement; in regular-expression mode it may name groups.</param>
    /// <param name="options">The search rules.</param>
    /// <returns>The edited text and the caret after it.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="RegexMatchTimeoutException">The pattern exceeded its match timeout.</exception>
    public static TextReplacement ReplaceAll(string text, string query, string replacement, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(options);

        if (query.Length == 0)
        {
            return new TextReplacement(text, text.Length, 0);
        }

        if (options.UseRegex)
        {
            var result = BuildRegex(query, options).Replace(text, replacement);
            return new TextReplacement(result, result.Length, 0);
        }

        var matches = FindAllLiteral(text, query, options);
        if (matches.Count == 0)
        {
            return new TextReplacement(text, text.Length, 0);
        }

        var builder = new StringBuilder(text.Length);
        var position = 0;

        foreach (var match in matches)
        {
            builder.Append(text, position, match.Start - position);
            builder.Append(replacement);
            position = match.End;
        }

        builder.Append(text, position, text.Length - position);
        var replaced = builder.ToString();
        return new TextReplacement(replaced, replaced.Length, 0);
    }

    /// <summary>Replaces the first match at or after an index.</summary>
    /// <param name="text">The text.</param>
    /// <param name="query">The query.</param>
    /// <param name="replacement">The replacement; in regular-expression mode it may name groups.</param>
    /// <param name="options">The search rules.</param>
    /// <param name="from">The index to search from.</param>
    /// <returns>The edited text and the selection, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="RegexMatchTimeoutException">The pattern exceeded its match timeout.</exception>
    public static TextReplacement? ReplaceOne(
        string text,
        string query,
        string replacement,
        SearchOptions options,
        int from)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(options);

        if (query.Length == 0)
        {
            return null;
        }

        if (options.UseRegex)
        {
            var match = BuildRegex(query, options).Match(text, Math.Clamp(from, 0, text.Length));

            if (!match.Success)
            {
                return null;
            }

            var expanded = match.Result(replacement);
            var value = string.Concat(text.AsSpan(0, match.Index), expanded, text.AsSpan(match.Index + match.Length));
            return new TextReplacement(value, match.Index, expanded.Length);
        }

        var found = FindFrom(text, query, options, from);
        if (found is not { } literal)
        {
            return null;
        }

        var replaced = string.Concat(text.AsSpan(0, literal.Start), replacement, text.AsSpan(literal.End));
        return new TextReplacement(replaced, literal.Start, replacement.Length);
    }

    private static Regex BuildRegex(string pattern, SearchOptions options)
    {
        var effective = options.WholeWord ? string.Concat(@"\b(?:", pattern, @")\b") : pattern;
        var regexOptions = options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        return new Regex(effective, regexOptions, MatchTimeout);
    }

    private static List<TextMatch> FindAllRegex(string text, string query, SearchOptions options)
    {
        var matches = new List<TextMatch>();

        foreach (Match match in BuildRegex(query, options).Matches(text))
        {
            matches.Add(new TextMatch(match.Index, match.Length));
        }

        return matches;
    }

    private static List<TextMatch> FindAllLiteral(string text, string query, SearchOptions options)
    {
        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var matches = new List<TextMatch>();
        var index = 0;

        while (index <= text.Length - query.Length)
        {
            var found = text.IndexOf(query, index, comparison);
            if (found < 0)
            {
                break;
            }

            if (!options.WholeWord || IsWholeWord(text, found, query.Length))
            {
                matches.Add(new TextMatch(found, query.Length));
            }

            index = found + 1;
        }

        return matches;
    }

    private static bool IsWholeWord(string text, int start, int length)
    {
        var before = start == 0 || !IsWordCharacter(text[start - 1]);
        var after = start + length >= text.Length || !IsWordCharacter(text[start + length]);
        return before && after;
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';
}
