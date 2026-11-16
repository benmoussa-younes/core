// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Primitives;
using Core.Results;

namespace Core.Tests;

public sealed class PathComponentTests
{
    [Theory]
    [InlineData("settings.json")]
    [InlineData("a b")]
    [InlineData("café")]
    public void A_valid_component_round_trips(string value)
    {
        var result = PathComponent.Validate(value);

        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value);
        Assert.True(PathComponent.IsValid(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData(" a")]
    [InlineData("a ")]
    [InlineData("a\nb")]
    [InlineData("a\0b")]
    public void An_invalid_component_is_rejected(string? value)
    {
        var result = PathComponent.Validate(value);

        Assert.True(result.IsFailure);
        Assert.Equal(CoreErrorCodes.PathsInvalidComponent, result.Error.Code);
        Assert.False(PathComponent.IsValid(value));
    }

    [Fact]
    public void A_component_longer_than_NAME_MAX_is_rejected()
    {
        Assert.True(PathComponent.IsValid(new string('a', PathComponent.MaxBytes)));
        Assert.False(PathComponent.IsValid(new string('a', PathComponent.MaxBytes + 1)));
    }

    [Fact]
    public void A_multibyte_component_is_measured_in_utf8_bytes()
    {
        // 128 'é' is 256 UTF-8 bytes: over NAME_MAX even though the string is
        // shorter than 255 characters.
        Assert.False(PathComponent.IsValid(new string('é', 128)));
    }

    [Fact]
    public void A_rejected_message_does_not_echo_a_control_character()
    {
        var result = PathComponent.Validate("bad\nname");

        Assert.True(result.IsFailure);
        Assert.False(result.Error.Message.Contains('\n'));
    }
}
