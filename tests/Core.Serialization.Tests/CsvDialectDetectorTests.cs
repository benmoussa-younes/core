// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;
using Core.Serialization.Codecs.Csv;

namespace Core.Serialization.Tests;

/// <summary>
/// The delimiter detector decides how a file is read before a single row is
/// parsed, so every case here is load-bearing: a wrong guess corrupts the whole
/// sheet. Ported from records' detector tests.
/// </summary>
public sealed class CsvDialectDetectorTests
{
    [Fact]
    public void Detect_CommaSeparated_ChoosesComma()
    {
        var dialect = Detect("a,b,c\n1,2,3\n4,5,6\n");

        Assert.Equal(',', dialect.Delimiter);
    }

    [Fact]
    public void Detect_SemicolonSeparated_ChoosesSemicolon()
    {
        var dialect = Detect("a;b;c\n1;2;3\n4;5;6\n");

        Assert.Equal(';', dialect.Delimiter);
    }

    [Fact]
    public void Detect_TabSeparated_ChoosesTab()
    {
        var dialect = Detect("a\tb\tc\n1\t2\t3\n");

        Assert.Equal('\t', dialect.Delimiter);
    }

    [Fact]
    public void Detect_PipeSeparated_ChoosesPipe()
    {
        var dialect = Detect("a|b|c\n1|2|3\n");

        Assert.Equal('|', dialect.Delimiter);
    }

    [Fact]
    public void Detect_QuotedDelimiter_IgnoresDelimiterInsideQuotes()
    {
        // One logical column contains a comma inside quotes; the real delimiter
        // is the semicolon, which produces one consistent field count.
        var dialect = Detect("name;city\n\"Doe, Jane\";Rabat\n\"Roe, John\";Fes\n");

        Assert.Equal(';', dialect.Delimiter);
    }

    [Fact]
    public void Detect_SingleColumn_FallsBackToDefault()
    {
        var dialect = Detect("header\nvalue\nother\n");

        Assert.Equal(',', dialect.Delimiter);
    }

    [Fact]
    public void Detect_EmptyText_ReturnsDefault()
    {
        var dialect = CsvDialectDetector.Detect(ReadOnlySpan<byte>.Empty);

        Assert.Equal(CsvDialect.Default, dialect);
    }

    [Fact]
    public void Detect_DefaultQuoteAndHeader_AreAssumed()
    {
        var dialect = Detect("a,b\n1,2\n");

        Assert.Equal('"', dialect.Quote);
        Assert.True(dialect.HasHeader);
    }

    [Fact]
    public void Detect_IgnoresBlankLines()
    {
        var dialect = Detect("a;b\n\n1;2\n\n3;4\n");

        Assert.Equal(';', dialect.Delimiter);
    }

    [Fact]
    public void Detect_Crlf_IsHandled()
    {
        var dialect = Detect("a;b\r\n1;2\r\n3;4\r\n");

        Assert.Equal(';', dialect.Delimiter);
    }

    [Fact]
    public void Detect_OnAHugeFile_OnlyReadsTheBoundedSample()
    {
        // The head is semicolon-separated and longer than the sample; the tail is
        // comma-separated. If detection scanned the whole file the two delimiters
        // would tie and the default (comma) would win; reading only the sample
        // decides for the semicolon.
        var builder = new StringBuilder();
        for (var index = 0; index < 20_000; index++)
        {
            builder.Append("a;b;c\n");
        }

        for (var index = 0; index < 20_000; index++)
        {
            builder.Append("a,b,c\n");
        }

        Assert.Equal(';', Detect(builder.ToString()).Delimiter);
    }

    [Fact]
    public void Detect_EscapedQuote_IsHandled()
    {
        // A doubled quote inside a quoted field is one literal quote, so the
        // semicolon after it is data and the field count stays consistent.
        var dialect = Detect("h1;h2\n\"a \"\"b\"\";c\";d\n");

        Assert.Equal(';', dialect.Delimiter);
    }

    [Fact]
    public void Detect_ReportsTheBoundedSampleLength()
    {
        Assert.Equal(64 * 1024, CsvDialectDetector.MaximumSampleLength);
    }

    private static CsvDialect Detect(string csv) => CsvDialectDetector.Detect(Encoding.UTF8.GetBytes(csv));
}
