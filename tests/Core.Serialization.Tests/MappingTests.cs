// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Documents;
using Core.Serialization.Mapping;
using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class MappingTests
{
    public sealed record Sample(string Name, int Count, List<Entry> Entries);

    public sealed record Entry(string Name, int Port);

    [Fact]
    public void It_maps_a_value_to_a_document_and_back()
    {
        var sample = new Sample("wg", 7, [new Entry("wg0", 51820), new Entry("wg1", 51821)]);

        var document = DataDocumentMapper.ToDocument(sample, SerializationJsonContext.Default.Sample);
        Assert.True(document.IsSuccess);

        Assert.Equal(DataValue.From("wg"), document.Value.Root[0, 0]);
        Assert.Equal(DataValue.From(7L), document.Value.Root[0, 1]);
        Assert.Single(document.Value.Sections);
        Assert.Equal("entries", document.Value.Sections[0].Name);
        Assert.Equal(2, document.Value.Sections[0].RowCount);

        var back = DataDocumentMapper.FromDocument(document.Value, SerializationJsonContext.Default.Sample);
        Assert.True(back.IsSuccess);
        Assert.Equal(sample.Name, back.Value.Name);
        Assert.Equal(sample.Count, back.Value.Count);
        Assert.Equal(sample.Entries, back.Value.Entries);
    }

    [Fact]
    public void It_reports_a_document_that_does_not_map()
    {
        var table = new DataTable(
            "root",
            new DataSchema([new DataField("name", DataKind.Text), new DataField("count", DataKind.Text)]));
        table.AddRow([DataValue.From("x"), DataValue.From("not-a-number")]);

        var result = DataDocumentMapper.FromDocument(DataDocument.FromTable(table), SerializationJsonContext.Default.Sample);

        Assert.Equal("serialization.invalid_document", result.Error.Code);
    }

    [Fact]
    public void It_propagates_a_document_that_json_cannot_represent()
    {
        var root = new DataTable("root", new DataSchema([new DataField("a", DataKind.Integer)]));
        root.AddRow([DataValue.From(1L)]);
        root.AddRow([DataValue.From(2L)]);
        var document = new DataDocument(root, [new DataTable("s", DataSchema.Empty)]);

        var result = DataDocumentMapper.FromDocument(document, SerializationJsonContext.Default.Sample);

        Assert.Equal("serialization.invalid_document", result.Error.Code);
    }

    [Fact]
    public void It_refuses_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(
            () => DataDocumentMapper.ToDocument(new Sample("x", 1, []), null!));
        Assert.Throws<ArgumentNullException>(
            () => DataDocumentMapper.FromDocument<Sample>(DataDocument.FromTable(new DataTable("root", DataSchema.Empty)), null!));
        Assert.Throws<ArgumentNullException>(
            () => DataDocumentMapper.FromDocument(null!, SerializationJsonContext.Default.Sample));
    }

    [Fact]
    public void A_key_value_table_looks_up_first_and_all_values()
    {
        var table = KeyValueTable.Create("Interface");
        table.AddRow([DataValue.From("Address"), DataValue.From("10.0.0.1")]);
        table.AddRow([DataValue.From("Address"), DataValue.From("10.0.0.2")]);
        table.AddRow([DataValue.From("ListenPort"), DataValue.From("51820")]);

        Assert.Equal("key", table.Schema[0].Name);
        Assert.True(KeyValueTable.TryGetValue(table, "Address", out var first));
        Assert.Equal("10.0.0.1", first);
        Assert.False(KeyValueTable.TryGetValue(table, "missing", out var missing));
        Assert.Equal(string.Empty, missing);
        Assert.Equal(["10.0.0.1", "10.0.0.2"], KeyValueTable.GetValues(table, "Address"));
        Assert.Empty(KeyValueTable.GetValues(table, "missing"));
    }

    [Fact]
    public void A_key_value_lookup_refuses_null_and_a_thin_table()
    {
        var table = KeyValueTable.Create("Interface");
        table.AddRow([DataValue.From("a"), DataValue.From("1")]);
        var thin = new DataTable("thin", new DataSchema([new DataField("only", DataKind.Text)]));
        thin.AddRow([DataValue.From("a")]);

        Assert.Throws<ArgumentNullException>(() => KeyValueTable.Create(null!));
        Assert.Throws<ArgumentNullException>(() => KeyValueTable.TryGetValue(null!, "a", out _));
        Assert.Throws<ArgumentNullException>(() => KeyValueTable.TryGetValue(table, null!, out _));
        Assert.Throws<ArgumentNullException>(() => KeyValueTable.GetValues(table, null!));
        Assert.False(KeyValueTable.TryGetValue(thin, "a", out _));
    }

    [Fact]
    public void A_table_builder_builds_a_table()
    {
        var table = DataTableBuilder
            .Create("t")
            .Column("a", DataKind.Integer)
            .Column("b", DataKind.Text)
            .Row(DataValue.From(1L), DataValue.From("x"))
            .Row(DataValue.From(2L), DataValue.Null)
            .Build();

        Assert.Equal(2, table.RowCount);
        Assert.Equal(DataValue.From(2L), table[1, 0]);
        Assert.True(table[1, 1].IsNull);
    }

    [Fact]
    public void A_table_builder_refuses_null_and_a_bad_row()
    {
        Assert.Throws<ArgumentNullException>(() => DataTableBuilder.Create(null!));
        Assert.Throws<ArgumentNullException>(() => DataTableBuilder.Create("t").Row(null!));
        Assert.Throws<ArgumentException>(() => DataTableBuilder.Create("t").Column("a", DataKind.Integer).Row(DataValue.From(1L), DataValue.From(2L)).Build());
    }
}
