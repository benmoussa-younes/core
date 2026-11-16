// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Globalization;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class DataValueTests
{
    [Fact]
    public void The_default_value_is_null()
    {
        DataValue value = default;

        Assert.Equal(DataKind.Null, value.Kind);
        Assert.True(value.IsNull);
        Assert.Equal(string.Empty, value.ToInvariantText());
        Assert.Equal(string.Empty, value.ToString());
        Assert.False(value.TryGetBoolean(out _));
        Assert.False(value.TryGetInteger(out _));
        Assert.False(value.TryGetNumber(out _));
        Assert.False(value.TryGetDecimal(out _));
        Assert.False(value.TryGetText(out _));
        Assert.False(value.TryGetDate(out _));
        Assert.False(value.TryGetTime(out _));
        Assert.False(value.TryGetTimestamp(out _));
        Assert.False(value.TryGetNode(out _));
    }

    [Fact]
    public void The_value_struct_is_three_machine_words()
    {
        // 8 (bits) + 8 (reference) + 4 (kind) padded to 24: one cache line holds
        // two values, and columnar buffers stay compact. Guarded so a re-added
        // inline decimal cannot silently double it.
        Assert.Equal(24, System.Runtime.CompilerServices.Unsafe.SizeOf<DataValue>());
    }

    [Fact]
    public void It_round_trips_a_boolean()
    {
        var value = DataValue.From(true);

        Assert.Equal(DataKind.Boolean, value.Kind);
        Assert.False(value.IsNull);
        Assert.True(value.TryGetBoolean(out var boolean) && boolean);
        Assert.Equal("true", value.ToInvariantText());
        Assert.True(DataValue.From(false).TryGetBoolean(out var falsy));
        Assert.False(falsy);
        Assert.Equal("false", DataValue.From(false).ToInvariantText());
    }

    [Fact]
    public void It_round_trips_an_integer_and_widens_it()
    {
        var value = DataValue.From(-5L);

        Assert.Equal(DataKind.Integer, value.Kind);
        Assert.True(value.TryGetInteger(out var integer));
        Assert.Equal(-5L, integer);
        Assert.True(value.TryGetNumber(out var number));
        Assert.Equal(-5d, number);
        Assert.True(value.TryGetDecimal(out var decimalValue));
        Assert.Equal(-5m, decimalValue);
        Assert.Equal("-5", value.ToInvariantText());
    }

    [Fact]
    public void It_round_trips_a_number()
    {
        var value = DataValue.From(1.5);

        Assert.Equal(DataKind.Number, value.Kind);
        Assert.True(value.TryGetNumber(out var number));
        Assert.Equal(1.5, number);
        Assert.Equal("1.5", value.ToInvariantText());
        Assert.False(value.TryGetDecimal(out _));
        Assert.False(value.TryGetInteger(out _));
    }

    [Fact]
    public void It_round_trips_a_decimal()
    {
        var value = DataValue.From(1.5m);

        Assert.Equal(DataKind.Decimal, value.Kind);
        Assert.True(value.TryGetDecimal(out var decimalValue));
        Assert.Equal(1.5m, decimalValue);
        Assert.Equal("1.5", value.ToInvariantText());
        Assert.False(value.TryGetNumber(out _));
    }

    [Fact]
    public void It_round_trips_text()
    {
        var value = DataValue.From("hello");

        Assert.Equal(DataKind.Text, value.Kind);
        Assert.True(value.TryGetText(out var text));
        Assert.Equal("hello", text);
        Assert.Equal("hello", value.ToInvariantText());
        Assert.False(value.TryGetInteger(out _));
    }

    [Fact]
    public void It_refuses_a_null_text()
    {
        Assert.Throws<ArgumentNullException>(() => DataValue.From(null!));
    }

    [Fact]
    public void It_round_trips_a_date()
    {
        var date = new DateOnly(2026, 9, 29);
        var value = DataValue.From(date);

        Assert.Equal(DataKind.Date, value.Kind);
        Assert.True(value.TryGetDate(out var stored));
        Assert.Equal(date, stored);
        Assert.Equal("2026-09-29", value.ToInvariantText());
        Assert.False(value.TryGetTime(out _));
    }

    [Fact]
    public void It_round_trips_a_time()
    {
        var time = new TimeOnly(1, 2, 3);
        var value = DataValue.From(time);

        Assert.Equal(DataKind.Time, value.Kind);
        Assert.True(value.TryGetTime(out var stored));
        Assert.Equal(time, stored);
        Assert.Equal("01:02:03.0000000", value.ToInvariantText());
        Assert.False(value.TryGetDate(out _));
    }

    [Fact]
    public void It_round_trips_a_timestamp_and_its_kind()
    {
        var instant = new DateTime(2026, 9, 29, 12, 30, 0, DateTimeKind.Utc);
        var value = DataValue.From(instant);

        Assert.Equal(DataKind.Timestamp, value.Kind);
        Assert.True(value.TryGetTimestamp(out var stored));
        Assert.Equal(instant, stored);
        Assert.Equal(DateTimeKind.Utc, stored.Kind);
        Assert.Equal(instant.ToString("O", CultureInfo.InvariantCulture), value.ToInvariantText());
        Assert.False(value.TryGetDate(out _));
    }

    [Fact]
    public void It_round_trips_a_node()
    {
        var node = DataNode.Scalar(DataValue.From(1L));
        var value = DataValue.FromNode(node);

        Assert.Equal(DataKind.Node, value.Kind);
        Assert.True(value.TryGetNode(out var stored));
        Assert.Same(node, stored);
        Assert.Equal("Node", value.ToString());
        Assert.Throws<InvalidOperationException>(() => value.ToInvariantText());
    }

    [Fact]
    public void It_refuses_a_null_node()
    {
        Assert.Throws<ArgumentNullException>(() => DataValue.FromNode(null!));
    }

    [Fact]
    public void Equal_values_are_equal_and_share_a_hash()
    {
        AssertEqual(DataValue.Null, DataValue.Null);
        AssertEqual(DataValue.From(true), DataValue.From(true));
        AssertEqual(DataValue.From(7L), DataValue.From(7L));
        AssertEqual(DataValue.From(1.5), DataValue.From(1.5));
        AssertEqual(DataValue.From(1.5m), DataValue.From(1.5m));
        AssertEqual(DataValue.From("x"), DataValue.From("x"));
        AssertEqual(DataValue.From(new DateOnly(2026, 1, 1)), DataValue.From(new DateOnly(2026, 1, 1)));
        AssertEqual(DataValue.From(new TimeOnly(1, 0)), DataValue.From(new TimeOnly(1, 0)));
        AssertEqual(
            DataValue.From(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            DataValue.From(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        var node = DataNode.Scalar(DataValue.From(1L));
        AssertEqual(DataValue.FromNode(node), DataValue.FromNode(node));
    }

    [Fact]
    public void Different_values_are_unequal()
    {
        Assert.NotEqual(DataValue.From(true), DataValue.From(false));
        Assert.NotEqual(DataValue.From(1L), DataValue.From(2L));
        Assert.NotEqual(DataValue.From(1.5), DataValue.From(2.5));
        Assert.NotEqual(DataValue.From(1.5m), DataValue.From(2.5m));
        Assert.NotEqual(DataValue.From("x"), DataValue.From("y"));
        Assert.NotEqual(DataValue.From(new DateOnly(2026, 1, 1)), DataValue.From(new DateOnly(2026, 1, 2)));
        Assert.NotEqual(DataValue.From(new TimeOnly(1, 0)), DataValue.From(new TimeOnly(2, 0)));
        Assert.NotEqual(
            DataValue.From(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            DataValue.From(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)));

        var node = DataNode.Scalar(DataValue.From(1L));
        Assert.NotEqual(DataValue.FromNode(node), DataValue.FromNode(DataNode.Scalar(DataValue.From(1L))));

        // A different kind is unequal even when the payload looks alike.
        Assert.NotEqual(DataValue.From(1L), DataValue.From(1.0));
        Assert.NotEqual(DataValue.Null, DataValue.From(0L));
        Assert.NotEqual(DataValue.From("1"), DataValue.From(1L));
    }

    [Fact]
    public void Object_equality_and_operators_agree()
    {
        var value = DataValue.From(3L);

        Assert.True(value.Equals((object)DataValue.From(3L)));
        Assert.False(value.Equals("not a value"));
        Assert.True(value == DataValue.From(3L));
        Assert.False(value != DataValue.From(3L));
        Assert.False(value == DataValue.From(4L));
        Assert.True(value != DataValue.From(4L));
    }

    private static void AssertEqual(DataValue left, DataValue right)
    {
        Assert.True(left.Equals(right));
        Assert.True(left == right);
        Assert.False(left != right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }
}
