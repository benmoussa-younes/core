// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Text;

namespace Core.Tests;

public sealed class TextSearchTests
{
    [Fact]
    public void A_literal_search_is_case_insensitive_by_default()
    {
        var matches = TextSearch.FindAll("One one ONE", "one", SearchOptions.Default);

        Assert.Equal(3, matches.Count);
        Assert.Equal(0, matches[0].Start);
        Assert.Equal(3, matches[0].Length);
        Assert.Equal(3, matches[0].End);
    }

    [Fact]
    public void Match_case_and_whole_word_are_honoured()
    {
        var caseSensitive = TextSearch.FindAll("One one ONE", "one", new SearchOptions { MatchCase = true });
        Assert.Single(caseSensitive);
        Assert.Equal(4, caseSensitive[0].Start);

        var wholeWord = TextSearch.FindAll("one stone one_one", "one", new SearchOptions { WholeWord = true });
        Assert.Single(wholeWord);
        Assert.Equal(0, wholeWord[0].Start);
    }

    [Fact]
    public void A_literal_search_advances_by_one_character()
    {
        var matches = TextSearch.FindAll("aaa", "aa", SearchOptions.Default);

        Assert.Equal(2, matches.Count);
        Assert.Equal(0, matches[0].Start);
        Assert.Equal(1, matches[1].Start);
    }

    [Fact]
    public void A_regex_search_and_validation_work()
    {
        var options = new SearchOptions { UseRegex = true };

        Assert.Null(TextSearch.Validate(@"\d+", options));
        Assert.NotNull(TextSearch.Validate("(", options));
        Assert.Null(TextSearch.Validate("literal", SearchOptions.Default));

        var matches = TextSearch.FindAll("a1 b22", @"\d+", options);
        Assert.Equal(2, matches.Count);
        Assert.Equal(1, matches[0].Start);
        Assert.Equal(1, matches[0].Length);
        Assert.Equal(4, matches[1].Start);
        Assert.Equal(2, matches[1].Length);
    }

    [Fact]
    public void A_catastrophic_pattern_hits_the_match_timeout()
    {
        // The timeout is the guard that keeps an operator-supplied pattern from
        // hanging the window; 100k 'a' plus a non-matching tail is the textbook
        // backtracking case.
        var text = string.Concat(new string('a', 100_000), "!");

        Assert.Throws<System.Text.RegularExpressions.RegexMatchTimeoutException>(
            () => TextSearch.FindAll(text, "^(a+)+$", new SearchOptions { UseRegex = true }));
    }

    [Fact]
    public void FindFrom_returns_the_first_match_at_or_after_the_index()
    {
        var match = TextSearch.FindFrom("one two three", "t", SearchOptions.Default, 5);

        Assert.NotNull(match);
        Assert.Equal(8, match.Value.Start);
        Assert.Null(TextSearch.FindFrom("one", "z", SearchOptions.Default, 0));
    }

    [Fact]
    public void ReplaceAll_replaces_literals_and_regex_groups()
    {
        var literal = TextSearch.ReplaceAll("a b a", "a", "x", SearchOptions.Default);
        Assert.Equal("x b x", literal.Text);
        Assert.Equal(literal.Text.Length, literal.SelectionStart);
        Assert.Equal(0, literal.SelectionLength);

        var unchanged = TextSearch.ReplaceAll("abc", "z", "x", SearchOptions.Default);
        Assert.Equal("abc", unchanged.Text);

        var regex = TextSearch.ReplaceAll(
            "a1 b2",
            @"(\w)(\d)",
            "$2$1",
            new SearchOptions { UseRegex = true });
        Assert.Equal("1a 2b", regex.Text);
    }

    [Fact]
    public void ReplaceOne_replaces_the_first_match_from_an_index()
    {
        var literal = TextSearch.ReplaceOne("a a", "a", "x", SearchOptions.Default, 1);
        Assert.NotNull(literal);
        Assert.Equal("a x", literal.Value.Text);
        Assert.Equal(2, literal.Value.SelectionStart);
        Assert.Equal(1, literal.Value.SelectionLength);

        Assert.Null(TextSearch.ReplaceOne("a", "z", "x", SearchOptions.Default, 0));

        var regex = TextSearch.ReplaceOne("a1 b2", @"(\w)(\d)", "$2$1", new SearchOptions { UseRegex = true }, 0);
        Assert.NotNull(regex);
        Assert.Equal("1a b2", regex.Value.Text);
        Assert.Equal(0, regex.Value.SelectionStart);
        Assert.Equal(2, regex.Value.SelectionLength);

        Assert.Null(TextSearch.ReplaceOne("abc", @"z+", "x", new SearchOptions { UseRegex = true }, 0));
    }

    [Fact]
    public void An_empty_query_finds_and_replaces_nothing()
    {
        Assert.Empty(TextSearch.FindAll("abc", string.Empty, SearchOptions.Default));
        Assert.Null(TextSearch.FindFrom("abc", string.Empty, SearchOptions.Default, 0));
        Assert.Equal("abc", TextSearch.ReplaceAll("abc", string.Empty, "x", SearchOptions.Default).Text);
        Assert.Null(TextSearch.ReplaceOne("abc", string.Empty, "x", SearchOptions.Default, 0));
        Assert.Equal(
            "abc",
            TextSearch.ReplaceAll("abc", string.Empty, "x", new SearchOptions { UseRegex = true }).Text);
        Assert.Null(TextSearch.ReplaceOne("abc", string.Empty, "x", new SearchOptions { UseRegex = true }, 0));
    }

    [Fact]
    public void It_validates_every_argument()
    {
        Assert.Throws<ArgumentNullException>(() => TextSearch.Validate(null!, SearchOptions.Default));
        Assert.Throws<ArgumentNullException>(() => TextSearch.Validate("x", null!));

        Assert.Throws<ArgumentNullException>(() => TextSearch.FindAll(null!, "x", SearchOptions.Default));
        Assert.Throws<ArgumentNullException>(() => TextSearch.FindAll("x", null!, SearchOptions.Default));
        Assert.Throws<ArgumentNullException>(() => TextSearch.FindAll("x", "x", null!));

        Assert.Throws<ArgumentNullException>(() => TextSearch.FindFrom(null!, "x", SearchOptions.Default, 0));
        Assert.Throws<ArgumentNullException>(() => TextSearch.FindFrom("x", null!, SearchOptions.Default, 0));
        Assert.Throws<ArgumentNullException>(() => TextSearch.FindFrom("x", "x", null!, 0));

        Assert.Throws<ArgumentNullException>(() => TextSearch.ReplaceAll(null!, "x", "y", SearchOptions.Default));
        Assert.Throws<ArgumentNullException>(() => TextSearch.ReplaceAll("x", null!, "y", SearchOptions.Default));
        Assert.Throws<ArgumentNullException>(() => TextSearch.ReplaceAll("x", "x", null!, SearchOptions.Default));
        Assert.Throws<ArgumentNullException>(() => TextSearch.ReplaceAll("x", "x", "y", null!));

        Assert.Throws<ArgumentNullException>(() => TextSearch.ReplaceOne(null!, "x", "y", SearchOptions.Default, 0));
        Assert.Throws<ArgumentNullException>(() => TextSearch.ReplaceOne("x", null!, "y", SearchOptions.Default, 0));
        Assert.Throws<ArgumentNullException>(() => TextSearch.ReplaceOne("x", "x", null!, SearchOptions.Default, 0));
        Assert.Throws<ArgumentNullException>(() => TextSearch.ReplaceOne("x", "x", "y", null!, 0));
    }
}
