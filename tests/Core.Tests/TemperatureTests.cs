// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Units;

namespace Core.Tests;

public sealed class TemperatureTests
{
    [Fact]
    public void Constructor_exposes_the_exact_millidegrees()
    {
        Assert.Equal(1234, new Temperature(1234).MillidegreesCelsius);
    }

    [Fact]
    public void Factories_convert_exactly()
    {
        Assert.Equal(10, Temperature.FromMillidegreesCelsius(10).MillidegreesCelsius);
        Assert.Equal(21_000, Temperature.FromDegreesCelsius(21).MillidegreesCelsius);
        Assert.Equal(-273_150, Temperature.FromKelvin(0).MillidegreesCelsius);
        Assert.Equal(26_850, Temperature.FromKelvin(300).MillidegreesCelsius);
    }

    [Fact]
    public void Factory_overflow_throws()
    {
        Assert.Throws<OverflowException>(() => Temperature.FromDegreesCelsius(long.MaxValue));
        Assert.Throws<OverflowException>(() => Temperature.FromKelvin(long.MinValue));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1500", 1500)]
    [InlineData("1500 mC", 1500)]
    [InlineData("1 m°C", 1)]
    [InlineData("1 C", 1_000)]
    [InlineData("1 °C", 1_000)]
    [InlineData("21.5 C", 21_500)]
    [InlineData("+1 C", 1_000)]
    [InlineData("-1 C", -1_000)]
    [InlineData("300 K", 26_850)]
    [InlineData("0 K", -273_150)]
    [InlineData("1.5 K", -271_650)]
    [InlineData("  42 mC  ", 42)]
    public void TryParse_accepts_valid_temperatures(string text, long expected)
    {
        Assert.True(Temperature.TryParse(text, out var temperature));
        Assert.Equal(expected, temperature.MillidegreesCelsius);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("mC")]
    [InlineData(".")]
    [InlineData("1.")]
    [InlineData("1.2.3")]
    [InlineData("1X")]
    [InlineData("9223372036854775808 mC")]
    [InlineData("-9223372036854775809 mC")]
    [InlineData("-9223372036854775.808 K")]
    public void TryParse_rejects_invalid_temperatures(string text)
    {
        Assert.False(Temperature.TryParse(text, out _));
    }

    [Fact]
    public void Parse_returns_success_or_a_stable_error()
    {
        Assert.True(Temperature.Parse("300 K").IsSuccess);

        var failure = Temperature.Parse("nope");
        Assert.True(failure.IsFailure);
        Assert.Equal("units.invalid", failure.Error.Code);
    }

    [Fact]
    public void ToString_is_exact_and_round_trippable()
    {
        var temperature = Temperature.FromDegreesCelsius(21);

        Assert.Equal("21000 mC", temperature.ToString());
        Assert.True(Temperature.TryParse(temperature.ToString(), out var parsed));
        Assert.Equal(temperature, parsed);
    }

    [Theory]
    [InlineData(0, "0 mC")]
    [InlineData(500, "500 mC")]
    [InlineData(1_500, "1.5 C")]
    [InlineData(21_000, "21 C")]
    [InlineData(26_850, "26.9 C")]
    [InlineData(-1_000, "-1 C")]
    public void ToHumanString_uses_scaled_units(long millidegrees, string expected)
    {
        Assert.Equal(expected, new Temperature(millidegrees).ToHumanString());
    }

    [Fact]
    public void Equality_and_comparison_follow_the_millidegrees()
    {
        var small = new Temperature(10);
        var large = new Temperature(20);

        Assert.True(small == new Temperature(10));
        Assert.True(small != large);
        Assert.True(small < large);
        Assert.True(small <= large);
        Assert.True(large > small);
        Assert.True(large >= small);
        Assert.True(small.Equals((object)new Temperature(10)));
        Assert.False(small.Equals("not a temperature"));
        Assert.Equal(small.GetHashCode(), new Temperature(10).GetHashCode());
        Assert.Equal(-1, small.CompareTo(large));
        Assert.Equal(1, large.CompareTo(small));
        Assert.Equal(0, small.CompareTo(new Temperature(10)));
    }

    [Fact]
    public void Arithmetic_adds_subtracts_and_negates()
    {
        Assert.Equal(new Temperature(30), new Temperature(10) + new Temperature(20));
        Assert.Equal(new Temperature(-10), new Temperature(10) - new Temperature(20));
        Assert.Equal(new Temperature(-10), -new Temperature(10));
        Assert.Throws<OverflowException>(() => new Temperature(long.MaxValue) + new Temperature(1));
        Assert.Throws<OverflowException>(() => new Temperature(long.MinValue) - new Temperature(1));
        Assert.Throws<OverflowException>(() => -new Temperature(long.MinValue));
    }
}
