// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class DataTableTests
{
    private static DataSchema Schema => new([new DataField("a", DataKind.Integer), new DataField("b", DataKind.Text)]);

    [Fact]
    public void It_looks_a_column_up_by_name()
    {
        var table = new DataTable("t", Schema);

        Assert.True(table.TryGetColumn("a", out var a));
        Assert.Equal(DataKind.Integer, a!.Kind);
        Assert.False(table.TryGetColumn("missing", out var missing));
        Assert.Null(missing);
        Assert.Throws<ArgumentNullException>(() => table.TryGetColumn(null!, out _));
    }

    [Fact]
    public void It_starts_empty()
    {
        var table = new DataTable("t", Schema);

        Assert.Equal("t", table.Name);
        Assert.Equal(2, table.ColumnCount);
        Assert.Equal(0, table.RowCount);
        Assert.Equal(DataKind.Integer, table.Column(0).Kind);
        Assert.True(table.TryGetColumnIndex("b", out var index));
        Assert.Equal(1, index);
        Assert.False(table.TryGetColumnIndex("missing", out _));
    }

    [Fact]
    public void It_refuses_null_inputs()
    {
        Assert.Throws<ArgumentNullException>(() => new DataTable(null!, Schema));
        Assert.Throws<ArgumentNullException>(() => new DataTable("t", null!));
    }

    [Fact]
    public void It_appends_and_reads_rows()
    {
        var table = new DataTable("t", Schema);
        table.AddRow([DataValue.From(1L), DataValue.From("x")]);
        table.AddRow([DataValue.From(2L), DataValue.Null]);

        Assert.Equal(2, table.RowCount);
        Assert.Equal(DataValue.From(1L), table[0, 0]);
        Assert.Equal(DataValue.From("x"), table[0, 1]);

        var row = table.GetRow(1);
        Assert.Equal(1, row.Index);
        Assert.Same(table, row.Table);
        Assert.Equal(2, row.Count);
        Assert.Equal(DataValue.From(2L), row[0]);
        Assert.True(row.TryGetValue("b", out var value));
        Assert.True(value.IsNull);
        Assert.False(row.TryGetValue("missing", out var missing));
        Assert.True(missing.IsNull);
    }

    [Fact]
    public void A_row_with_the_wrong_value_count_is_refused_unchanged()
    {
        var table = new DataTable("t", Schema);

        Assert.Throws<ArgumentException>(() => table.AddRow([DataValue.From(1L)]));
        Assert.Equal(0, table.RowCount);
        Assert.Equal(0, table.Column(0).Count);
    }

    [Fact]
    public void A_row_with_a_mismatched_value_is_refused_unchanged()
    {
        var table = new DataTable("t", Schema);

        Assert.Throws<ArgumentException>(() => table.AddRow([DataValue.From("x"), DataValue.From("y")]));
        Assert.Equal(0, table.RowCount);
        Assert.Equal(0, table.Column(0).Count);
        Assert.Equal(0, table.Column(1).Count);
    }

    [Fact]
    public void An_out_of_range_row_or_column_is_refused()
    {
        var table = new DataTable("t", Schema);

        Assert.Throws<ArgumentOutOfRangeException>(() => table.GetRow(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.GetRow(-1));
        Assert.Throws<IndexOutOfRangeException>(() => table.Column(2));
    }

    [Fact]
    public void An_empty_schema_table_holds_counted_rows()
    {
        var table = new DataTable("t", DataSchema.Empty);
        table.AddRow([]);

        Assert.Equal(1, table.RowCount);
        Assert.Equal(0, table.ColumnCount);
    }
}
