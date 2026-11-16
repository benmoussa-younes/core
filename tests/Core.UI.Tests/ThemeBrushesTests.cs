// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.UI.Adwaita;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class ThemeBrushesTests
{
    [Fact]
    public async Task It_resolves_a_shared_token()
    {
        var background = await Headless.Dispatch(() => ThemeBrushes.Resolve("BackgroundBrush"));

        Assert.IsType<global::Avalonia.Media.SolidColorBrush>(background);
    }

    [Fact]
    public async Task A_missing_key_fails_loudly()
    {
        await Headless.Dispatch(() =>
        {
            Assert.Throws<KeyNotFoundException>(() => ThemeBrushes.Resolve("NotAToken"));
        });
    }

    [Fact]
    public async Task An_empty_key_is_rejected()
    {
        await Headless.Dispatch(() =>
        {
            Assert.Throws<ArgumentException>(() => ThemeBrushes.Resolve(string.Empty));
        });
    }

    [Fact]
    public async Task A_missing_icon_resolves_to_null()
    {
        var icon = await Headless.Dispatch(() => ThemeIcons.Resolve("NotAGlyph"));

        Assert.Null(icon);
    }
}
