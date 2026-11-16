// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later


namespace Core.Tests;

public sealed class CapabilityContractTests
{
    [Fact]
    public async Task Capability_returns_a_successful_result()
    {
        var capability = new EchoCapability();

        var result = await capability.InvokeAsync("hello", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }
}
