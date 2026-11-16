// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class DataAggregatesTests
{
    [Fact]
    public void It_counts_the_non_null_cells()
    {
        var table = DataViewTests.Table();

        Assert.Equal(3, DataAggregates.CountNonNull(table.Column(0)));
    }

    [Fact]
    public void It_sums_and_averages_integer_decimal_and_number_columns()
    {
        var table = DataViewTests.Table();

        Assert.Equal(DataValue.From(4L), DataAggregates.Sum(table.Column(0)).Value);
        Assert.Equal(DataValue.From(43.5m), DataAggregates.Sum(table.Column(2)).Value);
        Assert.Equal(DataValue.From(7.5), DataAggregates.Sum(table.Column(3)).Value);

        Assert.True(DataAggregates.Average(table.Column(0)).Value.TryGetNumber(out var integerAverage));
        Assert.Equal(4d / 3d, integerAverage);

        Assert.True(DataAggregates.Average(table.Column(2)).Value.TryGetDecimal(out var decimalAverage));
        Assert.Equal(43.5m / 3m, decimalAverage);

        Assert.True(DataAggregates.Average(table.Column(3)).Value.TryGetNumber(out var numberAverage));
        Assert.Equal(2.5, numberAverage);
    }

    [Fact]
    public void It_finds_minimum_maximum_first_and_last_and_skips_nulls()
    {
        var table = DataViewTests.Table();

        Assert.Equal(DataValue.From(1L), DataAggregates.Min(table.Column(0)).Value);
        Assert.Equal(DataValue.From(2L), DataAggregates.Max(table.Column(0)).Value);
        Assert.Equal(DataValue.From(2L), DataAggregates.First(table.Column(0)).Value);
        Assert.Equal(DataValue.From(1L), DataAggregates.Last(table.Column(0)).Value);

        Assert.Equal(DataValue.From("a"), DataAggregates.Min(table.Column(1)).Value);
        Assert.Equal(DataValue.From("d"), DataAggregates.Max(table.Column(1)).Value);
        Assert.Equal(DataValue.From("b"), DataAggregates.First(table.Column(1)).Value);
        Assert.Equal(DataValue.From("d"), DataAggregates.Last(table.Column(1)).Value);
    }

    [Fact]
    public void A_non_numeric_column_is_a_typed_failure()
    {
        var table = DataViewTests.Table();

        Assert.Equal("data.not_numeric", DataAggregates.Sum(table.Column(1)).Error.Code);
        Assert.Equal("data.not_numeric", DataAggregates.Sum(table.Column(7)).Error.Code);
        Assert.Equal("data.not_numeric", DataAggregates.Average(table.Column(1)).Error.Code);
    }

    [Fact]
    public void An_empty_or_all_null_column_is_a_typed_failure()
    {
        var table = new DataTable(
            "empty",
            new DataSchema([new DataField("value", DataKind.Integer)]));
        table.AddRow([DataValue.Null]);

        var column = table.Column(0);
        Assert.Equal("data.empty", DataAggregates.Average(column).Error.Code);
        Assert.Equal("data.empty", DataAggregates.Min(column).Error.Code);
        Assert.Equal("data.empty", DataAggregates.Max(column).Error.Code);
        Assert.Equal("data.empty", DataAggregates.First(column).Error.Code);
        Assert.Equal("data.empty", DataAggregates.Last(column).Error.Code);
        Assert.Equal(DataValue.From(0L), DataAggregates.Sum(column).Value);
    }

    [Fact]
    public void An_overflow_is_a_typed_failure()
    {
        var integer = new DataTable("i", new DataSchema([new DataField("value", DataKind.Integer)]));
        integer.AddRow([DataValue.From(long.MaxValue)]);
        integer.AddRow([DataValue.From(1L)]);
        Assert.Equal("data.overflow", DataAggregates.Sum(integer.Column(0)).Error.Code);
        Assert.Equal("data.overflow", DataAggregates.Average(integer.Column(0)).Error.Code);

        var decimalTable = new DataTable("d", new DataSchema([new DataField("value", DataKind.Decimal)]));
        decimalTable.AddRow([DataValue.From(decimal.MaxValue)]);
        decimalTable.AddRow([DataValue.From(1m)]);
        Assert.Equal("data.overflow", DataAggregates.Sum(decimalTable.Column(0)).Error.Code);

        var number = new DataTable("n", new DataSchema([new DataField("value", DataKind.Number)]));
        number.AddRow([DataValue.From(double.MaxValue)]);
        number.AddRow([DataValue.From(double.MaxValue)]);
        Assert.Equal("data.overflow", DataAggregates.Sum(number.Column(0)).Error.Code);
    }

    [Fact]
    public void It_validates_its_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => DataAggregates.CountNonNull(null!));
        Assert.Throws<ArgumentNullException>(() => DataAggregates.Sum(null!));
        Assert.Throws<ArgumentNullException>(() => DataAggregates.Average(null!));
        Assert.Throws<ArgumentNullException>(() => DataAggregates.Min(null!));
        Assert.Throws<ArgumentNullException>(() => DataAggregates.Max(null!));
        Assert.Throws<ArgumentNullException>(() => DataAggregates.First(null!));
        Assert.Throws<ArgumentNullException>(() => DataAggregates.Last(null!));
    }
}
