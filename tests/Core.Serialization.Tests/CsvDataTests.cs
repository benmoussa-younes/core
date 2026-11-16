// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;
using System.Text;
using Core.IO.Mapped;
using Core.Serialization.Codecs.Csv;
using Core.Serialization.Documents;
using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class CsvDataTests
{
    [Fact]
    public void A_cell_bomb_is_rejected_before_the_rows_are_allocated()
    {
        var header = string.Join(',', Enumerable.Range(0, 1_000).Select(index => $"c{index}"));
        var row = string.Join(',', Enumerable.Range(0, 1_000).Select(_ => "0"));
        var builder = new StringBuilder(header).Append('\n');

        for (var index = 0; index < 4_001; index++)
        {
            builder.Append(row).Append('\n');
        }

        var result = CsvDataReader.Read(Encoding.UTF8.GetBytes(builder.ToString()));

        Assert.True(result.IsFailure);
        Assert.Equal(CoreErrorCodes.SerializationTooLarge, result.Error.Code);
    }

    [Fact]
    public void It_writes_a_header_and_rows_with_empty_fields_for_null()
    {
        var table = new DataTable("root", new DataSchema([new DataField("a", DataKind.Integer), new DataField("b", DataKind.Text)]));
        table.AddRow([DataValue.From(1L), DataValue.From("x")]);
        table.AddRow([DataValue.From(2L), DataValue.Null]);

        Assert.Equal("a,b\n1,x\n2,\n", Write(DataDocument.FromTable(table)));
    }

    [Fact]
    public void It_quotes_delimiters_quotes_and_newlines()
    {
        var table = new DataTable("root", new DataSchema([new DataField("t", DataKind.Text)]));
        table.AddRow([DataValue.From("a,b")]);
        table.AddRow([DataValue.From("he said \"hi\"")]);
        table.AddRow([DataValue.From("x\ny")]);

        Assert.Equal("t\n\"a,b\"\n\"he said \"\"hi\"\"\"\n\"x\ny\"\n", Write(DataDocument.FromTable(table)));
    }

    [Fact]
    public void It_refuses_sections_and_node_columns()
    {
        var section = new DataTable("s", DataSchema.Empty);
        var withSections = new DataDocument(new DataTable("root", DataSchema.Empty), [section]);
        Assert.Equal("serialization.invalid_document", CsvDataWriter.WriteToUtf8Bytes(withSections).Error.Code);

        var nodeTable = new DataTable("root", new DataSchema([new DataField("n", DataKind.Node)]));
        nodeTable.AddRow([DataValue.FromNode(DataNode.Scalar(DataValue.From(1L)))]);
        Assert.Equal("serialization.invalid_document", CsvDataWriter.WriteToUtf8Bytes(DataDocument.FromTable(nodeTable)).Error.Code);
    }

    [Fact]
    public void An_empty_document_writes_nothing()
    {
        Assert.Equal(string.Empty, Write(DataDocument.FromTable(new DataTable("root", DataSchema.Empty))));
    }

    [Fact]
    public void It_reads_columns_and_infers_kinds()
    {
        var document = Read("a,b,c,d\n1,1.5,true,x\n2,,false,y\n");

        var table = document.Root;
        Assert.Equal(2, table.RowCount);
        Assert.Equal(DataKind.Integer, table.Schema[0].Kind);
        Assert.Equal(DataKind.Decimal, table.Schema[1].Kind);
        Assert.Equal(DataKind.Boolean, table.Schema[2].Kind);
        Assert.Equal(DataKind.Text, table.Schema[3].Kind);
        Assert.Equal(DataValue.From(1L), table[0, 0]);
        Assert.Equal(DataValue.From(1.5m), table[0, 1]);
        Assert.Equal(DataValue.From(true), table[0, 2]);
        Assert.True(table[1, 1].IsNull);
    }

    [Fact]
    public void A_leading_zero_number_stays_text()
    {
        var document = Read("code\n01234\n00042\n-01\n+01\n00.5\n01e3\n00E5\n");

        Assert.Equal(DataKind.Text, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From("01234"), document.Root[0, 0]);
        Assert.Equal(DataValue.From("00042"), document.Root[1, 0]);
        Assert.Equal(DataValue.From("-01"), document.Root[2, 0]);
        Assert.Equal(DataValue.From("+01"), document.Root[3, 0]);
        Assert.Equal(DataValue.From("00.5"), document.Root[4, 0]);
        Assert.Equal(DataValue.From("01e3"), document.Root[5, 0]);
        Assert.Equal(DataValue.From("00E5"), document.Root[6, 0]);
    }

    [Fact]
    public void A_quoted_leading_zero_number_stays_text()
    {
        var document = Read("code\n\"01234\"\n\"-01\"\n\"00.5\"\n\"01e3\"\n\"00E5\"\n");

        Assert.Equal(DataKind.Text, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From("01234"), document.Root[0, 0]);
        Assert.Equal(DataValue.From("-01"), document.Root[1, 0]);
        Assert.Equal(DataValue.From("00.5"), document.Root[2, 0]);
        Assert.Equal(DataValue.From("01e3"), document.Root[3, 0]);
        Assert.Equal(DataValue.From("00E5"), document.Root[4, 0]);
    }

    [Fact]
    public void A_column_of_plain_numbers_stays_numeric()
    {
        var document = Read("v\n0\n0.5\n-0\n1234\n+0\n");

        Assert.Equal(DataKind.Decimal, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From(0m), document.Root[0, 0]);
        Assert.Equal(DataValue.From(0.5m), document.Root[1, 0]);
        Assert.Equal(DataValue.From(0m), document.Root[2, 0]);
        Assert.Equal(DataValue.From(1234m), document.Root[3, 0]);
        Assert.Equal(DataValue.From(0m), document.Root[4, 0]);
    }

    [Fact]
    public void A_leading_zero_and_plain_digits_mix_to_text()
    {
        var document = Read("v\n01234\n1\n");

        Assert.Equal(DataKind.Text, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From("01234"), document.Root[0, 0]);
        Assert.Equal(DataValue.From("1"), document.Root[1, 0]);
    }

    [Fact]
    public void It_reads_quoted_fields_with_delimiters_quotes_and_newlines()
    {
        Assert.Equal(DataValue.From("a,b"), Read("t\n\"a,b\"\n").Root[0, 0]);
        Assert.Equal(DataValue.From("he said \"hi\""), Read("t\n\"he said \"\"hi\"\"\"\n").Root[0, 0]);

        var multiline = Read("t\n\"x\ny\"\n");
        Assert.Equal(1, multiline.Root.RowCount);
        Assert.Equal(DataValue.From("x\ny"), multiline.Root[0, 0]);
    }

    [Fact]
    public void It_reads_crlf_and_a_byte_order_mark()
    {
        var document = Read("\uFEFFa,b\r\n1,2\r\n");

        Assert.Equal(2, document.Root.ColumnCount);
        Assert.Equal(1, document.Root.RowCount);
        Assert.Equal(DataValue.From(1L), document.Root[0, 0]);
    }

    [Fact]
    public void It_is_lenient_with_an_unclosed_quote()
    {
        Assert.Equal(DataValue.From("abc"), Read("t\n\"abc").Root[0, 0]);
    }

    [Fact]
    public void It_reads_quoted_values_of_every_kind()
    {
        var document = Read("a,b,c,d\n\"1\",\"true\",\"1.5\",abcd\n");

        Assert.Equal(DataKind.Integer, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From(1L), document.Root[0, 0]);
        Assert.Equal(DataKind.Boolean, document.Root.Schema[1].Kind);
        Assert.Equal(DataValue.From(true), document.Root[0, 1]);
        Assert.Equal(DataKind.Decimal, document.Root.Schema[2].Kind);
        Assert.Equal(DataValue.From(1.5m), document.Root[0, 2]);
        Assert.Equal(DataKind.Text, document.Root.Schema[3].Kind);
        Assert.Equal(DataValue.From("abcd"), document.Root[0, 3]);
    }

    [Fact]
    public void It_infers_money_as_decimal_not_a_double()
    {
        var document = Read("amount\n1.50\n2.25\n");

        Assert.Equal(DataKind.Decimal, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From(1.50m), document.Root[0, 0]);
        Assert.Equal(DataValue.From(2.25m), document.Root[1, 0]);
    }

    [Fact]
    public void An_integer_widens_into_a_decimal_column()
    {
        var document = Read("v\n1\n2.5\n");

        Assert.Equal(DataKind.Decimal, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From(1m), document.Root[0, 0]);
        Assert.Equal(DataValue.From(2.5m), document.Root[1, 0]);
    }

    [Fact]
    public void It_infers_an_iso_date()
    {
        var document = Read("d\n2026-01-01\n2026-02-01\n");

        Assert.Equal(DataKind.Date, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From(new DateOnly(2026, 1, 1)), document.Root[0, 0]);
        Assert.Equal(DataValue.From(new DateOnly(2026, 2, 1)), document.Root[1, 0]);
    }

    [Fact]
    public void It_infers_a_time_of_day()
    {
        var document = Read("t\n12:30:00\n01:02:03\n");

        Assert.Equal(DataKind.Time, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From(new TimeOnly(12, 30, 0)), document.Root[0, 0]);
    }

    [Fact]
    public void It_infers_an_iso_timestamp()
    {
        var document = Read("ts\n2026-01-01T12:30:00Z\n2026-01-02T00:00:00Z\n");

        Assert.Equal(DataKind.Timestamp, document.Root.Schema[0].Kind);
        Assert.Equal(
            DataValue.From(new DateTime(2026, 1, 1, 12, 30, 0, DateTimeKind.Utc)),
            document.Root[0, 0]);
    }

    [Fact]
    public void A_date_and_a_timestamp_are_a_timestamp()
    {
        var document = Read("v\n2026-01-01\n2026-01-01T00:00:00Z\n");

        Assert.Equal(DataKind.Timestamp, document.Root.Schema[0].Kind);
    }

    [Theory]
    [InlineData("2026-01-01\n12:00:00\n")] // a date and a time of day
    [InlineData("1\n2026-01-01\n")] // a number and a date
    [InlineData("1-2-3\n")] // date-shaped but not a date
    public void An_ambiguous_or_invalid_temporal_shape_is_text(string body)
    {
        var document = Read("v\n" + body);

        Assert.Equal(DataKind.Text, document.Root.Schema[0].Kind);
    }

    [Fact]
    public void It_round_trips_decimal_date_time_and_timestamp()
    {
        var schema = new DataSchema(
        [
            new DataField("Decimal", DataKind.Decimal),
            new DataField("Date", DataKind.Date),
            new DataField("Time", DataKind.Time),
            new DataField("Timestamp", DataKind.Timestamp),
        ]);
        var table = new DataTable("root", schema);
        table.AddRow(
        [
            DataValue.From(12.34m),
            DataValue.From(new DateOnly(2026, 6, 5)),
            DataValue.From(new TimeOnly(8, 9, 10)),
            // CSV writes a naive timestamp, so the round-trip kind is Unspecified.
            DataValue.From(new DateTime(2026, 6, 5, 8, 9, 10)),
        ]);

        var back = Read(Write(DataDocument.FromTable(table)));

        Assert.Equal(DataKind.Decimal, back.Root.Schema[0].Kind);
        Assert.Equal(DataKind.Date, back.Root.Schema[1].Kind);
        Assert.Equal(DataKind.Time, back.Root.Schema[2].Kind);
        Assert.Equal(DataKind.Timestamp, back.Root.Schema[3].Kind);
        Assert.Equal(DataValue.From(12.34m), back.Root[0, 0]);
        Assert.Equal(DataValue.From(new DateOnly(2026, 6, 5)), back.Root[0, 1]);
        Assert.Equal(DataValue.From(new TimeOnly(8, 9, 10)), back.Root[0, 2]);
        Assert.Equal(DataValue.From(new DateTime(2026, 6, 5, 8, 9, 10)), back.Root[0, 3]);
    }

    [Fact]
    public void It_reads_a_mapped_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "core-csv-" + Guid.NewGuid().ToString("N") + ".csv");

        try
        {
            File.WriteAllText(path, "a,b\n1,x\n");

            using (var file = MappedFile.Open(path).Value)
            {
                var result = CsvDataReader.Read(file);
                Assert.True(result.IsSuccess);
                Assert.Equal(DataValue.From(1L), result.Value.Root[0, 0]);
            }

            Assert.Throws<ArgumentNullException>(() => CsvDataReader.Read((MappedFile)null!));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_non_finite_number_is_text()
    {
        var document = Read("v\nNaN\nInfinity\n");

        Assert.Equal(DataKind.Text, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From("NaN"), document.Root[0, 0]);
        Assert.Equal(DataValue.From("Infinity"), document.Root[1, 0]);
    }

    [Fact]
    public void An_exponent_value_is_a_number_not_a_decimal()
    {
        var document = Read("v\n1e10\n-2.5e-3\n\"1e10\"\n");

        Assert.Equal(DataKind.Number, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From(1e10), document.Root[0, 0]);
        Assert.Equal(DataValue.From(-2.5e-3), document.Root[1, 0]);
        Assert.Equal(DataValue.From(1e10), document.Root[2, 0]);
    }

    [Fact]
    public void A_quoted_date_is_a_date()
    {
        var document = Read("d\n\"2026-01-01\"\n");

        Assert.Equal(DataKind.Date, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From(new DateOnly(2026, 1, 1)), document.Root[0, 0]);
    }

    [Theory]
    [InlineData("\"99:99\"")] // time-shaped, not a time
    [InlineData("\"2026-01-01T99:99\"")] // timestamp-shaped, not a timestamp
    [InlineData("\"2026-01-01x12:00\"")] // the wrong separator
    public void A_quoted_invalid_temporal_shape_is_text(string field)
    {
        var document = Read("v\n" + field + "\n");

        Assert.Equal(DataKind.Text, document.Root.Schema[0].Kind);
    }

    [Fact]
    public void It_interns_a_repeated_text_value()
    {
        var document = Read("t\nsame\nsame\n");

        Assert.True(document.Root[0, 0].TryGetText(out var first));
        Assert.True(document.Root[1, 0].TryGetText(out var second));
        Assert.Same(first, second);
    }

    [Fact]
    public void It_reads_more_distinct_text_than_the_intern_pool_holds()
    {
        var builder = new StringBuilder("t\n");

        for (var index = 0; index < 8200; index++)
        {
            builder.Append('v').Append(index).Append('\n');
        }

        var document = Read(builder.ToString());

        Assert.Equal(8200, document.Root.RowCount);
        Assert.Equal(DataValue.From("v8199"), document.Root[8199, 0]);
    }

    [Fact]
    public void It_drops_a_stray_character_after_a_closing_quote()
    {
        var document = Read("a,b\n\"x\"z,y\n");

        Assert.Equal(DataValue.From("x"), document.Root[0, 0]);
        Assert.Equal(DataValue.From("y"), document.Root[0, 1]);
    }

    [Fact]
    public void An_empty_file_is_an_empty_document()
    {
        var document = Read(string.Empty);

        Assert.Equal(0, document.Root.ColumnCount);
        Assert.Equal(0, document.Root.RowCount);
    }

    [Fact]
    public void A_header_without_rows_is_an_empty_table()
    {
        var document = Read("a,b\n");

        Assert.Equal(2, document.Root.ColumnCount);
        Assert.Equal(0, document.Root.RowCount);
    }

    [Fact]
    public void Mixed_boolean_and_numeric_is_text()
    {
        var document = Read("v\ntrue\n1\n");

        Assert.Equal(DataKind.Text, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From("true"), document.Root[0, 0]);
        Assert.Equal(DataValue.From("1"), document.Root[1, 0]);
    }

    [Fact]
    public void Uneven_rows_are_refused()
    {
        var result = CsvDataReader.Read(Encoding.UTF8.GetBytes("a,b\n1\n"));

        Assert.Equal("serialization.uneven_row", result.Error.Code);
    }

    [Fact]
    public void A_document_with_too_many_columns_is_refused()
    {
        var csv = string.Concat(
            string.Join(',', Enumerable.Repeat("c", CsvDataReader.MaxColumns + 1)),
            "\n");

        var result = CsvDataReader.Read(Encoding.UTF8.GetBytes(csv));

        Assert.Equal("serialization.too_many_columns", result.Error.Code);
    }

    [Fact]
    public void Duplicate_header_names_are_refused()
    {
        var result = CsvDataReader.Read(Encoding.UTF8.GetBytes("a,a\n1,2\n"));

        Assert.Equal("serialization.invalid_document", result.Error.Code);
    }

    [Fact]
    public void An_empty_quoted_field_is_null()
    {
        Assert.True(Read("t\n\"\"\n").Root[0, 0].IsNull);
    }

    private static string Write(DataDocument document)
    {
        var result = CsvDataWriter.WriteToUtf8Bytes(document);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);

        return Encoding.UTF8.GetString(result.Value);
    }

    private static DataDocument Read(string csv)
    {
        var result = CsvDataReader.Read(Encoding.UTF8.GetBytes(csv));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);

        return result.Value;
    }
}
