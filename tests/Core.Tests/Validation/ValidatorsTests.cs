// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Validation;

namespace Core.Tests;

public sealed class ValidatorsTests
{
    [Fact]
    public void NotBlank_accepts_a_value_and_rejects_null_or_whitespace()
    {
        Assert.Equal("value", Validators.NotBlank("value", "Name").Value);
        Assert.Equal("validation.required", Validators.NotBlank(null, "Name").Error.Code);
        Assert.Equal("validation.required", Validators.NotBlank(" ", "Name").Error.Code);
        Assert.Throws<ArgumentNullException>(() => Validators.NotBlank("value", null!));
    }

    [Fact]
    public void MaxLength_checks_blank_and_the_boundary()
    {
        Assert.Equal("abc", Validators.MaxLength("abc", 3, "Name").Value);
        Assert.Equal("validation.too_long", Validators.MaxLength("abcd", 3, "Name").Error.Code);
        Assert.Equal("validation.required", Validators.MaxLength(null, 3, "Name").Error.Code);
        Assert.Throws<ArgumentOutOfRangeException>(() => Validators.MaxLength("a", 0, "Name"));
        Assert.Throws<ArgumentNullException>(() => Validators.MaxLength("a", 1, null!));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    [InlineData(65_535)]
    public void Port_accepts_the_valid_range(int port)
    {
        Assert.Equal(port, Validators.Port(port).Value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(65_536)]
    public void Port_rejects_the_invalid_range(int port)
    {
        Assert.Equal("validation.port", Validators.Port(port).Error.Code);
    }
}
