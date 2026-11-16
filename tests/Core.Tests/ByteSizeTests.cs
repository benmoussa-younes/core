// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Primitives;

namespace Core.Tests;

public sealed class ByteSizeTests
{
    [Fact]
    public void Constructor_rejects_negative_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ByteSize(-1));
    }

    [Fact]
    public void Bytes_exposes_the_exact_count()
    {
        Assert.Equal(1234, new ByteSize(1234).Bytes);
    }

    [Fact]
    public void FromBytes_and_binary_factories_convert_exactly()
    {
        Assert.Equal(10, ByteSize.FromBytes(10).Bytes);
        Assert.Equal(1024, ByteSize.FromKibibytes(1).Bytes);
        Assert.Equal(1024 * 1024, ByteSize.FromMebibytes(1).Bytes);
        Assert.Equal(1024L * 1024 * 1024, ByteSize.FromGibibytes(1).Bytes);
    }

    [Fact]
    public void Binary_factory_overflow_throws()
    {
        Assert.Throws<OverflowException>(() => ByteSize.FromKibibytes(long.MaxValue));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1024", 1024)]
    [InlineData("1B", 1)]
    [InlineData("1K", 1024)]
    [InlineData("1KiB", 1024)]
    [InlineData("1KB", 1000)]
    [InlineData("1.5 MiB", 1572864)]
    [InlineData("2MB", 2000000)]
    [InlineData("1 GiB", 1073741824)]
    [InlineData("1TiB", 1099511627776)]
    [InlineData("1T", 1099511627776)]
    [InlineData("1PiB", 1125899906842624)]
    [InlineData("1GB", 1000000000)]
    [InlineData("1TB", 1000000000000)]
    [InlineData("1pb", 1000000000000000)]
    [InlineData("  42 B  ", 42)]
    [InlineData("9223372036854775807.0 B", 9223372036854775807)]
    [InlineData("999999999999999999 B", 999999999999999999)]
    [InlineData("9223372036854775807 B", 9223372036854775807)]
    [InlineData("1.0000000000000000000 B", 1)]
    [InlineData("0.0000000000000000000 B", 0)]
    [InlineData("9000000000000000.0 KiB", 9216000000000000000)]
    public void TryParse_accepts_valid_sizes(string text, long expected)
    {
        Assert.True(ByteSize.TryParse(text, out var size));
        Assert.Equal(expected, size.Bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.5B")]
    [InlineData("-1")]
    [InlineData("1X")]
    [InlineData("B")]
    [InlineData("1.")]
    [InlineData(".5 B")]
    [InlineData("1.2.3")]
    [InlineData("1.2.3 B")]
    [InlineData("9223372036854775808 B")]
    [InlineData("99999999999999999999")]
    [InlineData("99999P")]
    [InlineData("9999999.5P")]
    [InlineData("9999999999999999999PB")]
    [InlineData("0.0000000000000000001 B")]
    [InlineData("9999999999999999999999999999999999999999.5 B")]
    [InlineData("9223372036854775808.0 B")]
    [InlineData("9223372036854775807.1 B")]
    [InlineData("9223372036854775806.9 B")]
    [InlineData("9000000000000000.1 KiB")]
    [InlineData("99999999999999999999.2.3 B")]
    public void TryParse_rejects_invalid_sizes(string text)
    {
        Assert.False(ByteSize.TryParse(text, out _));
    }

    [Fact]
    public void Parse_returns_success_or_a_stable_error()
    {
        Assert.True(ByteSize.Parse("1 KiB").IsSuccess);

        var failure = ByteSize.Parse("nope");
        Assert.True(failure.IsFailure);
        Assert.Equal("bytesize.invalid", failure.Error.Code);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(42, "42 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KiB")]
    [InlineData(1536, "1.5 KiB")]
    [InlineData(1997, "2 KiB")]
    [InlineData(1048575, "1 MiB")]
    [InlineData(1048576, "1 MiB")]
    [InlineData(1125899906842624, "1 PiB")]
    public void ToHumanString_uses_binary_units(long bytes, string expected)
    {
        Assert.Equal(expected, new ByteSize(bytes).ToHumanString());
    }

    [Fact]
    public void ToString_is_exact_and_round_trippable()
    {
        var size = new ByteSize(1536);

        Assert.Equal("1536 B", size.ToString());
        Assert.True(ByteSize.TryParse(size.ToString(), out var parsed));
        Assert.Equal(size, parsed);
    }

    [Fact]
    public void Equality_and_comparison_follow_the_byte_count()
    {
        var small = new ByteSize(10);
        var large = new ByteSize(20);

        Assert.True(small == new ByteSize(10));
        Assert.True(small != large);
        Assert.True(small < large);
        Assert.True(small <= large);
        Assert.True(large > small);
        Assert.True(large >= small);
        Assert.True(small.Equals((object)new ByteSize(10)));
        Assert.False(small.Equals("not a size"));
        Assert.Equal(small.GetHashCode(), new ByteSize(10).GetHashCode());
        Assert.Equal(-1, small.CompareTo(large));
        Assert.Equal(1, large.CompareTo(small));
        Assert.Equal(0, small.CompareTo(new ByteSize(10)));
    }
}
