// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Core.UI.Adwaita;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class ThemeResourceTests
{
    [Fact]
    public async Task It_resolves_the_typed_tokens()
    {
        var (brush, colour, size, corner, font) = await Headless.Dispatch(() => (
            ThemeResources.ResolveBrush("BackgroundBrush"),
            ThemeResources.ResolveColor("AccentEmerald"),
            ThemeResources.ResolveDouble("FontSize"),
            ThemeResources.ResolveCornerRadius("SurfaceCornerRadius"),
            ThemeResources.ResolveFontFamily("MonospaceFont")));

        Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        Assert.Equal(Color.Parse("#10B981"), colour);
        Assert.Equal(13, size);
        Assert.Equal(new CornerRadius(10), corner);
        Assert.NotNull(font);
    }

    [Fact]
    public async Task A_missing_token_fails_loudly()
    {
        await Headless.Dispatch(() =>
        {
            Assert.Throws<KeyNotFoundException>(() => ThemeResources.ResolveColor("NotAToken"));
            Assert.Throws<KeyNotFoundException>(() => ThemeResources.ResolveDouble("NotAToken"));
            Assert.Throws<KeyNotFoundException>(() => ThemeResources.ResolveCornerRadius("NotAToken"));
            Assert.Throws<KeyNotFoundException>(() => ThemeResources.ResolveFontFamily("NotAToken"));
        });
    }

    [Fact]
    public async Task An_empty_token_is_rejected()
    {
        await Headless.Dispatch(() =>
        {
            Assert.Throws<ArgumentException>(() => ThemeResources.ResolveBrush(string.Empty));
            Assert.Throws<ArgumentException>(() => ThemeResources.ResolveColor(string.Empty));
            Assert.Throws<ArgumentException>(() => ThemeResources.ResolveDouble(string.Empty));
            Assert.Throws<ArgumentException>(() => ThemeResources.ResolveCornerRadius(string.Empty));
            Assert.Throws<ArgumentException>(() => ThemeResources.ResolveFontFamily(string.Empty));
        });
    }

    [Fact]
    public void Binding_rejects_a_null_target_property_or_empty_key()
    {
        var border = new Border();

        Assert.Throws<ArgumentNullException>(() => ThemeResources.Bind(null!, Border.BackgroundProperty, "BackgroundBrush"));
        Assert.Throws<ArgumentNullException>(() => ThemeResources.Bind(border, null!, "BackgroundBrush"));
        Assert.Throws<ArgumentException>(() => ThemeResources.Bind(border, Border.BackgroundProperty, string.Empty));
    }

    [Fact]
    public async Task A_bound_property_follows_the_token()
    {
        var bound = await Headless.Dispatch(() =>
        {
            var border = new Border();
            ThemeResources.Bind(border, Border.BackgroundProperty, "BackgroundBrush");

            var window = new Window { Width = 200, Height = 100, Content = border };
            window.Show();
            window.UpdateLayout();
            var colour = Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color;
            window.Close();

            return colour;
        });

        var expected = await Headless.Dispatch(
            () => Assert.IsAssignableFrom<ISolidColorBrush>(ThemeResources.ResolveBrush("BackgroundBrush")).Color);

        Assert.Equal(expected, bound);
    }
}
