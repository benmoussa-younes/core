// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Text;

namespace Core.Tests;

public sealed class LineEndingTests
{
    [Theory]
    [InlineData("a\nb\n", LineEnding.LineFeed)]
    [InlineData("a\r\nb\r\n", LineEnding.CarriageReturnLineFeed)]
    [InlineData("a\rb\r", LineEnding.CarriageReturn)]
    [InlineData("no line break", LineEnding.LineFeed)]
    [InlineData("", LineEnding.LineFeed)]
    public void It_detects_the_uniform_style(string text, LineEnding expected)
    {
        Assert.Equal(expected, LineEndingDetector.Detect(text));
    }

    [Fact]
    public void A_mixed_text_takes_the_majority_and_a_tie_is_line_feed()
    {
        Assert.Equal(LineEnding.CarriageReturnLineFeed, LineEndingDetector.Detect("a\r\nb\r\nc\n"));
        Assert.Equal(LineEnding.CarriageReturn, LineEndingDetector.Detect("a\rb\rc\r\n"));
        Assert.Equal(LineEnding.LineFeed, LineEndingDetector.Detect("a\r\nb\n"));
        Assert.Equal(LineEnding.LineFeed, LineEndingDetector.Detect("a\rb\n"));
    }

    [Fact]
    public void It_normalizes_every_style_to_line_feed()
    {
        Assert.Equal("a\nb", LineEndingConverter.ToLineFeed("a\r\nb"));
        Assert.Equal("a\nb", LineEndingConverter.ToLineFeed("a\rb"));
        Assert.Equal("a\nb", LineEndingConverter.ToLineFeed("a\nb"));
        Assert.Equal("a\nb\nc", LineEndingConverter.ToLineFeed("a\r\nb\rc"));
    }

    [Fact]
    public void It_restores_the_requested_endings()
    {
        const string text = "a\nb";

        Assert.Equal("a\nb", LineEndingConverter.FromLineFeed(text, LineEnding.LineFeed));
        Assert.Equal("a\r\nb", LineEndingConverter.FromLineFeed(text, LineEnding.CarriageReturnLineFeed));
        Assert.Equal("a\rb", LineEndingConverter.FromLineFeed(text, LineEnding.CarriageReturn));
        Assert.Equal(text, LineEndingConverter.FromLineFeed(text, (LineEnding)99));
    }

    [Fact]
    public void The_empty_document_is_line_feed_without_a_bom()
    {
        Assert.Equal(string.Empty, TextDocument.Empty.Text);
        Assert.Equal(LineEnding.LineFeed, TextDocument.Empty.LineEnding);
        Assert.False(TextDocument.Empty.HasBom);
    }

    [Fact]
    public void It_rejects_null_text()
    {
        Assert.Throws<ArgumentNullException>(() => LineEndingDetector.Detect(null!));
        Assert.Throws<ArgumentNullException>(() => LineEndingConverter.ToLineFeed(null!));
        Assert.Throws<ArgumentNullException>(() => LineEndingConverter.FromLineFeed(null!, LineEnding.LineFeed));
    }
}
