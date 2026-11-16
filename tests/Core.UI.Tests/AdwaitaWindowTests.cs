// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Controls;
using Core.UI.Chrome;
using Core.UI.Adwaita;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class AdwaitaWindowTests
{
    [Fact]
    public async Task It_has_the_shared_background_token()
    {
        var (background, token) = await Headless.Dispatch(() =>
        {
            var window = new AdwaitaWindow();
            return (window.Background, ThemeBrushes.Resolve("BackgroundBrush"));
        });

        Assert.Same(token, background);
    }

    [Fact]
    public async Task Client_side_decorations_remove_the_system_decorations()
    {
        var result = await Headless.Dispatch(() =>
        {
            var window = new AdwaitaWindow();

            window.ApplyDecorations(true);

            return (window.WindowDecorations, window.ExtendClientAreaToDecorationsHint);
        });

        Assert.Equal(WindowDecorations.None, result.WindowDecorations);
        Assert.True(result.ExtendClientAreaToDecorationsHint);
    }

    [Fact]
    public async Task Server_side_decorations_restore_the_system_decorations()
    {
        var result = await Headless.Dispatch(() =>
        {
            var window = new AdwaitaWindow();

            window.ApplyDecorations(false);

            return (window.ExtendClientAreaToDecorationsHint, window.WindowDecorations);
        });

        Assert.False(result.ExtendClientAreaToDecorationsHint);
        Assert.Equal(WindowDecorations.Full, result.WindowDecorations);
    }

    [Fact]
    public async Task Turning_decorations_off_reapplies_them()
    {
        var hint = await Headless.Dispatch(() =>
        {
            var window = new AdwaitaWindow { ClientSideDecorations = false };

            return window.ExtendClientAreaToDecorationsHint;
        });

        Assert.False(hint);
    }
}
