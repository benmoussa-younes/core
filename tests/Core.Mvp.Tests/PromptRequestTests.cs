// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Services;

namespace Core.Mvp.Tests;

public sealed class PromptRequestTests
{
    [Fact]
    public void A_password_value_is_redacted_from_the_diagnostic_form()
    {
        var request = new PromptRequest("Secret", "Token", "hunter2", IsPassword: true);

        var text = request.ToString();

        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_value_stays_visible()
    {
        var request = new PromptRequest("Rename", "Name", "old");

        Assert.Contains("old", request.ToString(), StringComparison.Ordinal);
    }
}
