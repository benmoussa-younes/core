// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class DataColumnTests
{
    [Fact]
    public void It_exposes_typed_spans_without_a_value_round_trip()
    {
        var integers = new DataColumn("i", DataKind.Integer);
        integers.Add(DataValue.From(1L));
        Assert.Equal(1L, integers.Integers[0]);

        var numbers = new DataColumn("n", DataKind.Number);
        numbers.Add(DataValue.From(1.5));
        Assert.Equal(1.5, numbers.Numbers[0]);

        var booleans = new DataColumn("b", DataKind.Boolean);
        booleans.Add(DataValue.From(true));
        Assert.True(booleans.Booleans[0]);

        var decimals = new DataColumn("d", DataKind.Decimal);
        decimals.Add(DataValue.From(1.5m));
        Assert.Equal(1.5m, decimals.Decimals[0]);

        var dates = new DataColumn("dt", DataKind.Date);
        dates.Add(DataValue.From(new DateOnly(2026, 1, 1)));
        Assert.Equal(new DateOnly(2026, 1, 1), dates.Dates[0]);

        var times = new DataColumn("t", DataKind.Time);
        times.Add(DataValue.From(new TimeOnly(1, 0)));
        Assert.Equal(new TimeOnly(1, 0), times.Times[0]);

        var stamps = new DataColumn("ts", DataKind.Timestamp);
        var instant = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        stamps.Add(DataValue.From(instant));
        Assert.Equal(instant, stamps.Timestamps[0]);

        var texts = new DataColumn("x", DataKind.Text);
        texts.Add(DataValue.From("hi"));
        Assert.Equal("hi", texts.Texts[0]);

        Assert.Throws<InvalidOperationException>(() => { _ = integers.Numbers; });
    }

    [Fact]
    public void It_refuses_a_null_name_and_an_invalid_kind()
    {
        Assert.Throws<ArgumentNullException>(() => new DataColumn(null!, DataKind.Text));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataColumn("c", DataKind.Null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataColumn("c", (DataKind)99));
    }

    [Fact]
    public void It_stores_booleans_and_nulls()
    {
        AssertStores(DataKind.Boolean, DataValue.From(true), DataValue.From(1L));
    }

    [Fact]
    public void It_stores_integers_and_nulls()
    {
        AssertStores(DataKind.Integer, DataValue.From(7L), DataValue.From(true));
    }

    [Fact]
    public void It_stores_numbers_and_nulls()
    {
        AssertStores(DataKind.Number, DataValue.From(1.5), DataValue.From(true));
    }

    [Fact]
    public void It_stores_decimals_and_nulls()
    {
        AssertStores(DataKind.Decimal, DataValue.From(1.5m), DataValue.From(1.5));
    }

    [Fact]
    public void It_stores_text_and_nulls()
    {
        AssertStores(DataKind.Text, DataValue.From("hello"), DataValue.From(1L));
    }

    [Fact]
    public void It_stores_dates_and_nulls()
    {
        AssertStores(DataKind.Date, DataValue.From(new DateOnly(2026, 9, 29)), DataValue.From(1L));
    }

    [Fact]
    public void It_stores_times_and_nulls()
    {
        AssertStores(DataKind.Time, DataValue.From(new TimeOnly(1, 2, 3)), DataValue.From(1L));
    }

    [Fact]
    public void It_stores_timestamps_and_nulls()
    {
        AssertStores(DataKind.Timestamp, DataValue.From(new DateTime(2026, 9, 29, 1, 2, 3, DateTimeKind.Utc)), DataValue.From(1L));
    }

    [Fact]
    public void It_stores_nodes_and_nulls()
    {
        AssertStores(DataKind.Node, DataValue.FromNode(DataNode.Scalar(DataValue.From(1L))), DataValue.From("x"));
    }

    [Fact]
    public void It_widens_an_integer_into_a_number_or_decimal_column()
    {
        var number = new DataColumn("n", DataKind.Number);
        number.Add(DataValue.From(2L));
        Assert.True(number.Get(0).TryGetNumber(out var asNumber));
        Assert.Equal(2d, asNumber);

        var decimalColumn = new DataColumn("d", DataKind.Decimal);
        decimalColumn.Add(DataValue.From(2L));
        Assert.True(decimalColumn.Get(0).TryGetDecimal(out var asDecimal));
        Assert.Equal(2m, asDecimal);
    }

    [Fact]
    public void It_sets_a_value_at_a_row()
    {
        var column = new DataColumn("c", DataKind.Integer);
        column.Add(DataValue.From(1L));
        column.Add(DataValue.From(2L));

        column.Set(0, DataValue.From(9L));

        Assert.Equal(DataValue.From(9L), column.Get(0));
        Assert.Equal(DataValue.From(2L), column.Get(1));
    }

    [Fact]
    public void CanStore_refuses_a_value_in_the_null_kind_and_accepts_null()
    {
        Assert.False(DataColumn.CanStore(DataKind.Null, DataValue.From(1L)));
        Assert.True(DataColumn.CanStore(DataKind.Text, DataValue.Null));
    }

    private static void AssertStores(DataKind kind, DataValue value, DataValue mismatched)
    {
        var column = new DataColumn("c", kind);

        column.Add(value);
        column.Add(DataValue.Null);

        Assert.Equal(2, column.Count);
        Assert.Equal(kind, column.Kind);
        Assert.Equal("c", column.Name);
        Assert.Equal(value, column.Get(0));
        Assert.True(column.Get(1).IsNull);

        column.Set(1, value);
        Assert.Equal(value, column.Get(1));

        Assert.Throws<ArgumentException>(() => column.Add(mismatched));
        Assert.Throws<ArgumentException>(() => column.Set(0, mismatched));
        Assert.Throws<ArgumentOutOfRangeException>(() => column.Get(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => column.Get(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => column.Set(2, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => column.Set(-1, value));
    }
}
