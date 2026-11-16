// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;

namespace Core.Tests;

public sealed class CoreErrorTests
{
    [Fact]
    public void Constructor_exposes_code_and_message()
    {
        var error = new CoreError("usage", "invalid arguments");

        Assert.Equal("usage", error.Code);
        Assert.Equal("invalid arguments", error.Message);
    }

    [Fact]
    public void Records_with_same_values_are_equal()
    {
        Assert.Equal(new CoreError("code", "message"), new CoreError("code", "message"));
    }

    [Fact]
    public void Null_code_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CoreError(null!, "message"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_or_whitespace_code_throws(string code)
    {
        Assert.Throws<ArgumentException>(() => new CoreError(code, "message"));
    }

    [Fact]
    public void Null_message_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CoreError("code", null!));
    }
}
