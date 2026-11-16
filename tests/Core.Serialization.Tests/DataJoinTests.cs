// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class DataJoinTests
{
    [Fact]
    public void An_inner_join_emits_one_row_per_right_match()
    {
        var joined = DataJoin.Join(Left(), Right(), [new JoinKey("id", "id")]).Value;

        Assert.Equal(2, joined.RowCount);
        Assert.Equal(["id", "name", "right.id", "right.value"], Names(joined));
        Assert.Equal(DataValue.From("b"), joined[0, 1]);
        Assert.Equal(DataValue.From("x"), joined[0, 3]);
        Assert.Equal(DataValue.From("y"), joined[1, 3]);
    }

    [Fact]
    public void A_left_join_keeps_unmatched_rows_with_null_right_cells()
    {
        var joined = DataJoin.Join(Left(), Right(), [new JoinKey("id", "id")], JoinKind.Left).Value;

        Assert.Equal(5, joined.RowCount);
        Assert.Equal(DataValue.From("a"), joined[0, 1]);
        Assert.True(joined[0, 3].IsNull);
        Assert.Equal(DataValue.From("d"), joined[4, 1]);
        Assert.True(joined[4, 3].IsNull);
    }

    [Fact]
    public void It_joins_on_several_keys()
    {
        var left = Table(
            "left",
            [new DataField("id", DataKind.Integer), new DataField("group", DataKind.Text), new DataField("name", DataKind.Text)],
            [Row(1, "a", "one"), Row(1, "b", "two"), Row(2, "a", "three")]);
        var right = Table(
            "right",
            [new DataField("id", DataKind.Integer), new DataField("group", DataKind.Text), new DataField("value", DataKind.Text)],
            [Row(1, "a", "x"), Row(2, "a", "y"), Row(1, "b", "z")]);

        var joined = DataJoin.Join(
            left,
            right,
            [new JoinKey("id", "id"), new JoinKey("group", "group")]).Value;

        Assert.Equal(3, joined.RowCount);
        Assert.Equal(DataValue.From("one"), joined[0, 2]);
        Assert.Equal(DataValue.From("x"), joined[0, 5]);
    }

    [Fact]
    public void A_null_key_never_matches()
    {
        var joined = DataJoin.Join(Left(), Right(), [new JoinKey("id", "id")]).Value;

        Assert.Equal(2, joined.RowCount);
    }

    [Fact]
    public void A_length_prefixed_key_cannot_be_forged_by_a_delimiter()
    {
        var left = Table(
            "left",
            [new DataField("key", DataKind.Text)],
            [Row("a|b"), Row("c")]);
        var right = Table(
            "right",
            [new DataField("key", DataKind.Text)],
            [Row("a"), Row("b|c")]);

        var joined = DataJoin.Join(left, right, [new JoinKey("key", "key")]).Value;

        Assert.Equal(0, joined.RowCount);
    }

    [Fact]
    public void It_rejects_an_invalid_specification()
    {
        var left = Left();
        var right = Right();

        Assert.Throws<ArgumentNullException>(() => DataJoin.Join(null!, right, [new JoinKey("id", "id")]));
        Assert.Throws<ArgumentNullException>(() => DataJoin.Join(left, null!, [new JoinKey("id", "id")]));
        Assert.Throws<ArgumentNullException>(() => DataJoin.Join(left, right, null!));
        Assert.Throws<ArgumentNullException>(() => DataJoin.Join(left, right, [new JoinKey("id", "id")], rightPrefix: null!));

        Assert.Equal("data.invalid_join", DataJoin.Join(left, right, []).Error.Code);
        Assert.Equal("data.invalid_join", DataJoin.Join(left, right, [new JoinKey("missing", "id")]).Error.Code);
        Assert.Equal("data.invalid_join", DataJoin.Join(left, right, [new JoinKey("id", "missing")]).Error.Code);
        Assert.Equal("data.invalid_join", DataJoin.Join(left, right, [new JoinKey("", "id")]).Error.Code);
        Assert.Equal("data.invalid_join", DataJoin.Join(left, right, [new JoinKey("id", "id")], (JoinKind)99).Error.Code);

        var textRight = Table("right", [new DataField("id", DataKind.Text)], [Row("1")]);
        Assert.Equal("data.invalid_join", DataJoin.Join(left, textRight, [new JoinKey("id", "id")]).Error.Code);
    }

    [Fact]
    public void It_rejects_a_duplicate_output_column_name()
    {
        var left = Table("left", [new DataField("id", DataKind.Integer), new DataField("right.value", DataKind.Text)], [Row(1, "a")]);
        var right = Table("right", [new DataField("id", DataKind.Integer), new DataField("value", DataKind.Text)], [Row(1, "b")]);

        Assert.Equal("data.invalid_join", DataJoin.Join(left, right, [new JoinKey("id", "id")]).Error.Code);
    }

    [Fact]
    public void A_join_key_is_a_value()
    {
        var key = new JoinKey("left", "right");

        Assert.Equal("left", key.LeftColumn);
        Assert.Equal("right", key.RightColumn);
        Assert.Equal(key, new JoinKey("left", "right"));
        var (left, right) = key;
        Assert.Equal("left", left);
        Assert.Equal("right", right);
        _ = key.ToString();
        _ = key.GetHashCode();
    }

    private static DataTable Left() => Table(
        "left",
        [new DataField("id", DataKind.Integer), new DataField("name", DataKind.Text)],
        [Row(1, "a"), Row(2, "b"), Row(3, "c"), Row(null, "d")]);

    private static DataTable Right() => Table(
        "right",
        [new DataField("id", DataKind.Integer), new DataField("value", DataKind.Text)],
        [Row(2, "x"), Row(2, "y"), Row(4, "z"), Row(null, "w")]);

    private static DataTable Table(string name, DataField[] fields, DataValue[][] rows)
    {
        var table = new DataTable(name, new DataSchema(fields));

        foreach (var row in rows)
        {
            table.AddRow(row);
        }

        return table;
    }

    private static DataValue[] Row(long? id, string value) =>
    [
        id is { } integer ? DataValue.From(integer) : DataValue.Null,
        DataValue.From(value),
    ];

    private static DataValue[] Row(long id, string group, string value) =>
    [
        DataValue.From(id),
        DataValue.From(group),
        DataValue.From(value),
    ];

    private static DataValue[] Row(string key) => [DataValue.From(key)];

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
