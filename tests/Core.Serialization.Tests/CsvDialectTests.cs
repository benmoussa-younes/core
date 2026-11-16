// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;
using Core.Serialization.Codecs.Csv;
using Core.Serialization.Documents;
using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class CsvDialectTests
{
    [Fact]
    public void A_semicolon_delimiter_round_trips()
    {
        var dialect = new CsvDialect(delimiter: ';');
        var document = Read("a;b\n1;x,y\n", dialect);

        Assert.Equal(2, document.Root.ColumnCount);
        Assert.Equal(DataValue.From("x,y"), document.Root[0, 1]);

        // The comma inside the field is not the delimiter, so it is written raw and
        // read back as one field.
        Assert.Equal("a;b\n1;x,y\n", Write(document, dialect));
    }

    [Fact]
    public void A_tab_delimiter_is_honoured()
    {
        var document = Read("a\tb\n1\t2\n", new CsvDialect(delimiter: '\t'));

        Assert.Equal(2, document.Root.ColumnCount);
        Assert.Equal(DataValue.From(2L), document.Root[0, 1]);
    }

    [Fact]
    public void A_headerless_file_names_the_columns_and_reads_the_first_record()
    {
        var document = Read("1,2\n3,4\n", new CsvDialect(hasHeader: false));

        Assert.Equal("Column1", document.Root.Schema[0].Name);
        Assert.Equal("Column2", document.Root.Schema[1].Name);
        Assert.Equal(2, document.Root.RowCount);
        Assert.Equal(DataValue.From(1L), document.Root[0, 0]);
        Assert.Equal(DataValue.From(4L), document.Root[1, 1]);
    }

    [Fact]
    public void A_headerless_file_is_written_without_a_header()
    {
        var dialect = new CsvDialect(hasHeader: false);
        var document = Read("1,2\n3,4\n", dialect);

        Assert.Equal("1,2\n3,4\n", Write(document, dialect));
    }

    [Fact]
    public void A_headed_dialect_still_writes_the_names()
    {
        var table = new DataTable("root", new DataSchema([new DataField("a", DataKind.Integer), new DataField("b", DataKind.Integer)]));
        table.AddRow([DataValue.From(1L), DataValue.From(2L)]);

        Assert.Equal("a,b\n1,2\n", Write(DataDocument.FromTable(table), CsvDialect.Default));
    }

    [Fact]
    public void A_date_writes_as_yyyy_MM_dd()
    {
        var table = new DataTable("root", new DataSchema([new DataField("d", DataKind.Date)]));
        table.AddRow([DataValue.From(new DateOnly(2026, 1, 2))]);

        Assert.Equal("d\n2026-01-02\n", Write(DataDocument.FromTable(table), CsvDialect.Default));
        Assert.Equal(DataValue.From(new DateOnly(2026, 1, 2)), Read("d\n2026-01-02\n", CsvDialect.Default).Root[0, 0]);
    }

    [Fact]
    public void A_whole_second_time_writes_without_a_fraction()
    {
        var table = new DataTable("root", new DataSchema([new DataField("t", DataKind.Time)]));
        table.AddRow([DataValue.From(new TimeOnly(3, 4, 5))]);

        Assert.Equal("t\n03:04:05\n", Write(DataDocument.FromTable(table), CsvDialect.Default));
        Assert.Equal(DataValue.From(new TimeOnly(3, 4, 5)), Read("t\n03:04:05\n", CsvDialect.Default).Root[0, 0]);
    }

    [Fact]
    public void A_sub_second_time_trims_trailing_zeros()
    {
        var time = new TimeOnly(3, 4, 5).Add(TimeSpan.FromTicks(1_230_000));
        var table = new DataTable("root", new DataSchema([new DataField("t", DataKind.Time)]));
        table.AddRow([DataValue.From(time)]);

        Assert.Equal("t\n03:04:05.123\n", Write(DataDocument.FromTable(table), CsvDialect.Default));
        Assert.Equal(DataValue.From(time), Read("t\n03:04:05.123\n", CsvDialect.Default).Root[0, 0]);
    }

    [Fact]
    public void A_whole_second_timestamp_writes_as_naive_iso()
    {
        var table = new DataTable("root", new DataSchema([new DataField("ts", DataKind.Timestamp)]));
        table.AddRow([DataValue.From(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc))]);

        Assert.Equal("ts\n2026-01-02T03:04:05\n", Write(DataDocument.FromTable(table), CsvDialect.Default));
        Assert.Equal(DataValue.From(new DateTime(2026, 1, 2, 3, 4, 5)), Read("ts\n2026-01-02T03:04:05\n", CsvDialect.Default).Root[0, 0]);
    }

    [Fact]
    public void A_sub_second_timestamp_trims_trailing_zeros()
    {
        var timestamp = new DateTime(2026, 1, 2, 3, 4, 5).AddTicks(1_230_000);
        var table = new DataTable("root", new DataSchema([new DataField("ts", DataKind.Timestamp)]));
        table.AddRow([DataValue.From(timestamp)]);

        Assert.Equal("ts\n2026-01-02T03:04:05.123\n", Write(DataDocument.FromTable(table), CsvDialect.Default));
        Assert.Equal(DataValue.From(timestamp), Read("ts\n2026-01-02T03:04:05.123\n", CsvDialect.Default).Root[0, 0]);
    }

    [Fact]
    public void A_crlf_line_ending_is_written()
    {
        var table = new DataTable("root", new DataSchema([new DataField("a", DataKind.Integer)]));
        table.AddRow([DataValue.From(1L)]);

        var result = CsvDataWriter.WriteToUtf8Bytes(DataDocument.FromTable(table), new CsvDialect(lineEnding: "\r\n"));

        Assert.True(result.IsSuccess);
        Assert.Equal("a\r\n1\r\n", Encoding.UTF8.GetString(result.Value));
    }

    [Fact]
    public void A_byte_order_mark_is_written_when_requested_and_skipped_on_read()
    {
        var table = new DataTable("root", new DataSchema([new DataField("a", DataKind.Integer)]));
        table.AddRow([DataValue.From(1L)]);

        var written = CsvDataWriter.WriteToUtf8Bytes(DataDocument.FromTable(table), new CsvDialect(writeBom: true));
        Assert.True(written.IsSuccess);
        Assert.Equal([0xEF, 0xBB, 0xBF, (byte)'a'], written.Value[..4]);

        var back = CsvDataReader.Read(written.Value);
        Assert.Equal(DataValue.From(1L), back.Value.Root[0, 0]);
    }

    [Fact]
    public void A_custom_quote_is_honoured()
    {
        var document = Read("t\n'x,y'\n", new CsvDialect(quote: '\''));

        Assert.Equal(DataValue.From("x,y"), document.Root[0, 0]);
    }

    [Fact]
    public void Quoting_can_be_disabled()
    {
        var document = Read("t\n\"x\"\n", new CsvDialect(quote: '\0'));

        // With quoting off, the quote character is ordinary data.
        Assert.Equal(DataValue.From("\"x\""), document.Root[0, 0]);
    }

    [Theory]
    [InlineData('\n', '"', "\n")] // delimiter is a newline
    [InlineData('a', 'a', "\n")] // quote equals the delimiter
    [InlineData(',', '"', "x")] // unsupported line ending
    public void An_invalid_dialect_is_rejected(char delimiter, char quote, string lineEnding)
    {
        Assert.Throws<ArgumentException>(() => new CsvDialect(delimiter, quote, lineEnding: lineEnding));
    }

    [Fact]
    public void A_non_ascii_delimiter_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new CsvDialect(delimiter: '\u00e9'));
    }

    [Theory]
    [InlineData('\u00e9')]
    [InlineData('\n')]
    [InlineData('\r')]
    public void An_invalid_quote_is_rejected(char quote)
    {
        Assert.Throws<ArgumentException>(() => new CsvDialect(quote: quote));
    }

    [Fact]
    public void A_null_dialect_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => CsvDataReader.Read("a\n"u8, null!));
        Assert.Throws<ArgumentNullException>(
            () => CsvDataWriter.WriteToUtf8Bytes(DataDocument.FromTable(new DataTable("root", DataSchema.Empty)), null!));
    }

    [Fact]
    public void The_default_dialect_is_a_comma_and_a_double_quote_with_a_header_and_lf()
    {
        var dialect = CsvDialect.Default;

        Assert.Equal(',', dialect.Delimiter);
        Assert.Equal('"', dialect.Quote);
        Assert.True(dialect.HasHeader);
        Assert.Equal("\n", dialect.LineEnding);
        Assert.False(dialect.WriteBom);
    }

    private static DataDocument Read(string csv, CsvDialect dialect)
    {
        var result = CsvDataReader.Read(Encoding.UTF8.GetBytes(csv), dialect);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);

        return result.Value;
    }

    private static string Write(DataDocument document, CsvDialect dialect)
    {
        var result = CsvDataWriter.WriteToUtf8Bytes(document, dialect);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);

        return Encoding.UTF8.GetString(result.Value);
    }
}
