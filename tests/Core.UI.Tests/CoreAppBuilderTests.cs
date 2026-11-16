// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Core.UI.Hosting;

namespace Core.UI.Tests;

public sealed class CoreAppBuilderTests
{
    [Fact]
    public void The_x11_backend_builds_a_builder()
    {
        Assert.NotNull(CoreAppBuilder.Configure<TestApp>(GraphicsBackend.X11));
    }

    [Fact]
    public void The_wayland_backend_builds_a_builder()
    {
        Assert.NotNull(CoreAppBuilder.Configure<TestApp>(GraphicsBackend.Wayland));
    }

    [Fact]
    public void The_environment_backend_builds_a_builder()
    {
        Assert.NotNull(CoreAppBuilder.Configure<TestApp>());
    }

    private sealed class TestApp : Application
    {
        public override void Initialize()
        {
        }
    }
}
