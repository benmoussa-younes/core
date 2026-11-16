// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class DataViewTests
{
    [Fact]
    public void It_sorts_stably_and_keeps_nulls_last_in_both_directions()
    {
        var view = new DataView(Table());

        var ascending = view.Sort(new SortKey("id")).Value;
        Assert.Equal(["a", "c", "b", "d"], Names(ascending));

        var descending = view.Sort(new SortKey("id", Descending: true)).Value;
        Assert.Equal(["b", "a", "c", "d"], Names(descending));
    }

    [Fact]
    public void The_integer_radix_sort_is_stable_for_duplicate_keys()
    {
        var table = new DataTable(
            "t",
            new DataSchema([new DataField("id", DataKind.Integer), new DataField("name", DataKind.Text)]));
        table.AddRow([DataValue.From(2L), DataValue.From("first")]);
        table.AddRow([DataValue.From(1L), DataValue.From("one")]);
        table.AddRow([DataValue.From(2L), DataValue.From("second")]);
        table.AddRow([DataValue.From(1L), DataValue.From("two")]);

        var ascending = new DataView(table).Sort(new SortKey("id")).Value;
        Assert.Equal(["one", "two", "first", "second"], Names(ascending));

        var descending = new DataView(table).Sort(new SortKey("id", Descending: true)).Value;
        Assert.Equal(["first", "second", "one", "two"], Names(descending));
    }

    [Fact]
    public void The_integer_radix_sort_orders_signed_values()
    {
        var table = new DataTable(
            "t",
            new DataSchema([new DataField("id", DataKind.Integer), new DataField("name", DataKind.Text)]));
        table.AddRow([DataValue.From(5L), DataValue.From("five")]);
        table.AddRow([DataValue.From(-1L), DataValue.From("minus-one")]);
        table.AddRow([DataValue.From(long.MinValue), DataValue.From("minimum")]);
        table.AddRow([DataValue.From(0L), DataValue.From("zero")]);
        table.AddRow([DataValue.From(long.MaxValue), DataValue.From("maximum")]);
        table.AddRow([DataValue.From(-1L), DataValue.From("minus-one-again")]);

        var sorted = new DataView(table).Sort(new SortKey("id")).Value;

        Assert.Equal(
            ["minimum", "minus-one", "minus-one-again", "zero", "five", "maximum"],
            Names(sorted));
    }

    [Fact]
    public void It_sorts_two_null_keys_last_and_stably()
    {
        var table = new DataTable(
            "t",
            new DataSchema([new DataField("id", DataKind.Integer), new DataField("name", DataKind.Text)]));
        table.AddRow([DataValue.Null, DataValue.From("a")]);
        table.AddRow([DataValue.Null, DataValue.From("b")]);
        table.AddRow([DataValue.From(1L), DataValue.From("c")]);

        var sorted = new DataView(table).Sort(new SortKey("id")).Value;

        Assert.Equal(["c", "a", "b"], Names(sorted));
    }

    [Fact]
    public void A_multi_key_sort_is_stable_when_every_key_ties()
    {
        var table = new DataTable(
            "t",
            new DataSchema(
            [
                new DataField("id", DataKind.Integer),
                new DataField("name", DataKind.Text),
                new DataField("order", DataKind.Text),
            ]));
        table.AddRow([DataValue.From(1L), DataValue.From("a"), DataValue.From("first")]);
        table.AddRow([DataValue.From(1L), DataValue.From("a"), DataValue.From("second")]);
        table.AddRow([DataValue.From(2L), DataValue.From("b"), DataValue.From("third")]);

        var sorted = new DataView(table).Sort(new SortKey("id"), new SortKey("name")).Value;

        Assert.Equal("first", Field(sorted[0], "order"));
        Assert.Equal("second", Field(sorted[1], "order"));
        Assert.Equal("third", Field(sorted[2], "order"));
    }

    [Fact]
    public void It_sorts_by_several_keys()
    {
        var view = new DataView(Table());

        var sorted = view.Sort(new SortKey("id"), new SortKey("name", Descending: true)).Value;

        Assert.Equal(["c", "a", "b", "d"], Names(sorted));
    }

    [Fact]
    public void It_sorts_every_column_kind()
    {
        var view = new DataView(Table());

        foreach (var column in new[] { "name", "amount", "score", "date", "time", "timestamp", "flag", "node" })
        {
            var sorted = view.Sort(new SortKey(column)).Value;
            Assert.Equal(4, sorted.Count);
            Assert.Equal("d", Name(sorted[3]));
        }
    }

    [Fact]
    public void An_empty_key_list_keeps_the_view_and_an_unknown_key_is_rejected()
    {
        var view = new DataView(Table());

        Assert.Same(view, view.Sort().Value);
        Assert.Throws<ArgumentNullException>(() => view.Sort(null!));
        Assert.Equal("data.invalid_sort", view.Sort(new SortKey("missing")).Error.Code);
        Assert.Equal("data.invalid_sort", view.Sort(new SortKey(" ")).Error.Code);
    }

    [Fact]
    public void It_filters_and_materializes_the_visible_rows()
    {
        var view = new DataView(Table());
        var filtered = view.Filter(row =>
            row.TryGetValue("name", out var name) && name.ToInvariantText() is "a" or "b");

        Assert.Equal(2, filtered.Count);
        Assert.Equal(["b", "a"], Names(filtered));
        Assert.Equal(4, view.Count);

        var table = filtered.ToTable();
        Assert.Equal(2, table.RowCount);
        Assert.Equal(Table().Schema.Count, table.Schema.Count);
    }

    [Fact]
    public void It_filters_by_table_row_index()
    {
        var view = new DataView(Table());
        var filtered = view.FilterRows(index => index % 2 == 0);

        Assert.Equal(2, filtered.Count);
        Assert.Equal([0, 2], filtered.RowIndexes.ToArray());
        Assert.Throws<ArgumentNullException>(() => view.FilterRows(null!));
    }

    [Fact]
    public void It_indexes_the_visible_rows_and_exposes_the_row_indexes()
    {
        var view = new DataView(Table());

        Assert.Equal("b", Name(view[0]));
        Assert.Equal([0, 1, 2, 3], view.RowIndexes.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => view[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => view[4]);
    }

    [Fact]
    public void It_validates_its_constructors_and_predicate()
    {
        Assert.Throws<ArgumentNullException>(() => new DataView(null!));
        Assert.Throws<ArgumentNullException>(() => new DataView(Table()).Filter(null!));
    }

    [Fact]
    public void A_sort_key_is_a_value()
    {
        var key = new SortKey("id", Descending: true);

        Assert.Equal("id", key.Column);
        Assert.True(key.Descending);
        Assert.Equal(key, new SortKey("id", true));
        Assert.NotEqual(key, new SortKey("id"));
        var (column, descending) = key;
        Assert.Equal("id", column);
        Assert.True(descending);
        _ = key.ToString();
        _ = key.GetHashCode();
    }

    internal static DataTable Table()
    {
        var table = new DataTable(
            "t",
            new DataSchema(
            [
                new DataField("id", DataKind.Integer),
                new DataField("name", DataKind.Text),
                new DataField("amount", DataKind.Decimal),
                new DataField("score", DataKind.Number),
                new DataField("date", DataKind.Date),
                new DataField("time", DataKind.Time),
                new DataField("timestamp", DataKind.Timestamp),
                new DataField("flag", DataKind.Boolean),
                new DataField("node", DataKind.Node),
            ]));

        table.AddRow(Row(2, "b", 10.5m, 1.5, 2, false, "z"));
        table.AddRow(Row(1, "a", 2.5m, 2.5, 1, false, "a"));
        table.AddRow(Row(1, "c", 30.5m, 3.5, 3, true, "c"));
        table.AddRow(Row(null, "d", null, null, null, null, null));

        return table;
    }

    private static DataValue[] Row(
        long? id,
        string name,
        decimal? amount,
        double? score,
        int? day,
        bool? flag,
        string? node) =>
    [
        id is { } integer ? DataValue.From(integer) : DataValue.Null,
        DataValue.From(name),
        amount is { } number ? DataValue.From(number) : DataValue.Null,
        score is { } floating ? DataValue.From(floating) : DataValue.Null,
        day is { } date ? DataValue.From(new DateOnly(2026, 1, date)) : DataValue.Null,
        day is { } time ? DataValue.From(new TimeOnly(time, 0, 0)) : DataValue.Null,
        day is { } timestamp ? DataValue.From(new DateTime(2026, 1, timestamp, 0, 0, 0, DateTimeKind.Utc)) : DataValue.Null,
        flag is { } boolean ? DataValue.From(boolean) : DataValue.Null,
        node is { } text ? DataValue.FromNode(DataNode.Scalar(DataValue.From(text))) : DataValue.Null,
    ];

    private static List<string> Names(DataView view)
    {
        var names = new List<string>(view.Count);

        for (var index = 0; index < view.Count; index++)
        {
            names.Add(Name(view[index]));
        }

        return names;
    }

    private static string Name(DataRow row) => Field(row, "name");

    private static string Field(DataRow row, string name)
    {
        Assert.True(row.TryGetValue(name, out var value));

        return value.ToInvariantText();
    }
}
