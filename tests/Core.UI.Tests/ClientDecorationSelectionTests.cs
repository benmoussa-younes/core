// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.UI.Chrome;
using Core.UI.Hosting;

namespace Core.UI.Tests;

public sealed class ClientDecorationSelectionTests
{
    [Fact]
    public void Nothing_is_forced_when_the_window_did_not_ask()
    {
        Assert.False(ClientDecorationSelection.ShouldUseClientSideDecorations(
            requested: false, GraphicsBackend.Wayland, force: true));
    }

    [Fact]
    public void A_wayland_session_uses_client_side_decorations()
    {
        Assert.True(ClientDecorationSelection.ShouldUseClientSideDecorations(
            requested: true, GraphicsBackend.Wayland, force: false));
    }

    [Fact]
    public void X11_keeps_the_window_manager_decorations_by_default()
    {
        Assert.False(ClientDecorationSelection.ShouldUseClientSideDecorations(
            requested: true, GraphicsBackend.X11, force: false));
    }

    [Fact]
    public void X11_can_force_client_side_decorations()
    {
        Assert.True(ClientDecorationSelection.ShouldUseClientSideDecorations(
            requested: true, GraphicsBackend.X11, force: true));
    }

    [Fact]
    public void From_environment_returns_a_bool()
    {
        Assert.False(ClientDecorationSelection.FromEnvironment(requested: false, GraphicsBackend.Wayland));
    }
}
