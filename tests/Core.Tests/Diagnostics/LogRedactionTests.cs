// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Diagnostics;

namespace Core.Tests;

public sealed class LogRedactionTests
{
    [Fact]
    public void Plain_text_passes_through()
    {
        Assert.Equal("plain ascii", LogRedaction.SingleLine("plain ascii"));
    }

    [Theory]
    [InlineData("a\nb", "a\\nb")]
    [InlineData("a\rb", "a\\rb")]
    [InlineData("a\r\nb", "a\\r\\nb")]
    [InlineData("a\tb", "a\\tb")]
    [InlineData("a\0b", "a\\u0000b")]
    [InlineData("a\u001fb", "a\\u001Fb")]
    public void A_control_character_is_escaped(string value, string expected)
    {
        Assert.Equal(expected, LogRedaction.SingleLine(value));
    }

    [Theory]
    [InlineData("a\u2028b")]
    [InlineData("a\u2029b")]
    [InlineData("a\u202Eb")]
    [InlineData("a\u2066b")]
    public void Unicode_line_and_bidi_controls_are_escaped(string value)
    {
        var redacted = LogRedaction.SingleLine(value);

        Assert.DoesNotContain('\u2028', redacted);
        Assert.DoesNotContain('\u2029', redacted);
        Assert.DoesNotContain('\u202E', redacted);
        Assert.DoesNotContain('\u2066', redacted);
    }

    [Fact]
    public void The_output_is_capped_at_the_requested_length()
    {
        Assert.Equal("aaaaa", LogRedaction.SingleLine(new string('a', 10), 5));
    }

    [Fact]
    public void An_escape_that_does_not_fit_is_dropped_rather_than_written_partially()
    {
        var redacted = LogRedaction.SingleLine("aaaa\0", 5);

        Assert.Equal("aaaa", redacted);
        Assert.True(redacted.Length <= 5);
    }

    [Fact]
    public void Null_and_empty_are_an_empty_line()
    {
        Assert.Equal(string.Empty, LogRedaction.SingleLine(null));
        Assert.Equal(string.Empty, LogRedaction.SingleLine(string.Empty));
    }

    [Fact]
    public void A_non_positive_maximum_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LogRedaction.SingleLine("x", 0));
    }

    [Fact]
    public void The_null_logger_discards_entries()
    {
        NullApplicationLogger.Instance.Log(LogLevel.Debug, "ignored");
    }
}
