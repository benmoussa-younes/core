// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class DataSchemaTests
{
    [Fact]
    public void It_keeps_the_fields_in_order()
    {
        var schema = new DataSchema([new DataField("a", DataKind.Integer), new DataField("b", DataKind.Text)]);

        Assert.Equal(2, schema.Count);
        Assert.Equal("a", schema[0].Name);
        Assert.Equal("b", schema[1].Name);
        Assert.Equal(DataKind.Integer, schema.KindOf(0));
        Assert.Equal(DataKind.Text, schema.KindOf(1));
        Assert.Equal(2, schema.Fields.Count);
    }

    [Fact]
    public void The_empty_schema_has_no_fields()
    {
        Assert.Equal(0, DataSchema.Empty.Count);
    }

    [Fact]
    public void It_refuses_null_fields()
    {
        Assert.Throws<ArgumentNullException>(() => new DataSchema(null!));
    }

    [Fact]
    public void It_refuses_a_duplicate_name()
    {
        Assert.Throws<ArgumentException>(
            () => new DataSchema([new DataField("a", DataKind.Text), new DataField("a", DataKind.Integer)]));
    }

    [Fact]
    public void It_looks_a_field_up_ordinally()
    {
        var schema = new DataSchema([new DataField("a", DataKind.Integer)]);

        Assert.True(schema.TryGetIndex("a", out var index));
        Assert.Equal(0, index);
        Assert.False(schema.TryGetIndex("A", out _));
        Assert.False(schema.TryGetIndex("missing", out _));
        Assert.Throws<ArgumentNullException>(() => schema.TryGetIndex(null!, out _));
    }

    [Fact]
    public void An_out_of_range_index_is_refused()
    {
        var schema = new DataSchema([new DataField("a", DataKind.Integer)]);

        Assert.Throws<IndexOutOfRangeException>(() => schema[1]);
        Assert.Throws<IndexOutOfRangeException>(() => schema.KindOf(1));
    }
}
