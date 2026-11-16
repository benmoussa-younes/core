// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Primitives;

namespace Core.Tests;

public sealed class PathPatternTests
{
    [Fact]
    public void Create_rejects_a_null_pattern()
    {
        Assert.Throws<ArgumentNullException>(() => PathPattern.Create(null!));
    }

    [Theory]
    [InlineData("[abc")]
    [InlineData("[z-a]")]
    [InlineData("a\\")]
    public void Create_rejects_invalid_patterns(string pattern)
    {
        var result = PathPattern.Create(pattern);

        Assert.True(result.IsFailure);
        Assert.Equal("pathpattern.invalid", result.Error.Code);
    }

    [Fact]
    public void Exposes_the_original_pattern_and_casing()
    {
        var pattern = Pattern("*.TXT", ignoreCase: true);

        Assert.Equal("*.TXT", pattern.Pattern);
        Assert.Equal("*.TXT", pattern.ToString());
        Assert.True(pattern.IgnoreCase);
    }

    [Theory]
    [InlineData("*.txt", "a.txt", true)]
    [InlineData("*.txt", "a.md", false)]
    [InlineData("*.txt", "a/b.txt", false)]
    [InlineData("a?c", "abc", true)]
    [InlineData("a?c", "ac", false)]
    [InlineData("a?c", "a/c", false)]
    [InlineData("[abc].txt", "a.txt", true)]
    [InlineData("[abc].txt", "d.txt", false)]
    [InlineData("[a-c].txt", "b.txt", true)]
    [InlineData("[a-c].txt", "d.txt", false)]
    [InlineData("[!a].txt", "b.txt", true)]
    [InlineData("[!a].txt", "a.txt", false)]
    [InlineData("[^a].txt", "a.txt", false)]
    [InlineData("[!ab].txt", "c.txt", true)]
    [InlineData("[!ab].txt", "b.txt", false)]
    [InlineData("[!a-c].txt", "d.txt", true)]
    [InlineData("[!a-c].txt", "b.txt", false)]
    [InlineData("a\\*", "a*", true)]
    [InlineData("a\\*", "ab", false)]
    [InlineData("", "", true)]
    [InlineData("", "a", false)]
    [InlineData("a/b", "a/b", true)]
    [InlineData("a/b", "a/c", false)]
    [InlineData("a/b", "a/b/c", false)]
    [InlineData("a", "a/b", false)]
    [InlineData("a/b", "a", false)]
    [InlineData("a", "a/", false)]
    [InlineData("", "/", false)]
    [InlineData("abcd*efgh", "abef", false)]
    [InlineData("a*", "a", true)]
    [InlineData("a*b", "axb", true)]
    [InlineData("a*b", "ax", false)]
    [InlineData("[ab]*x", "aXx", true)]
    [InlineData("[ab]*x", "aXy", false)]
    [InlineData("[ab]x*", "ax", true)]
    [InlineData("[a-z]*", "abc", true)]
    [InlineData("[a-z]*", "", false)]
    [InlineData("[a-z]*", "1abc", false)]
    [InlineData("[a-z]*", "a/b", false)]
    [InlineData("[!x]*.log", "a.log", true)]
    [InlineData("[!x]*.log", "x.log", false)]
    [InlineData("[!x]*.log", ".log", false)]
    [InlineData("[a]*a", "a", false)]
    [InlineData("[a]*a", "aa", true)]
    [InlineData("*a*b", "xayb", true)]
    [InlineData("*a*b", "xy", false)]
    [InlineData("a/b", "ab", false)]
    [InlineData("abc*", "ab", false)]
    [InlineData("abc*", "abd", false)]
    [InlineData("*.txt", "a", false)]
    [InlineData("*.x", "a.x", true)]
    [InlineData("*.x", "a.y", false)]
    [InlineData("*.md", "a.md", true)]
    [InlineData("*.md", "a.mx", false)]
    [InlineData("*.htm", "a.htm", true)]
    [InlineData("*.htm", "a.htx", false)]
    [InlineData("*.json", "a.json", true)]
    [InlineData("*.json", "a.jsom", false)]
    [InlineData("[\\a]", "a", true)]
    [InlineData("[a-\\c]", "b", true)]
    public void Matches_literal_wildcards_and_classes(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, Pattern(pattern).IsMatch(path));
    }

    [Theory]
    [InlineData("**", "", true)]
    [InlineData("**", "a/b/c", true)]
    [InlineData("**/*.txt", "a.txt", true)]
    [InlineData("**/*.txt", "a/b/c.txt", true)]
    [InlineData("**/*.txt", "a/b/c.md", false)]
    [InlineData("a/**", "a", true)]
    [InlineData("a/**", "a/b/c", true)]
    [InlineData("a/**", "b/x", false)]
    [InlineData("a/**/b", "a/b", true)]
    [InlineData("a/**/b", "a/x/y/b", true)]
    [InlineData("a/**/b", "a/x/y/c", false)]
    [InlineData("a/**/b", "a/b/c", false)]
    [InlineData("**/b", "b", true)]
    [InlineData("**/b", "a/b", true)]
    [InlineData("a/**/**", "a", true)]
    [InlineData("a/**/**", "a/b", true)]
    [InlineData("**/a/**/**", "a", true)]
    [InlineData("**/a/b", "x/a", false)]
    [InlineData("a/**/b", "a", false)]
    public void Matches_globstar_as_zero_or_more_segments(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, Pattern(pattern).IsMatch(path));
    }

    [Theory]
    [InlineData("**/*.log", "a.log", true)]
    [InlineData("**/*.log", "x/a.log", true)]
    [InlineData("**/*.log", "x/y/a.log", true)]
    [InlineData("**/*.log", "x/ba.log", true)]
    [InlineData("**/*.log", ".log", true)]
    [InlineData("**/*.log", "x/.log", true)]
    [InlineData("**/*.log", "a.log/", false)]
    [InlineData("**/*.log", "x/a.log.md", false)]
    [InlineData("**/*.log", "x/a.lo", false)]
    [InlineData("a/**/*.log", "a/b/c.log", true)]
    [InlineData("a/**/*.log", "b/a.log", false)]
    [InlineData("**/*", "", true)]
    [InlineData("**/*", "a", true)]
    [InlineData("**/*", "a/b", true)]
    [InlineData("a*/**/*", "abc", false)]
    [InlineData("**/x/*.log", "a/x/b.log", true)]
    public void Trailing_suffix_fast_path_keeps_the_globstar_answers(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, Pattern(pattern).IsMatch(path));
    }

    [Fact]
    public void Trailing_suffix_fast_path_honors_ignore_case()
    {
        Assert.True(Pattern("**/*.TXT", ignoreCase: true).IsMatch("a/b.txt"));
    }

    [Fact]
    public void A_many_globstar_pattern_resolves_every_globstar_segment_without_comparing_one()
    {
        // The many-globstar memo walk answers a `**` pattern segment from the
        // globstar branch (it spans zero or more path segments) and never asks the
        // compiled globstar segment to compare text: that branch is what the walk
        // owns, so the segment's own match is the unreachable-by-design body.
        Assert.True(Pattern("a/b/**/c/**/d").IsMatch("a/b/c/d"));
        Assert.True(Pattern("a/b/**/c/**/d").IsMatch("a/b/x/c/y/z/d"));
        Assert.False(Pattern("a/b/**/c/**/d").IsMatch("a/b/c"));
    }

    [Fact]
    public void Create_rejects_a_pattern_longer_than_the_limit()
    {
        var result = PathPattern.Create(new string('a', 5000));

        Assert.True(result.IsFailure);
        Assert.Equal("pathpattern.invalid", result.Error.Code);
    }

    [Fact]
    public void IsMatch_rejects_a_path_longer_than_the_limit()
    {
        var pattern = Pattern("a");

        Assert.False(pattern.IsMatch(new string('a', 5000)));
    }

    [Fact]
    public void Globstar_uses_a_pooled_memo_for_many_segments()
    {
        var pattern = Pattern("**/**/x");
        var path = string.Join('/', Enumerable.Repeat("s", 699)) + "/x";

        Assert.True(pattern.IsMatch(path));
    }

    [Fact]
    public void IgnoreCase_controls_casing()
    {
        Assert.False(Pattern("*.txt").IsMatch("A.TXT"));
        Assert.True(Pattern("*.txt", ignoreCase: true).IsMatch("A.TXT"));
        Assert.True(Pattern("[a-c].txt", ignoreCase: true).IsMatch("B.txt"));
        Assert.False(Pattern("[a-c].txt", ignoreCase: true).IsMatch("F.txt"));
    }

    [Fact]
    public void Pathological_globstar_patterns_terminate()
    {
        var pattern = Pattern("**/**/**/**/**/**/missing");

        Assert.False(pattern.IsMatch("a/b/c/d/e/f/g/h/i/j"));
    }

    private static PathPattern Pattern(string pattern, bool ignoreCase = false)
    {
        var result = PathPattern.Create(pattern, ignoreCase);

        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
