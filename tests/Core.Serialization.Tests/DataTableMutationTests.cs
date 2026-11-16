// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class DataTableMutationTests
{
    [Fact]
    public void It_inserts_a_row_at_the_start_middle_and_end()
    {
        var table = Table();
        table.AddRow([DataValue.From(1L), DataValue.From("one")]);
        table.AddRow([DataValue.From(3L), DataValue.From("three")]);

        table.InsertRow(1, [DataValue.From(2L), DataValue.From("two")]);
        table.InsertRow(0, [DataValue.From(0L), DataValue.From("zero")]);
        table.InsertRow(4, [DataValue.From(4L), DataValue.From("four")]);

        Assert.Equal(5, table.RowCount);
        Assert.Equal([0L, 1L, 2L, 3L, 4L], Enumerable.Range(0, 5).Select(row => table[row, 0].TryGetInteger(out var value) ? value : -1));
        Assert.Equal(DataValue.From("four"), table[4, 1]);
    }

    [Fact]
    public void It_removes_a_row_and_shifts_the_rest_up()
    {
        var table = Table();
        table.AddRow([DataValue.From(1L), DataValue.From("one")]);
        table.AddRow([DataValue.From(2L), DataValue.From("two")]);
        table.AddRow([DataValue.From(3L), DataValue.From("three")]);

        table.RemoveRow(1);

        Assert.Equal(2, table.RowCount);
        Assert.Equal(DataValue.From(1L), table[0, 0]);
        Assert.Equal(DataValue.From(3L), table[1, 0]);
    }

    [Fact]
    public void A_mismatched_inserted_row_leaves_the_table_unchanged()
    {
        var table = Table();
        table.AddRow([DataValue.From(1L), DataValue.From("one")]);

        Assert.Throws<ArgumentException>(() => table.InsertRow(0, [DataValue.From("x"), DataValue.From("y")]));

        Assert.Equal(1, table.RowCount);
        Assert.Equal(DataValue.From(1L), table[0, 0]);
    }

    [Fact]
    public void It_appends_a_column_padded_with_nulls()
    {
        var table = Table();
        table.AddRow([DataValue.From(1L), DataValue.From("one")]);

        table.AddColumn(new DataField("c", DataKind.Boolean));

        Assert.Equal(3, table.ColumnCount);
        Assert.Equal("c", table.Schema[2].Name);
        Assert.Equal(DataKind.Boolean, table.Schema[2].Kind);
        Assert.True(table[0, 2].IsNull);
    }

    [Fact]
    public void A_duplicate_column_name_is_refused_atomically()
    {
        var table = Table();
        table.AddRow([DataValue.From(1L), DataValue.From("one")]);

        Assert.Throws<ArgumentException>(() => table.AddColumn(new DataField("a", DataKind.Text)));

        Assert.Equal(2, table.ColumnCount);
        Assert.Equal("a", table.Schema[0].Name);
    }

    [Fact]
    public void It_inserts_and_removes_a_column()
    {
        var table = Table();

        table.InsertColumn(1, new DataField("mid", DataKind.Text));

        Assert.Equal(["a", "mid", "b"], Enumerable.Range(0, 3).Select(index => table.Schema[index].Name));

        table.RemoveColumnAt(1);

        Assert.Equal(["a", "b"], Enumerable.Range(0, 2).Select(index => table.Schema[index].Name));
    }

    [Fact]
    public void It_renames_a_column()
    {
        var table = Table();

        table.RenameColumn(0, "amount");

        Assert.Equal("amount", table.Column(0).Name);
        Assert.Equal("amount", table.Schema[0].Name);
        Assert.True(table.TryGetColumn("amount", out _));
        Assert.False(table.TryGetColumn("a", out _));
    }

    [Fact]
    public void A_duplicate_rename_is_refused_atomically()
    {
        var table = Table();

        Assert.Throws<ArgumentException>(() => table.RenameColumn(0, "b"));

        Assert.Equal("a", table.Column(0).Name);
        Assert.Equal("a", table.Schema[0].Name);
    }

    [Fact]
    public void It_moves_a_column()
    {
        var table = new DataTable("root", new DataSchema(
        [
            new DataField("a", DataKind.Integer),
            new DataField("b", DataKind.Integer),
            new DataField("c", DataKind.Integer),
        ]));
        table.AddRow([DataValue.From(1L), DataValue.From(2L), DataValue.From(3L)]);

        table.MoveColumn(0, 2);

        Assert.Equal(["b", "c", "a"], Enumerable.Range(0, 3).Select(index => table.Schema[index].Name));
        Assert.Equal(DataValue.From(2L), table[0, 0]);
        Assert.Equal(DataValue.From(1L), table[0, 2]);
    }

    [Fact]
    public void Insert_and_remove_round_trip_every_storage_kind()
    {
        var table = new DataTable("root", new DataSchema(
        [
            new DataField("integer", DataKind.Integer),
            new DataField("number", DataKind.Number),
            new DataField("decimal", DataKind.Decimal),
            new DataField("boolean", DataKind.Boolean),
            new DataField("text", DataKind.Text),
            new DataField("date", DataKind.Date),
            new DataField("time", DataKind.Time),
            new DataField("timestamp", DataKind.Timestamp),
        ]));

        var first = Row(10);
        var second = Row(20);
        table.AddRow(first);
        table.InsertRow(1, second);
        table.RemoveRow(0);

        Assert.Equal(1, table.RowCount);
        Assert.Equal(DataValue.From(20L), table[0, 0]);
        Assert.Equal(DataValue.From("text20"), table[0, 4]);
        Assert.Equal(DataValue.From(new DateOnly(2026, 1, 21)), table[0, 5]);

        static DataValue[] Row(int seed) =>
        [
            DataValue.From((long)seed),
            DataValue.From(seed + 0.5),
            DataValue.From(seed + 0.25m),
            DataValue.From((seed & 1) == 0),
            DataValue.From("text" + seed),
            DataValue.From(new DateOnly(2026, 1, seed + 1)),
            DataValue.From(new TimeOnly(seed % 24, 0)),
            DataValue.From(new DateTime(2026, 1, 1, seed % 24, 0, 0, DateTimeKind.Utc)),
        ];
    }

    [Fact]
    public void Out_of_range_edits_are_rejected()
    {
        var table = Table();
        table.AddRow([DataValue.From(1L), DataValue.From("one")]);

        Assert.Throws<ArgumentOutOfRangeException>(() => table.InsertRow(-1, [DataValue.From(1L), DataValue.From("x")]));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.InsertRow(3, [DataValue.From(1L), DataValue.From("x")]));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.RemoveRow(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.InsertColumn(3, new DataField("x", DataKind.Text)));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.RemoveColumnAt(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.RenameColumn(2, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.MoveColumn(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.MoveColumn(-1, 0));
    }

    [Fact]
    public void A_mismatched_column_insert_is_rejected()
    {
        var table = Table();

        Assert.Throws<ArgumentException>(() => table.Column(0).Insert(0, DataValue.From("x")));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Column(0).RemoveAt(0));
    }

    [Fact]
    public void A_node_column_round_trips_insert_and_remove()
    {
        var zero = DataNode.Scalar(DataValue.From(0L));
        var one = DataNode.Scalar(DataValue.From(1L));
        var table = new DataTable("root", new DataSchema([new DataField("n", DataKind.Node)]));
        table.AddRow([DataValue.FromNode(one)]);
        table.InsertRow(0, [DataValue.FromNode(zero)]);

        Assert.Equal(2, table.RowCount);
        Assert.True(table[0, 0].TryGetNode(out var first));
        Assert.Same(zero, first);

        table.RemoveRow(0);

        Assert.Equal(1, table.RowCount);
        Assert.True(table[0, 0].TryGetNode(out var remaining));
        Assert.Same(one, remaining);
    }

    [Fact]
    public void Moving_a_column_to_its_own_index_is_a_noop()
    {
        var table = Table();

        table.MoveColumn(0, 0);

        Assert.Equal(["a", "b"], Enumerable.Range(0, 2).Select(index => table.Schema[index].Name));
    }

    [Fact]
    public void It_moves_a_column_earlier()
    {
        var table = new DataTable("root", new DataSchema(
        [
            new DataField("a", DataKind.Integer),
            new DataField("b", DataKind.Integer),
            new DataField("c", DataKind.Integer),
        ]));

        table.MoveColumn(2, 0);

        Assert.Equal(["c", "a", "b"], Enumerable.Range(0, 3).Select(index => table.Schema[index].Name));
    }

    [Fact]
    public void It_sets_a_cell_in_every_kind()
    {
        var table = new DataTable("root", new DataSchema(
        [
            new DataField("integer", DataKind.Integer),
            new DataField("number", DataKind.Number),
            new DataField("decimal", DataKind.Decimal),
            new DataField("boolean", DataKind.Boolean),
            new DataField("text", DataKind.Text),
            new DataField("date", DataKind.Date),
            new DataField("time", DataKind.Time),
            new DataField("timestamp", DataKind.Timestamp),
        ]));
        table.AddRow(
        [
            DataValue.From(0L),
            DataValue.From(0.0),
            DataValue.From(0.0m),
            DataValue.From(true),
            DataValue.From("old"),
            DataValue.From(new DateOnly(2026, 1, 1)),
            DataValue.From(new TimeOnly(0, 0)),
            DataValue.From(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
        ]);

        table.SetCell(0, 0, DataValue.From(11L));
        table.SetCell(0, 1, DataValue.From(11.5));
        table.SetCell(0, 2, DataValue.From(11.25m));
        table.SetCell(0, 3, DataValue.From(false));
        table.SetCell(0, 4, DataValue.From("changed"));
        table.SetCell(0, 5, DataValue.From(new DateOnly(2026, 2, 2)));
        table.SetCell(0, 6, DataValue.From(new TimeOnly(2, 3, 4)));
        table.SetCell(0, 7, DataValue.From(new DateTime(2026, 2, 2, 2, 3, 4, DateTimeKind.Utc)));

        Assert.Equal(DataValue.From(11L), table[0, 0]);
        Assert.Equal(DataValue.From(11.5), table[0, 1]);
        Assert.Equal(DataValue.From(11.25m), table[0, 2]);
        Assert.Equal(DataValue.From(false), table[0, 3]);
        Assert.Equal(DataValue.From("changed"), table[0, 4]);
        Assert.Equal(DataValue.From(new DateOnly(2026, 2, 2)), table[0, 5]);
        Assert.Equal(DataValue.From(new TimeOnly(2, 3, 4)), table[0, 6]);
        Assert.Equal(DataValue.From(new DateTime(2026, 2, 2, 2, 3, 4, DateTimeKind.Utc)), table[0, 7]);
    }

    [Fact]
    public void A_mismatched_set_cell_leaves_the_table_unchanged()
    {
        var table = Table();
        table.AddRow([DataValue.From(1L), DataValue.From("one")]);

        Assert.Throws<ArgumentException>(() => table.SetCell(0, 0, DataValue.From("x")));

        Assert.Equal(DataValue.From(1L), table[0, 0]);
        Assert.Equal(DataValue.From("one"), table[0, 1]);
    }

    [Fact]
    public void An_out_of_range_set_cell_is_rejected()
    {
        var table = Table();
        table.AddRow([DataValue.From(1L), DataValue.From("one")]);

        Assert.Throws<ArgumentOutOfRangeException>(() => table.SetCell(1, 0, DataValue.From(2L)));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.SetCell(-1, 0, DataValue.From(2L)));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.SetCell(0, 2, DataValue.From(2L)));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.SetCell(0, -1, DataValue.From(2L)));
    }

    [Fact]
    public void It_replaces_a_text_column_with_an_integer_column()
    {
        var table = Table();
        table.AddRow([DataValue.From(1L), DataValue.From("one")]);
        table.AddRow([DataValue.From(2L), DataValue.From("two")]);

        table.ReplaceColumnAt(1, new DataField("count", DataKind.Integer), [DataValue.From(10L), DataValue.From(20L)]);

        Assert.Equal("count", table.Schema[1].Name);
        Assert.Equal(DataKind.Integer, table.Schema[1].Kind);
        Assert.Equal(DataValue.From(10L), table[0, 1]);
        Assert.Equal(DataValue.From(20L), table[1, 1]);
        Assert.Equal(DataValue.From(1L), table[0, 0]);
    }

    [Fact]
    public void A_wrong_replacement_value_count_is_refused_atomically()
    {
        var table = Table();
        table.AddRow([DataValue.From(1L), DataValue.From("one")]);

        Assert.Throws<ArgumentException>(
            () => table.ReplaceColumnAt(1, new DataField("count", DataKind.Integer), [DataValue.From(10L), DataValue.From(20L)]));

        Assert.Equal(2, table.ColumnCount);
        Assert.Equal("b", table.Schema[1].Name);
        Assert.Equal(DataKind.Text, table.Schema[1].Kind);
        Assert.Equal(DataValue.From("one"), table[0, 1]);
    }

    [Fact]
    public void A_duplicate_replacement_name_is_refused_atomically()
    {
        var table = Table();
        table.AddRow([DataValue.From(1L), DataValue.From("one")]);

        Assert.Throws<ArgumentException>(
            () => table.ReplaceColumnAt(1, new DataField("a", DataKind.Integer), [DataValue.From(10L)]));

        Assert.Equal("b", table.Schema[1].Name);
        Assert.Equal(DataKind.Text, table.Schema[1].Kind);
        Assert.Equal(DataValue.From("one"), table[0, 1]);
    }

    [Fact]
    public void An_unstorable_replacement_value_is_refused_atomically()
    {
        var table = Table();
        table.AddRow([DataValue.From(1L), DataValue.From("one")]);

        Assert.Throws<ArgumentException>(
            () => table.ReplaceColumnAt(1, new DataField("count", DataKind.Integer), [DataValue.From("x")]));

        Assert.Equal("b", table.Schema[1].Name);
        Assert.Equal(DataValue.From("one"), table[0, 1]);
    }

    [Fact]
    public void An_out_of_range_replacement_is_rejected()
    {
        var table = Table();

        Assert.Throws<ArgumentOutOfRangeException>(() => table.ReplaceColumnAt(2, new DataField("x", DataKind.Text), []));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.ReplaceColumnAt(-1, new DataField("x", DataKind.Text), []));
    }

    private static DataTable Table() =>
        new("root", new DataSchema([new DataField("a", DataKind.Integer), new DataField("b", DataKind.Text)]));
}
