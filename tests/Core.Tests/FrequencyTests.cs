// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Units;

namespace Core.Tests;

public sealed class FrequencyTests
{
    [Fact]
    public void Constructor_exposes_the_exact_hertz()
    {
        Assert.Equal(1234, new Frequency(1234).Hertz);
    }

    [Fact]
    public void Factories_convert_exactly()
    {
        Assert.Equal(10, Frequency.FromHertz(10).Hertz);
        Assert.Equal(1_000, Frequency.FromKilohertz(1).Hertz);
        Assert.Equal(1_000_000, Frequency.FromMegahertz(1).Hertz);
        Assert.Equal(1_000_000_000, Frequency.FromGigahertz(1).Hertz);
        Assert.Equal(1_000_000_000_000, Frequency.FromTerahertz(1).Hertz);
        Assert.Equal(1_000_000_000_000_000, Frequency.FromPetahertz(1).Hertz);
        Assert.Equal(1_000_000_000_000_000_000, Frequency.FromExahertz(1).Hertz);
    }

    [Fact]
    public void Factory_overflow_throws()
    {
        Assert.Throws<OverflowException>(() => Frequency.FromKilohertz(long.MaxValue));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1500", 1500)]
    [InlineData("1500 Hz", 1500)]
    [InlineData("1 kHz", 1_000)]
    [InlineData("1.5 kHz", 1_500)]
    [InlineData("1 MHz", 1_000_000)]
    [InlineData("+1 MHz", 1_000_000)]
    [InlineData("-1 MHz", -1_000_000)]
    [InlineData("1 GHz", 1_000_000_000)]
    [InlineData("1 THz", 1_000_000_000_000)]
    [InlineData("1 PHz", 1_000_000_000_000_000)]
    [InlineData("1 EHz", 1_000_000_000_000_000_000)]
    [InlineData("  42 Hz  ", 42)]
    public void TryParse_accepts_valid_frequencies(string text, long expected)
    {
        Assert.True(Frequency.TryParse(text, out var frequency));
        Assert.Equal(expected, frequency.Hertz);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("Hz")]
    [InlineData(".")]
    [InlineData("1.")]
    [InlineData("1.2.3")]
    [InlineData("1.5 Hz")]
    [InlineData("1X")]
    [InlineData("9223372036854775808 Hz")]
    [InlineData("-9223372036854775809 Hz")]
    [InlineData("9999999999999999999 EHz")]
    public void TryParse_rejects_invalid_frequencies(string text)
    {
        Assert.False(Frequency.TryParse(text, out _));
    }

    [Fact]
    public void Parse_returns_success_or_a_stable_error()
    {
        Assert.True(Frequency.Parse("1 kHz").IsSuccess);

        var failure = Frequency.Parse("nope");
        Assert.True(failure.IsFailure);
        Assert.Equal("units.invalid", failure.Error.Code);
    }

    [Fact]
    public void ToString_is_exact_and_round_trippable()
    {
        var frequency = Frequency.FromMegahertz(3);

        Assert.Equal("3000000 Hz", frequency.ToString());
        Assert.True(Frequency.TryParse(frequency.ToString(), out var parsed));
        Assert.Equal(frequency, parsed);
    }

    [Theory]
    [InlineData(0, "0 Hz")]
    [InlineData(999, "999 Hz")]
    [InlineData(1_500, "1.5 kHz")]
    [InlineData(3_000_000_000, "3 GHz")]
    [InlineData(-1_000_000, "-1 MHz")]
    public void ToHumanString_uses_scaled_units(long hertz, string expected)
    {
        Assert.Equal(expected, new Frequency(hertz).ToHumanString());
    }

    [Fact]
    public void Equality_and_comparison_follow_the_hertz()
    {
        var small = new Frequency(10);
        var large = new Frequency(20);

        Assert.True(small == new Frequency(10));
        Assert.True(small != large);
        Assert.True(small < large);
        Assert.True(small <= large);
        Assert.True(large > small);
        Assert.True(large >= small);
        Assert.True(small.Equals((object)new Frequency(10)));
        Assert.False(small.Equals("not a frequency"));
        Assert.Equal(small.GetHashCode(), new Frequency(10).GetHashCode());
        Assert.Equal(-1, small.CompareTo(large));
        Assert.Equal(1, large.CompareTo(small));
        Assert.Equal(0, small.CompareTo(new Frequency(10)));
    }

    [Fact]
    public void Arithmetic_adds_subtracts_and_negates()
    {
        Assert.Equal(new Frequency(30), new Frequency(10) + new Frequency(20));
        Assert.Equal(new Frequency(-10), new Frequency(10) - new Frequency(20));
        Assert.Equal(new Frequency(-10), -new Frequency(10));
        Assert.Throws<OverflowException>(() => new Frequency(long.MaxValue) + new Frequency(1));
        Assert.Throws<OverflowException>(() => new Frequency(long.MinValue) - new Frequency(1));
        Assert.Throws<OverflowException>(() => -new Frequency(long.MinValue));
    }
}
