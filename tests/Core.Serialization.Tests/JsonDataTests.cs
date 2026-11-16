// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;
using Core.Results;
using Core.Serialization.Codecs.Json;
using Core.Serialization.Documents;
using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class JsonDataTests
{
    [Fact]
    public void A_one_row_root_writes_an_object()
    {
        var document = OneRow(new DataSchema([new DataField("a", DataKind.Integer), new DataField("b", DataKind.Text)]), [DataValue.From(1L), DataValue.From("x")]);

        Assert.Equal("{\"a\":1,\"b\":\"x\"}", Write(document));
    }

    [Fact]
    public void A_multi_row_root_writes_an_array()
    {
        var table = new DataTable("root", new DataSchema([new DataField("a", DataKind.Integer)]));
        table.AddRow([DataValue.From(1L)]);
        table.AddRow([DataValue.From(2L)]);

        Assert.Equal("[{\"a\":1},{\"a\":2}]", Write(DataDocument.FromTable(table)));
    }

    [Fact]
    public void An_empty_root_writes_an_empty_object()
    {
        Assert.Equal("{}", Write(DataDocument.FromTable(new DataTable("root", DataSchema.Empty))));
    }

    [Fact]
    public void A_document_with_sections_writes_root_fields_then_section_arrays()
    {
        var root = OneRowTable(new DataSchema([new DataField("version", DataKind.Integer)]), [DataValue.From(1L)]);
        var section = new DataTable("tunnels", new DataSchema([new DataField("name", DataKind.Text)]));
        section.AddRow([DataValue.From("wg0")]);

        var document = new DataDocument(root, [section]);

        Assert.Equal("{\"version\":1,\"tunnels\":[{\"name\":\"wg0\"}]}", Write(document));
    }

    [Fact]
    public void A_document_with_sections_and_no_root_fields_writes_only_the_sections()
    {
        var section = new DataTable("items", new DataSchema([new DataField("a", DataKind.Integer)]));
        section.AddRow([DataValue.From(1L)]);

        var document = new DataDocument(new DataTable("root", DataSchema.Empty), [section]);

        Assert.Equal("{\"items\":[{\"a\":1}]}", Write(document));
    }

    [Fact]
    public void It_writes_every_kind_and_a_nested_node()
    {
        var schema = new DataSchema(
        [
            new DataField("b", DataKind.Boolean),
            new DataField("i", DataKind.Integer),
            new DataField("n", DataKind.Number),
            new DataField("d", DataKind.Decimal),
            new DataField("t", DataKind.Text),
            new DataField("date", DataKind.Date),
            new DataField("time", DataKind.Time),
            new DataField("ts", DataKind.Timestamp),
            new DataField("z", DataKind.Text),
            new DataField("node", DataKind.Node),
        ]);

        var node = DataNode.Map(
        [
            new DataProperty(
                "k",
                DataNode.List(
                [
                    DataNode.Scalar(DataValue.From(1L)),
                    DataNode.Scalar(DataValue.From("x")),
                    DataNode.Scalar(DataValue.Null),
                ])),
        ]);

        var table = new DataTable("root", schema);
        table.AddRow(
        [
            DataValue.From(true),
            DataValue.From(1L),
            DataValue.From(1.5),
            DataValue.From(1.5m),
            DataValue.From("x"),
            DataValue.From(new DateOnly(2026, 9, 29)),
            DataValue.From(new TimeOnly(1, 2, 3)),
            DataValue.From(new DateTime(2026, 9, 29, 12, 30, 0, DateTimeKind.Utc)),
            DataValue.Null,
            DataValue.FromNode(node),
        ]);

        Assert.Equal(
            "{\"b\":true,\"i\":1,\"n\":1.5,\"d\":1.5,\"t\":\"x\",\"date\":\"2026-09-29\",\"time\":\"01:02:03.0000000\",\"ts\":\"2026-09-29T12:30:00.0000000Z\",\"z\":null,\"node\":{\"k\":[1,\"x\",null]}}",
            Write(DataDocument.FromTable(table)));
    }

    [Fact]
    public void A_writer_refuses_sections_with_more_than_one_root_row()
    {
        var root = OneRowTable(new DataSchema([new DataField("a", DataKind.Integer)]), [DataValue.From(1L)]);
        root.AddRow([DataValue.From(2L)]);
        var document = new DataDocument(root, [new DataTable("s", DataSchema.Empty)]);

        var result = JsonDataWriter.WriteToUtf8Bytes(document);

        Assert.True(result.IsFailure);
        Assert.Equal("serialization.invalid_document", result.Error.Code);
    }

    [Fact]
    public void A_one_row_root_reads_an_object_with_inferred_kinds()
    {
        var document = Read("{\"a\":1,\"b\":\"x\",\"c\":true,\"d\":1.5,\"e\":null}");

        Assert.Equal(1, document.Root.RowCount);
        Assert.Equal(DataKind.Integer, document.Root.Schema[0].Kind);
        Assert.Equal(DataKind.Text, document.Root.Schema[1].Kind);
        Assert.Equal(DataKind.Boolean, document.Root.Schema[2].Kind);
        Assert.Equal(DataKind.Number, document.Root.Schema[3].Kind);
        Assert.Equal(DataKind.Text, document.Root.Schema[4].Kind);
        Assert.Equal(DataValue.From(1L), document.Root[0, 0]);
        Assert.True(document.Root[0, 4].IsNull);
    }

    [Fact]
    public void A_top_level_array_of_objects_reads_as_root_rows()
    {
        var document = Read("[{\"a\":1},{\"a\":2}]");

        Assert.Equal(2, document.Root.RowCount);
        Assert.Equal(DataKind.Integer, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From(2L), document.Root[1, 0]);
    }

    [Fact]
    public void A_top_level_array_of_scalars_reads_as_a_value_column()
    {
        var document = Read("[1,2,3]");

        Assert.Equal(3, document.Root.RowCount);
        Assert.Equal("value", document.Root.Schema[0].Name);
        Assert.Equal(DataKind.Integer, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From(2L), document.Root[1, 0]);
    }

    [Fact]
    public void A_scalar_document_reads_as_a_single_value()
    {
        Assert.Equal(DataValue.From(42L), Read("42").Root[0, 0]);
        Assert.Equal(DataValue.From("hi"), Read("\"hi\"").Root[0, 0]);
        Assert.True(Read("null").Root[0, 0].IsNull);
    }

    [Fact]
    public void A_nested_object_or_scalar_array_is_a_node_cell()
    {
        var document = Read("{\"nested\":{\"a\":1},\"arr\":[1,2]}");

        Assert.Equal(DataKind.Node, document.Root.Schema[0].Kind);
        Assert.True(document.Root[0, 0].TryGetNode(out var nested));
        Assert.True(nested!.TryGetProperty("a", out var a));
        Assert.Equal(DataValue.From(1L), a!.Value);
        Assert.Equal(DataKind.Node, document.Root.Schema[1].Kind);
        Assert.True(document.Root[0, 1].TryGetNode(out var array));
        Assert.Equal(2, array!.Items.Count);
    }

    [Fact]
    public void An_array_of_objects_property_reads_as_a_section()
    {
        var document = Read("{\"version\":1,\"tunnels\":[{\"name\":\"wg0\"},{\"name\":\"wg1\",\"port\":2}]}");

        Assert.Equal(1, document.Root.RowCount);
        Assert.Single(document.Sections);
        var section = document.Sections[0];
        Assert.Equal("tunnels", section.Name);
        Assert.Equal(2, section.RowCount);
        Assert.Equal(DataKind.Text, section.Schema[0].Kind);
        Assert.Equal(DataKind.Integer, section.Schema[1].Kind);
        Assert.Equal(DataValue.From("wg1"), section[1, 0]);
        Assert.Equal(DataValue.From(2L), section[1, 1]);
        Assert.True(section[0, 1].IsNull);
    }

    [Fact]
    public void Mixed_boolean_and_numeric_is_text_and_node_wins()
    {
        var mixed = Read("[{\"v\":true},{\"v\":1}]");
        Assert.Equal(DataKind.Text, mixed.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From("true"), mixed.Root[0, 0]);
        Assert.Equal(DataValue.From("1"), mixed.Root[1, 0]);

        var numeric = Read("[{\"v\":1},{\"v\":1.5}]");
        Assert.Equal(DataKind.Number, numeric.Root.Schema[0].Kind);

        var nodes = Read("[{\"v\":{\"a\":1}},{\"v\":2}]");
        Assert.Equal(DataKind.Node, nodes.Root.Schema[0].Kind);
        Assert.True(nodes.Root[1, 0].TryGetNode(out var wrapped));
        Assert.True(wrapped!.Value.TryGetInteger(out var two));
        Assert.Equal(2L, two);

        var allNull = Read("[{\"v\":null},{\"v\":null}]");
        Assert.Equal(DataKind.Text, allNull.Root.Schema[0].Kind);
    }

    [Fact]
    public void A_number_beyond_double_is_kept_as_text_and_duplicates_last_win()
    {
        var document = Read("{\"big\":1e400,\"dup\":1,\"dup\":2}");

        Assert.Equal(DataKind.Text, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From("1e400"), document.Root[0, 0]);
        Assert.Equal(DataValue.From(2L), document.Root[0, 1]);
    }

    [Fact]
    public void An_empty_object_and_an_empty_array_read_as_empty_documents()
    {
        Assert.Equal(0, Read("{}").Root.ColumnCount);
        Assert.Equal(0, Read("[]").Root.RowCount);
        Assert.Equal("value", Read("[]").Root.Schema[0].Name);
    }

    [Fact]
    public void It_streams_a_top_level_array_of_objects()
    {
        var document = Read("[{\"a\":1,\"b\":\"x\",\"c\":true,\"d\":null},{\"a\":2,\"c\":false}]");

        Assert.Equal(2, document.Root.RowCount);
        Assert.Equal(DataKind.Integer, document.Root.Schema[0].Kind);
        Assert.Equal(DataKind.Text, document.Root.Schema[1].Kind);
        Assert.Equal(DataKind.Boolean, document.Root.Schema[2].Kind);
        Assert.Equal(DataValue.From("x"), document.Root[0, 1]);
        Assert.Equal(DataValue.From(false), document.Root[1, 2]);
        Assert.True(document.Root[1, 3].IsNull);
    }

    [Fact]
    public void A_streamed_row_with_a_lone_surrogate_value_falls_back_to_the_tree_read()
    {
        // The streaming path reads values with GetString(), which throws
        // InvalidOperationException (not JsonException) on a lone surrogate; the
        // fallback must turn it into the same syntax failure, never let it escape.
        var result = JsonDataReader.Read(Encoding.UTF8.GetBytes("[{\"a\":\"\\ud800\"}]"));

        Assert.True(result.IsFailure);
        Assert.Equal(CoreErrorCodes.SerializationSyntax, result.Error.Code);
    }

    [Fact]
    public void A_streamed_row_with_a_lone_surrogate_column_name_falls_back_to_the_tree_read()
    {
        var result = JsonDataReader.Read(Encoding.UTF8.GetBytes("[{\"\\ud800\":1}]"));

        Assert.True(result.IsFailure);
        Assert.Equal(CoreErrorCodes.SerializationSyntax, result.Error.Code);
    }

    [Fact]
    public void It_keeps_a_huge_number_as_text_in_a_streamed_array()
    {
        var document = Read("[{\"v\":1e400}]");

        Assert.Equal(DataKind.Text, document.Root.Schema[0].Kind);
        Assert.Equal(DataValue.From("1e400"), document.Root[0, 0]);
    }

    [Fact]
    public void A_section_coerces_a_node_and_a_text_column()
    {
        var nodes = Read("{\"s\":[{\"v\":{\"a\":1}},{\"v\":2}]}");
        Assert.Equal(DataKind.Node, nodes.Sections[0].Schema[0].Kind);
        Assert.True(nodes.Sections[0][1, 0].TryGetNode(out var wrapped));
        Assert.True(wrapped!.Value.TryGetInteger(out var two));
        Assert.Equal(2L, two);

        var text = Read("{\"s\":[{\"v\":1},{\"v\":\"x\"}]}");
        Assert.Equal(DataKind.Text, text.Sections[0].Schema[0].Kind);
        Assert.Equal(DataValue.From("1"), text.Sections[0][0, 0]);
    }

    [Fact]
    public void A_false_value_is_read()
    {
        Assert.Equal(DataValue.From(false), Read("false").Root[0, 0]);
    }

    [Fact]
    public void An_empty_document_is_a_syntax_failure()
    {
        var result = JsonDataReader.Read(ReadOnlySpan<byte>.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal("serialization.syntax", result.Error.Code);
    }

    [Fact]
    public void Two_values_are_a_syntax_failure()
    {
        var result = JsonDataReader.Read(Encoding.UTF8.GetBytes("{} {}"));

        Assert.True(result.IsFailure);
        Assert.Equal("serialization.syntax", result.Error.Code);
    }

    [Theory]
    [InlineData("{\"a\":}")]
    [InlineData("[}]")]
    [InlineData("[,]")]
    public void Malformed_structures_report_a_syntax_failure(string json)
    {
        var result = JsonDataReader.Read(Encoding.UTF8.GetBytes(json));

        Assert.True(result.IsFailure);
        Assert.Equal("serialization.syntax", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"a\":1} {\"b\":2}")]
    [InlineData("[{\"a\":1}] {\"b\":2}")]
    [InlineData("{\"a\":")]
    [InlineData("}")]
    [InlineData("nope")]
    public void Invalid_json_reports_a_syntax_failure(string json)
    {
        var result = JsonDataReader.Read(Encoding.UTF8.GetBytes(json));

        Assert.True(result.IsFailure);
        Assert.Equal("serialization.syntax", result.Error.Code);
    }

    [Fact]
    public void A_non_finite_number_has_no_json_form()
    {
        var table = new DataTable("root", new DataSchema([new DataField("n", DataKind.Number)]));
        table.AddRow([DataValue.From(double.NaN)]);

        var result = JsonDataWriter.WriteToUtf8Bytes(DataDocument.FromTable(table));

        Assert.Equal("serialization.invalid_document", result.Error.Code);
    }

    // --- The additive API: the default number format is byte-identical to the
    // legacy BCL rendering, and a caller-supplied format only changes Number cells.

    [Fact]
    public void The_default_number_format_keeps_the_bcl_rendering()
    {
        Assert.Equal("{\"n\":3}", Write(OneRow(new DataSchema([new DataField("n", DataKind.Number)]), [DataValue.From(3.0)])));
        Assert.Equal("{\"n\":-0}", Write(OneRow(new DataSchema([new DataField("n", DataKind.Number)]), [DataValue.From(-0.0)])));
        Assert.Equal("{\"n\":1E+20}", Write(OneRow(new DataSchema([new DataField("n", DataKind.Number)]), [DataValue.From(1.0E+20)])));
    }

    [Fact]
    public void A_custom_number_format_changes_only_number_cells()
    {
        var schema = new DataSchema([new DataField("f", DataKind.Number), new DataField("i", DataKind.Integer), new DataField("t", DataKind.Text)]);
        var document = OneRow(schema, [DataValue.From(3.0), DataValue.From(7L), DataValue.From("x")]);
        var format = JsonNumberFormat.From(static value => value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));

        var result = JsonDataWriter.WriteToUtf8Bytes(document, format);

        Assert.True(result.IsSuccess);
        Assert.Equal("{\"f\":3.0,\"i\":7,\"t\":\"x\"}", Encoding.UTF8.GetString(result.Value));
    }

    [Fact]
    public void A_non_finite_number_is_still_refused_with_a_custom_format()
    {
        var document = OneRow(new DataSchema([new DataField("n", DataKind.Number)]), [DataValue.From(double.PositiveInfinity)]);
        var format = JsonNumberFormat.From(static value => value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var result = JsonDataWriter.WriteToUtf8Bytes(document, format);

        Assert.Equal("serialization.invalid_document", result.Error.Code);
    }

    [Fact]
    public void WriteNode_emits_a_bare_root()
    {
        Assert.Equal("[1,2]", WriteNode(DataNode.List([DataNode.Scalar(DataValue.From(1L)), DataNode.Scalar(DataValue.From(2L))])));
        Assert.Equal("[]", WriteNode(DataNode.List([])));
        Assert.Equal("{}", WriteNode(DataNode.Map([])));
        Assert.Equal("\"abc\"", WriteNode(DataNode.Scalar(DataValue.From("abc"))));
        Assert.Equal("null", WriteNode(DataNode.Scalar(DataValue.Null)));
    }

    [Fact]
    public void WriteNode_renders_number_cells_with_the_format()
    {
        var node = DataNode.List([DataNode.Scalar(DataValue.From(3.0)), DataNode.Scalar(DataValue.From(-0.0))]);
        var format = JsonNumberFormat.From(static value => BitConverter.DoubleToInt64Bits(value) == long.MinValue
            ? "-0.0"
            : value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));

        var result = JsonDataWriter.WriteNodeToUtf8Bytes(node, format);

        Assert.True(result.IsSuccess);
        Assert.Equal("[3.0,-0.0]", Encoding.UTF8.GetString(result.Value));
    }

    [Fact]
    public void The_default_node_writer_falls_back_to_the_BCL_number_rendering()
    {
        Assert.False(JsonNumberFormat.Default.HasCustomRenderer);

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();

        Assert.True(JsonDataWriter.WriteNode(DataNode.Map([]), buffer).IsSuccess);
        Assert.Equal("{}", Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    [Fact]
    public void The_default_escaper_is_the_BCL_one()
    {
        Assert.Same(System.Text.Encodings.Web.JavaScriptEncoder.Default, JsonEscaping.Default);
    }

    [Fact]
    public void The_widest_escape_the_raw_encoder_claims_to_write_is_the_long_form()
    {
        // The writer sizes its scratch buffer from this number: an escape wider than
        // it claims would be truncated instead of written.
        Assert.Equal(6, JsonEscaping.Utf8Raw.MaxOutputCharactersPerInputCharacter);
    }

    [Fact]
    public void ReadNode_reads_a_bare_root()
    {
        Assert.Equal(DataNodeKind.List, ReadNode("[]").Kind);
        Assert.Equal(DataNodeKind.Map, ReadNode("{}").Kind);
        Assert.Equal(DataNodeKind.Scalar, ReadNode("\"abc\"").Kind);
        Assert.Equal(3, ReadNode("[1,[2,3],4]").Items.Count);
        Assert.Equal(DataKind.Integer, ReadNode("3").Value.Kind);
        Assert.Equal(DataKind.Number, ReadNode("3.0").Value.Kind);
    }

    [Fact]
    public void ReadNode_reports_a_syntax_failure()
    {
        var result = JsonDataReader.ReadNode(Encoding.UTF8.GetBytes("{oops"), 64);

        Assert.True(result.IsFailure);
        Assert.Equal("serialization.syntax", result.Error.Code);
    }

    [Fact]
    public void ReadNode_honours_the_supplied_maximum_depth()
    {
        // The BCL default (64) is far below a data tree's legitimate nesting, so the
        // depth is the caller's policy; the failure must be about depth, not be a
        // silent stop at 64.
        var deep = new string('[', 600) + new string(']', 600);

        Assert.True(JsonDataReader.ReadNode(Encoding.UTF8.GetBytes(deep), 1_001).IsSuccess);

        var tooDeep = JsonDataReader.ReadNode(Encoding.UTF8.GetBytes(deep), 64);
        Assert.True(tooDeep.IsFailure);
        Assert.Contains("depth", tooDeep.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string WriteNode(DataNode node)
    {
        var result = JsonDataWriter.WriteNodeToUtf8Bytes(node);
        Assert.True(result.IsSuccess);

        return Encoding.UTF8.GetString(result.Value);
    }

    private static DataNode ReadNode(string json)
    {
        var result = JsonDataReader.ReadNode(Encoding.UTF8.GetBytes(json), 64);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);

        return result.Value;
    }

    private static string Write(DataDocument document)
    {
        var result = JsonDataWriter.WriteToUtf8Bytes(document);
        Assert.True(result.IsSuccess);

        return Encoding.UTF8.GetString(result.Value);
    }

    private static DataDocument Read(string json)
    {
        var result = JsonDataReader.Read(Encoding.UTF8.GetBytes(json));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);

        return result.Value;
    }

    private static DataDocument OneRow(DataSchema schema, DataValue[] row) =>
        DataDocument.FromTable(OneRowTable(schema, row));

    private static DataTable OneRowTable(DataSchema schema, DataValue[] row)
    {
        var table = new DataTable("root", schema);
        table.AddRow(row);

        return table;
    }

    [Theory]
    [InlineData("plain ascii", "\"plain ascii\"")]
    [InlineData("café", "\"café\"")]
    [InlineData("quote\"backslash\\tab\t", "\"quote\\\"backslash\\\\tab\\u0009\"")]
    [InlineData("\u0000\u0001\u001f\u007f", "\"\\u0000\\u0001\\u001f\\u007f\"")]
    [InlineData("\u2028\u2029", "\"\\u2028\\u2029\"")]
    [InlineData("emoji 😀 combining e\u0301", "\"emoji 😀 combining e\u0301\"")]
    public void Utf8Raw_escaping_is_reversible_and_keeps_the_default_rules(string text, string expected)
    {
        // Utf8Raw is the compact dialect: raw UTF-8 for non-ASCII, lowercase
        // \uXXXX for every other escape and never a short form.
        var raw = Encoding.UTF8.GetString(
            JsonDataWriter.WriteNodeToUtf8Bytes(Scalar(text), JsonNumberFormat.Default, JsonEscaping.Utf8Raw).Value);

        Assert.Equal(expected, raw);
        Assert.Equal(text, System.Text.Json.JsonSerializer.Deserialize<string>(raw));

        // The default mode is byte-for-byte the legacy BCL escaping.
        var legacy = Encoding.UTF8.GetString(JsonDataWriter.WriteNodeToUtf8Bytes(Scalar(text)).Value);
        Assert.Equal(text, System.Text.Json.JsonSerializer.Deserialize<string>(legacy));
    }

    [Fact]
    public void Utf8Raw_keeps_non_ascii_raw_and_lowercases_control_escapes()
    {
        var node = DataNode.Map(
        [
            new DataProperty("café", DataNode.Scalar(DataValue.From("a\"b\\c\td\u2028e"))),
        ]);

        var raw = Encoding.UTF8.GetString(
            JsonDataWriter.WriteNodeToUtf8Bytes(node, JsonNumberFormat.Default, JsonEscaping.Utf8Raw).Value);

        Assert.Equal("{\"café\":\"a\\\"b\\\\c\\u0009d\\u2028e\"}", raw);

        // The default leaves non-ASCII escaped, uppercases hex and uses the short form.
        var legacy = Encoding.UTF8.GetString(
            JsonDataWriter.WriteNodeToUtf8Bytes(node).Value);

        Assert.Equal("{\"caf\\u00E9\":\"a\\u0022b\\\\c\\td\\u2028e\"}", legacy);
    }

    [Fact]
    public void Writing_a_node_deeper_than_the_writer_allows_is_a_typed_failure_not_a_crash()
    {
        // Utf8JsonWriter refuses the 1001st container with an InvalidOperationException;
        // the writer must turn that into serialization.depth_exceeded.
        Assert.True(JsonDataWriter.WriteNodeToUtf8Bytes(NestedNode(1_000)).IsSuccess);

        var tooDeep = JsonDataWriter.WriteNodeToUtf8Bytes(NestedNode(1_001), JsonNumberFormat.Default, JsonEscaping.Utf8Raw);

        Assert.True(tooDeep.IsFailure);
        Assert.Equal(CoreErrorCodes.SerializationDepthExceeded, tooDeep.Error.Code);
    }

    [Theory]
    [InlineData("\"\\ud800\"")]
    [InlineData("\"\\udc00\"")]
    [InlineData("\"\\ud800x\"")]
    [InlineData("\"a\\udc00b\"")]
    [InlineData("[1,\"\\ud800\"]")]
    [InlineData("{\"k\":\"\\ud800\"}")]
    public void ReadNode_reports_a_lone_surrogate_as_a_syntax_failure(string json)
    {
        // Utf8JsonReader.GetString() throws InvalidOperationException (not
        // JsonException) on a lone surrogate; untrusted input must never escape.
        var result = JsonDataReader.ReadNode(Encoding.UTF8.GetBytes(json), 64);

        Assert.True(result.IsFailure);
        Assert.Equal(CoreErrorCodes.SerializationSyntax, result.Error.Code);
    }

    [Fact]
    public void ReadNode_still_decodes_a_valid_surrogate_pair()
    {
        var result = JsonDataReader.ReadNode(Encoding.UTF8.GetBytes("\"\\ud83d\\ude00\""), 64);

        Assert.True(result.IsSuccess);
        Assert.Equal("😀", result.Value.Value.ToInvariantText());
    }

    [Theory]
    [InlineData("[\"\\ud800\"]")]
    [InlineData("{\"k\":\"\\udc00\"}")]
    public void Read_reports_a_lone_surrogate_as_a_syntax_failure(string json)
    {
        // The document path streams an array of objects first; both it and the tree
        // fallback must turn the lone-surrogate InvalidOperationException into a
        // syntax failure rather than let it escape.
        var result = JsonDataReader.Read(Encoding.UTF8.GetBytes(json));

        Assert.True(result.IsFailure);
        Assert.Equal(CoreErrorCodes.SerializationSyntax, result.Error.Code);
    }

    [Fact]
    public void Read_rejects_a_cell_bomb_before_allocating_the_rows()
    {
        // 500 columns × 8 001 rows = 4 000 500 cells, just over MaxCells. The
        // input is small enough to parse but would otherwise allocate millions
        // of DataValue cells.
        var json = ObjectArray(columns: 500, rows: 8_001);

        var result = JsonDataReader.Read(Encoding.UTF8.GetBytes(json));

        Assert.True(result.IsFailure);
        Assert.Equal(CoreErrorCodes.SerializationTooLarge, result.Error.Code);
    }

    [Fact]
    public void Read_rejects_too_many_columns()
    {
        var json = ObjectArray(columns: JsonDataReader.MaxColumns + 1, rows: 1);

        var result = JsonDataReader.Read(Encoding.UTF8.GetBytes(json));

        Assert.True(result.IsFailure);
        Assert.Equal(CoreErrorCodes.SerializationTooManyColumns, result.Error.Code);
    }

    [Fact]
    public void ReadNode_reports_nesting_past_the_maximum_as_a_depth_failure()
    {
        var below = JsonDataReader.ReadNode(Encoding.UTF8.GetBytes(NestedJson(1_000)), 1_000);
        Assert.True(below.IsSuccess);

        var above = JsonDataReader.ReadNode(Encoding.UTF8.GetBytes(NestedJson(1_001)), 1_000);

        Assert.True(above.IsFailure);
        Assert.Equal(CoreErrorCodes.SerializationDepthExceeded, above.Error.Code);
    }

    [Fact]
    public void ReadNode_rejects_a_depth_policy_the_recursive_walk_cannot_support()
    {
        var atCeiling = JsonDataReader.ReadNode(
            Encoding.UTF8.GetBytes("[]"),
            JsonDataReader.MaximumSupportedDepth);

        Assert.True(atCeiling.IsSuccess);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JsonDataReader.ReadNode(
                Encoding.UTF8.GetBytes("[]"),
                JsonDataReader.MaximumSupportedDepth + 1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JsonDataReader.ReadNode(Encoding.UTF8.GetBytes("[]"), 0));
    }

    [Fact]
    public void A_value_the_reader_accepts_can_be_written_back_at_the_same_depth()
    {
        var decoded = JsonDataReader.ReadNode(Encoding.UTF8.GetBytes(NestedJson(1_000)), 1_000);
        Assert.True(decoded.IsSuccess);

        var encoded = JsonDataWriter.WriteNodeToUtf8Bytes(decoded.Value, JsonNumberFormat.Default, JsonEscaping.Utf8Raw);

        Assert.True(encoded.IsSuccess);
        Assert.Equal(NestedJson(1_000), Encoding.UTF8.GetString(encoded.Value));
    }

    private static DataNode Scalar(string text) => DataNode.Scalar(DataValue.From(text));

    private static DataNode NestedNode(int depth)
    {
        DataNode node = DataNode.Scalar(DataValue.From(0L));
        for (var level = 0; level < depth; level++)
        {
            node = DataNode.List([node]);
        }

        return node;
    }

    private static string NestedJson(int depth) => new string('[', depth) + new string(']', depth);

    private static string ObjectArray(int columns, int rows)
    {
        var builder = new StringBuilder("[");
        for (var row = 0; row < rows; row++)
        {
            if (row > 0)
            {
                builder.Append(',');
            }

            builder.Append('{');
            for (var column = 0; column < columns; column++)
            {
                if (column > 0)
                {
                    builder.Append(',');
                }

                builder.Append('"').Append(column).Append("\":0");
            }

            builder.Append('}');
        }

        return builder.Append(']').ToString();
    }
}
