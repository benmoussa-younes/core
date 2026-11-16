// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Units;

namespace Core.Tests;

public sealed class EnergyTests
{
    [Fact]
    public void Constructor_exposes_the_exact_microjoules()
    {
        Assert.Equal(1234, new Energy(1234).Microjoules);
    }

    [Fact]
    public void Factories_convert_exactly()
    {
        Assert.Equal(10, Energy.FromMicrojoules(10).Microjoules);
        Assert.Equal(1_000, Energy.FromMillijoules(1).Microjoules);
        Assert.Equal(1_000_000, Energy.FromJoules(1).Microjoules);
        Assert.Equal(1_000_000_000, Energy.FromKilojoules(1).Microjoules);
        Assert.Equal(1_000_000_000_000, Energy.FromMegajoules(1).Microjoules);
        Assert.Equal(1_000_000_000_000_000, Energy.FromGigajoules(1).Microjoules);
        Assert.Equal(1_000_000_000_000_000_000, Energy.FromTerajoules(1).Microjoules);
    }

    [Fact]
    public void Factory_overflow_throws()
    {
        Assert.Throws<OverflowException>(() => Energy.FromJoules(long.MaxValue));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1500", 1500)]
    [InlineData("1500 uJ", 1500)]
    [InlineData("1 µJ", 1)]
    [InlineData("1 μJ", 1)]
    [InlineData("1 mJ", 1_000)]
    [InlineData("1.5 mJ", 1_500)]
    [InlineData("1 J", 1_000_000)]
    [InlineData("+1 J", 1_000_000)]
    [InlineData("-1 J", -1_000_000)]
    [InlineData("1 kJ", 1_000_000_000)]
    [InlineData("1 MJ", 1_000_000_000_000)]
    [InlineData("1 GJ", 1_000_000_000_000_000)]
    [InlineData("1 TJ", 1_000_000_000_000_000_000)]
    [InlineData("  42 uJ  ", 42)]
    public void TryParse_accepts_valid_energies(string text, long expected)
    {
        Assert.True(Energy.TryParse(text, out var energy));
        Assert.Equal(expected, energy.Microjoules);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("uJ")]
    [InlineData(".")]
    [InlineData("1.")]
    [InlineData("1.2.3")]
    [InlineData("1.5 uJ")]
    [InlineData("1X")]
    [InlineData("9223372036854775808 uJ")]
    [InlineData("-9223372036854775809 uJ")]
    [InlineData("9223372036854776 kJ")]
    [InlineData("-9223372036854776 kJ")]
    [InlineData("99999999999999999999999999999999999999.0 uJ")]
    [InlineData("9223372036854775808.0 mJ")]
    [InlineData("9999999999999999999 TJ")]
    public void TryParse_rejects_invalid_energies(string text)
    {
        Assert.False(Energy.TryParse(text, out _));
    }

    [Fact]
    public void Parse_returns_success_or_a_stable_error()
    {
        Assert.True(Energy.Parse("1 mJ").IsSuccess);

        var failure = Energy.Parse("nope");
        Assert.True(failure.IsFailure);
        Assert.Equal("units.invalid", failure.Error.Code);
    }

    [Fact]
    public void ToString_is_exact_and_round_trippable()
    {
        var energy = Energy.FromMillijoules(1500);

        Assert.Equal("1500000 uJ", energy.ToString());
        Assert.True(Energy.TryParse(energy.ToString(), out var parsed));
        Assert.Equal(energy, parsed);
    }

    [Theory]
    [InlineData(0, "0 uJ")]
    [InlineData(500, "500 uJ")]
    [InlineData(1_500, "1.5 mJ")]
    [InlineData(1_250, "1.3 mJ")]
    [InlineData(999_950, "1 J")]
    [InlineData(1_500_000, "1.5 J")]
    [InlineData(-1_000_000, "-1 J")]
    public void ToHumanString_uses_scaled_units(long microjoules, string expected)
    {
        Assert.Equal(expected, new Energy(microjoules).ToHumanString());
    }

    [Fact]
    public void Equality_and_comparison_follow_the_microjoules()
    {
        var small = new Energy(10);
        var large = new Energy(20);

        Assert.True(small == new Energy(10));
        Assert.True(small != large);
        Assert.True(small < large);
        Assert.True(small <= large);
        Assert.True(large > small);
        Assert.True(large >= small);
        Assert.True(small.Equals((object)new Energy(10)));
        Assert.False(small.Equals("not an energy"));
        Assert.Equal(small.GetHashCode(), new Energy(10).GetHashCode());
        Assert.Equal(-1, small.CompareTo(large));
        Assert.Equal(1, large.CompareTo(small));
        Assert.Equal(0, small.CompareTo(new Energy(10)));
    }

    [Fact]
    public void Arithmetic_adds_subtracts_and_negates()
    {
        Assert.Equal(new Energy(30), new Energy(10) + new Energy(20));
        Assert.Equal(new Energy(-10), new Energy(10) - new Energy(20));
        Assert.Equal(new Energy(-10), -new Energy(10));
        Assert.Throws<OverflowException>(() => new Energy(long.MaxValue) + new Energy(1));
        Assert.Throws<OverflowException>(() => new Energy(long.MinValue) - new Energy(1));
        Assert.Throws<OverflowException>(() => -new Energy(long.MinValue));
    }
}
