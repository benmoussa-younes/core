// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class DataProjectionTests
{
    [Fact]
    public void Select_keeps_the_requested_columns_in_order()
    {
        var table = Table();

        var selected = DataProjection.Select(table, ["name", "id"]).Value;

        Assert.Equal(["name", "id"], Names(selected));
        Assert.Equal(2, selected.RowCount);
        Assert.Equal(DataValue.From("a"), selected[0, 0]);
        Assert.Equal(DataValue.From(1L), selected[0, 1]);
        Assert.Equal(3, table.ColumnCount);
    }

    [Fact]
    public void Drop_keeps_the_remaining_columns_in_order()
    {
        var dropped = DataProjection.Drop(Table(), ["id"]).Value;

        Assert.Equal(["name", "amount"], Names(dropped));
        Assert.Equal(2, dropped.RowCount);
    }

    [Fact]
    public void It_rejects_an_unknown_empty_or_duplicate_projection()
    {
        var table = Table();

        Assert.Equal("data.invalid_projection", DataProjection.Select(table, []).Error.Code);
        Assert.Equal("data.invalid_projection", DataProjection.Select(table, ["missing"]).Error.Code);
        Assert.Equal("data.invalid_projection", DataProjection.Select(table, ["id", "id"]).Error.Code);
        Assert.Equal("data.invalid_projection", DataProjection.Drop(table, []).Error.Code);
        Assert.Equal("data.invalid_projection", DataProjection.Drop(table, ["missing"]).Error.Code);
        Assert.Equal("data.invalid_projection", DataProjection.Drop(table, ["id", "name", "amount"]).Error.Code);

        Assert.Throws<ArgumentNullException>(() => DataProjection.Select(null!, ["id"]));
        Assert.Throws<ArgumentNullException>(() => DataProjection.Select(table, null!));
        Assert.Throws<ArgumentNullException>(() => DataProjection.Drop(null!, ["id"]));
        Assert.Throws<ArgumentNullException>(() => DataProjection.Drop(table, null!));
    }

    private static DataTable Table()
    {
        var table = new DataTable(
            "t",
            new DataSchema(
            [
                new DataField("id", DataKind.Integer),
                new DataField("name", DataKind.Text),
                new DataField("amount", DataKind.Decimal),
            ]));

        table.AddRow([DataValue.From(1L), DataValue.From("a"), DataValue.From(1.5m)]);
        table.AddRow([DataValue.From(2L), DataValue.From("b"), DataValue.From(2.5m)]);

        return table;
    }

    private static List<string> Names(DataTable table)
    {
        var names = new List<string>(table.ColumnCount);

        for (var column = 0; column < table.ColumnCount; column++)
        {
            names.Add(table.Schema[column].Name);
        }

        return names;
    }
}
