// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Paths;

namespace Core.Tests;

public sealed class SingleInstanceSocketNameTests
{
    [Fact]
    public void A_name_without_the_user_is_the_vendor_and_the_application()
    {
        Assert.Equal(
            "benmoussa-vpn-manager.socket",
            SingleInstanceSocketName.For("benmoussa", "vpn-manager"));
    }

    [Fact]
    public void A_name_with_the_user_keeps_the_released_suffix()
    {
        Assert.Equal(
            "benmoussa-vpn-manager-alice.sock",
            SingleInstanceSocketName.For("benmoussa", "vpn-manager", "alice"));
    }

    [Theory]
    [InlineData("", "vpn-manager")]
    [InlineData("a/b", "vpn-manager")]
    [InlineData("benmoussa", "")]
    [InlineData("benmoussa", "a\\b")]
    public void An_invalid_part_is_refused(string vendor, string application)
    {
        Assert.Throws<ArgumentException>(() => SingleInstanceSocketName.For(vendor, application));
    }
}
