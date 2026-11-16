// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.UI.Hosting;

namespace Core.UI.Tests;

public sealed class PlatformSelectionTests
{
    [Theory]
    [InlineData(false, "wayland-0", false)]
    [InlineData(true, null, false)]
    [InlineData(true, "", false)]
    [InlineData(true, "wayland-0", true)]
    public void Anything_but_a_linux_wayland_session_selects_x11(bool isLinux, string? waylandDisplay, bool waylandDisabled)
    {
        Assert.Equal(GraphicsBackend.X11, PlatformSelection.Select(isLinux, waylandDisplay, waylandDisabled));
    }

    [Fact]
    public void A_linux_wayland_session_selects_wayland()
    {
        Assert.Equal(GraphicsBackend.Wayland, PlatformSelection.Select(true, "wayland-0", false));
    }

    [Fact]
    public void From_environment_returns_a_defined_backend()
    {
        Assert.True(Enum.IsDefined(PlatformSelection.FromEnvironment()));
    }
}
