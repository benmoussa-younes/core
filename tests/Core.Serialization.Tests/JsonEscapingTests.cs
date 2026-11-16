// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Codecs.Json;

namespace Core.Serialization.Tests;

/// <summary>
/// The encoder contract itself, not only the text it produces through the writer:
/// the writer sizes its scratch buffer from <c>MaxOutputCharactersPerInputCharacter</c>
/// and copies the untouched prefix from <c>FindFirstCharacterToEncode</c>, so a
/// buffer two characters short, an escaped scalar and an unescaped one each have a
/// defined answer.
/// </summary>
public sealed unsafe class JsonEscapingTests
{
    [Theory]
    [InlineData('"', true)]
    [InlineData('\\', true)]
    [InlineData('\t', true)]
    [InlineData('\u007f', true)]
    [InlineData('\u2028', true)]
    [InlineData('\u2029', true)]
    [InlineData('a', false)]
    [InlineData('\u00e9', false)]
    public void The_raw_encoder_escapes_only_the_json_metacharacters_and_raw_utf8(char current, bool escaped)
    {
        Assert.Equal(escaped, JsonEscaping.Utf8Raw.WillEncode(current));
    }

    [Fact]
    public void The_raw_encoder_never_writes_more_than_its_claimed_width()
    {
        var buffer = stackalloc char[6];

        Assert.Equal(6, JsonEscaping.Utf8Raw.MaxOutputCharactersPerInputCharacter);
        Assert.True(JsonEscaping.Utf8Raw.TryEncodeUnicodeScalar('\u2028', buffer, 6, out var width));
        Assert.Equal(6, width);
        Assert.Equal("\\u2028", new string(buffer, 0, width));
    }

    [Fact]
    public void A_buffer_one_char_short_is_refused_instead_of_truncated()
    {
        var buffer = stackalloc char[5];
        Assert.False(JsonEscaping.Utf8Raw.TryEncodeUnicodeScalar('\u2028', buffer, 5, out var wide));
        Assert.Equal(0, wide);

        var narrow = stackalloc char[1];
        Assert.False(JsonEscaping.Utf8Raw.TryEncodeUnicodeScalar('"', narrow, 1, out var shortForm));
        Assert.Equal(0, shortForm);
    }

    [Fact]
    public void A_scalar_the_encoder_leaves_literal_is_refused_by_try_encode()
    {
        // WillEncode is false for it, so the writer transcodes it rather than
        // asking for an escape; a caller that asks anyway gets a refusal, not a
        // silently wrong character.
        var buffer = stackalloc char[6];

        Assert.False(JsonEscaping.Utf8Raw.TryEncodeUnicodeScalar('a', buffer, 6, out var written));
        Assert.Equal(0, written);
    }

    [Fact]
    public void FindFirstCharacterToEncode_reports_the_first_metacharacter()
    {
        const string text = "ab\"cd";

        fixed (char* pointer = text)
        {
            Assert.Equal(2, JsonEscaping.Utf8Raw.FindFirstCharacterToEncode(pointer, text.Length));
        }

        fixed (char* pointer = "plain")
        {
            Assert.Equal(-1, JsonEscaping.Utf8Raw.FindFirstCharacterToEncode(pointer, 5));
        }
    }
}
