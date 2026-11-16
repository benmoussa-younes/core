// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Media;
using Core.UI.Adwaita;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class AdwaitaButtonTests
{
    [Fact]
    public void It_is_flat_and_square()
    {
        var button = new AdwaitaButton();

        Assert.Equal(GnomeMetrics.ButtonSize, button.MinWidth);
        Assert.Equal(GnomeMetrics.ButtonSize, button.MinHeight);
        Assert.Equal(Brushes.Transparent, button.Background);
        Assert.Equal(new Thickness(0), button.BorderThickness);
        Assert.Equal(new CornerRadius(GnomeMetrics.ButtonCornerRadius), button.CornerRadius);
    }

    [Fact]
    public async Task It_uses_the_shared_foreground_token()
    {
        var (foreground, token) = await Headless.Dispatch(() =>
        {
            var button = new AdwaitaButton();
            return (button.Foreground, ThemeBrushes.Resolve("ForegroundBrush"));
        });

        Assert.Same(token, foreground);
    }
}
