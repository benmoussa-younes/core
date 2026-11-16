// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Security;

namespace Core.Tests;

public sealed class SecretTextTests
{
    [Fact]
    public void A_null_or_empty_value_is_the_empty_secret()
    {
        Assert.True(SecretText.From(null).IsEmpty);
        Assert.True(SecretText.From(string.Empty).IsEmpty);
        Assert.Same(SecretText.Empty, SecretText.From(null));
        Assert.Equal("(none)", SecretText.Empty.ToString());
    }

    [Fact]
    public void The_raw_value_has_one_exit_and_text_is_redacted()
    {
        var secret = SecretText.From("hunter2");

        Assert.False(secret.IsEmpty);
        Assert.Equal("hunter2", secret.Reveal());
        Assert.Equal("[redacted]", secret.ToString());
        Assert.Equal("[redacted]", $"{secret}");
        Assert.DoesNotContain("hunter2", secret.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Equality_compares_the_raw_values_and_a_hash_matches()
    {
        var secret = SecretText.From("value");

        Assert.True(secret.Equals(SecretText.From("value")));
        Assert.False(secret.Equals(SecretText.From("Value")));
        Assert.False(secret.Equals(SecretText.From("value-longer")));
        Assert.False(secret.Equals((SecretText?)null));
        Assert.False(secret.Equals((object?)null));
        Assert.False(secret.Equals("value"));
        Assert.Equal(secret.GetHashCode(), SecretText.From("value").GetHashCode());
    }
}
