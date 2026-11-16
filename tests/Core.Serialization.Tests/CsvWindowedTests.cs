// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;
using Core.IO.Mapped;
using Core.Serialization.Codecs.Csv;
using Core.Serialization.Documents;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

/// <summary>
/// The windowed mapped read: a tiny window forces records, quotes and line endings
/// to straddle the window boundary, so the carry buffer is exercised.
/// </summary>
public sealed class CsvWindowedTests
{
    [Fact]
    public void The_windowed_read_matches_the_span_read()
    {
        const string csv = "a,b,c\n1,2.5,true\n3,,false\nhello,2026-01-01,4\n";

        var windowed = Read(csv, 3);
        var span = CsvDataReader.Read(Encoding.UTF8.GetBytes(csv)).Value;

        Assert.Equal(span.Root.RowCount, windowed.Root.RowCount);
        Assert.Equal(span.Root.ColumnCount, windowed.Root.ColumnCount);

        for (var column = 0; column < span.Root.ColumnCount; column++)
        {
            Assert.Equal(span.Root.Schema[column].Kind, windowed.Root.Schema[column].Kind);
        }

        for (var row = 0; row < span.Root.RowCount; row++)
        {
            for (var column = 0; column < span.Root.ColumnCount; column++)
            {
                Assert.Equal(span.Root[row, column], windowed.Root[row, column]);
            }
        }
    }

    [Fact]
    public void A_quoted_newline_may_span_windows()
    {
        var document = Read("t\n\"x\ny\"\n", 2);

        Assert.Equal(DataValue.From("x\ny"), document.Root[0, 0]);
    }

    [Fact]
    public void A_crlf_may_split_across_windows()
    {
        var document = Read("a,b\r\n1,2\r\n3,4\r\n", 3);

        Assert.Equal(2, document.Root.RowCount);
        Assert.Equal(DataValue.From(1L), document.Root[0, 0]);
        Assert.Equal(DataValue.From(4L), document.Root[1, 1]);
    }

    [Fact]
    public void A_quote_may_split_across_windows()
    {
        var document = Read("t\n\"ab\"\"cd\"\n", 3);

        Assert.Equal(DataValue.From("ab\"cd"), document.Root[0, 0]);
    }

    [Fact]
    public void A_headerless_windowed_file_reads_the_first_record()
    {
        var document = Read("1,2\n3,4\n", 3, new CsvDialect(hasHeader: false));

        Assert.Equal("Column1", document.Root.Schema[0].Name);
        Assert.Equal(2, document.Root.RowCount);
        Assert.Equal(DataValue.From(1L), document.Root[0, 0]);
    }

    [Fact]
    public void A_record_longer_than_the_window_grows_the_buffer()
    {
        var value = new string('x', 5000);
        var document = Read($"t\n\"{value}\"\n", 8);

        Assert.Equal(DataValue.From(value), document.Root[0, 0]);
    }

    [Fact]
    public void A_last_record_without_a_trailing_newline_is_read()
    {
        var document = Read("a,b\n1,2", 3);

        Assert.Equal(1, document.Root.RowCount);
        Assert.Equal(DataValue.From(2L), document.Root[0, 1]);
    }

    [Fact]
    public void An_empty_windowed_file_is_an_empty_document()
    {
        var document = Read(string.Empty, 3);

        Assert.Equal(0, document.Root.ColumnCount);
        Assert.Equal(0, document.Root.RowCount);
    }

    [Fact]
    public void An_uneven_windowed_row_is_refused()
    {
        var path = Write("a,b\n1\n");

        try
        {
            using var file = MappedFile.Open(path).Value;
            var result = CsvDataReader.ReadMapped(file, CsvDialect.Default, 3);

            Assert.Equal("serialization.uneven_row", result.Error.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Duplicate_windowed_header_names_are_refused()
    {
        var path = Write("a,a\n1,2\n");

        try
        {
            using var file = MappedFile.Open(path).Value;
            var result = CsvDataReader.ReadMapped(file, CsvDialect.Default, 3);

            Assert.Equal("serialization.invalid_document", result.Error.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_null_mapping_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => CsvDataReader.ReadMapped(null!, CsvDialect.Default, 3));
        Assert.Throws<ArgumentNullException>(() => CsvDataReader.ReadMapped(null!, CsvDialect.Default, 0));
    }

    [Fact]
    public void A_record_larger_than_the_maximum_is_refused()
    {
        // An unterminated quoted field makes the carry buffer grow without a record
        // boundary; the cap must stop it.
        var path = Write(string.Concat("a\n\"", new string('x', 256)));

        try
        {
            using var file = MappedFile.Open(path).Value;
            var result = CsvDataReader.ReadMapped(file, CsvDialect.Default, 3, maxRecordBytes: 64);

            Assert.Equal("serialization.too_large", result.Error.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_windowed_document_with_too_many_columns_is_refused()
    {
        var header = string.Join(',', Enumerable.Repeat("c", CsvDataReader.MaxColumns + 1));
        var path = Write(string.Concat(header, "\n"));

        try
        {
            using var file = MappedFile.Open(path).Value;
            var result = CsvDataReader.ReadMapped(file, CsvDialect.Default, 3);

            Assert.Equal("serialization.too_many_columns", result.Error.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_windowed_read_skips_a_leading_bom()
    {
        var document = Read("\uFEFFa,b\n1,2\n", 3);

        Assert.Equal("a", document.Root.Schema[0].Name);
    }

    private static DataDocument Read(string csv, int chunk, CsvDialect? dialect = null)
    {
        var path = Write(csv);

        try
        {
            using var file = MappedFile.Open(path).Value;
            var result = CsvDataReader.ReadMapped(file, dialect ?? CsvDialect.Default, chunk);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);

            return result.Value;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Write(string csv)
    {
        var path = Path.Combine(Path.GetTempPath(), "core-windowed-" + Guid.NewGuid().ToString("N") + ".csv");
        File.WriteAllText(path, csv);

        return path;
    }
}
