// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Security;

namespace Core.Tests;

public sealed class SecretRedactionTests
{
    [Theory]
    [InlineData("password=hunter2", "password=[redacted]")]
    [InlineData("token: abc123", "token=[redacted]")]
    [InlineData("Authorization=Bearer xyz", "Authorization=[redacted]")]
    [InlineData("api_key = 0123456789", "api_key=[redacted]")]
    public void Key_value_secrets_are_redacted(string value, string expected)
    {
        Assert.Equal(expected, SecretRedaction.Scrub(value));
    }

    [Fact]
    public void Uri_user_info_is_redacted()
    {
        Assert.Equal(
            "https://[redacted]@example.org/path",
            SecretRedaction.Scrub("https://user:pass@example.org/path"));
    }

    [Fact]
    public void Plain_text_is_unchanged()
    {
        Assert.Equal("the operation completed", SecretRedaction.Scrub("the operation completed"));
    }
}
