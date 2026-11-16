// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Units;

namespace Core.Tests;

public sealed class PowerTests
{
    [Fact]
    public void Constructor_exposes_the_exact_microwatts()
    {
        Assert.Equal(1234, new Power(1234).Microwatts);
    }

    [Fact]
    public void Factories_convert_exactly()
    {
        Assert.Equal(10, Power.FromMicrowatts(10).Microwatts);
        Assert.Equal(1_000, Power.FromMilliwatts(1).Microwatts);
        Assert.Equal(1_000_000, Power.FromWatts(1).Microwatts);
        Assert.Equal(1_000_000_000, Power.FromKilowatts(1).Microwatts);
        Assert.Equal(1_000_000_000_000, Power.FromMegawatts(1).Microwatts);
        Assert.Equal(1_000_000_000_000_000, Power.FromGigawatts(1).Microwatts);
        Assert.Equal(1_000_000_000_000_000_000, Power.FromTerawatts(1).Microwatts);
    }

    [Fact]
    public void Factory_overflow_throws()
    {
        Assert.Throws<OverflowException>(() => Power.FromWatts(long.MaxValue));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1500", 1500)]
    [InlineData("1500 uW", 1500)]
    [InlineData("1 µW", 1)]
    [InlineData("1 μW", 1)]
    [InlineData("1 mW", 1_000)]
    [InlineData("1.5 mW", 1_500)]
    [InlineData("1 W", 1_000_000)]
    [InlineData("+1 W", 1_000_000)]
    [InlineData("-1 W", -1_000_000)]
    [InlineData("1 kW", 1_000_000_000)]
    [InlineData("1 MW", 1_000_000_000_000)]
    [InlineData("1 GW", 1_000_000_000_000_000)]
    [InlineData("1 TW", 1_000_000_000_000_000_000)]
    [InlineData("  42 uW  ", 42)]
    public void TryParse_accepts_valid_powers(string text, long expected)
    {
        Assert.True(Power.TryParse(text, out var power));
        Assert.Equal(expected, power.Microwatts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("uW")]
    [InlineData(".")]
    [InlineData("1.")]
    [InlineData("1.2.3")]
    [InlineData("1.5 uW")]
    [InlineData("1X")]
    [InlineData("9223372036854775808 uW")]
    [InlineData("-9223372036854775809 uW")]
    [InlineData("9999999999999999999 TW")]
    public void TryParse_rejects_invalid_powers(string text)
    {
        Assert.False(Power.TryParse(text, out _));
    }

    [Fact]
    public void Parse_returns_success_or_a_stable_error()
    {
        Assert.True(Power.Parse("1 mW").IsSuccess);

        var failure = Power.Parse("nope");
        Assert.True(failure.IsFailure);
        Assert.Equal("units.invalid", failure.Error.Code);
    }

    [Fact]
    public void ToString_is_exact_and_round_trippable()
    {
        var power = Power.FromMilliwatts(1500);

        Assert.Equal("1500000 uW", power.ToString());
        Assert.True(Power.TryParse(power.ToString(), out var parsed));
        Assert.Equal(power, parsed);
    }

    [Theory]
    [InlineData(0, "0 uW")]
    [InlineData(500, "500 uW")]
    [InlineData(1_500, "1.5 mW")]
    [InlineData(1_500_000, "1.5 W")]
    [InlineData(1_500_000_000, "1.5 kW")]
    [InlineData(-2_000_000, "-2 W")]
    public void ToHumanString_uses_scaled_units(long microwatts, string expected)
    {
        Assert.Equal(expected, new Power(microwatts).ToHumanString());
    }

    [Fact]
    public void Equality_and_comparison_follow_the_microwatts()
    {
        var small = new Power(10);
        var large = new Power(20);

        Assert.True(small == new Power(10));
        Assert.True(small != large);
        Assert.True(small < large);
        Assert.True(small <= large);
        Assert.True(large > small);
        Assert.True(large >= small);
        Assert.True(small.Equals((object)new Power(10)));
        Assert.False(small.Equals("not a power"));
        Assert.Equal(small.GetHashCode(), new Power(10).GetHashCode());
        Assert.Equal(-1, small.CompareTo(large));
        Assert.Equal(1, large.CompareTo(small));
        Assert.Equal(0, small.CompareTo(new Power(10)));
    }

    [Fact]
    public void Arithmetic_adds_subtracts_and_negates()
    {
        Assert.Equal(new Power(30), new Power(10) + new Power(20));
        Assert.Equal(new Power(-10), new Power(10) - new Power(20));
        Assert.Equal(new Power(-10), -new Power(10));
        Assert.Throws<OverflowException>(() => new Power(long.MaxValue) + new Power(1));
        Assert.Throws<OverflowException>(() => new Power(long.MinValue) - new Power(1));
        Assert.Throws<OverflowException>(() => -new Power(long.MinValue));
    }
}
