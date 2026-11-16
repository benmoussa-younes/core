// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Media;
using Core.UI.Adwaita;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class AccentPaletteTests
{
    [Fact]
    public void The_palette_is_ordered_and_first_is_the_default()
    {
        Assert.Equal(6, AccentPalette.Catalog.Count);
        Assert.Equal("AccentEmerald", AccentPalette.Catalog[0].Key);
        Assert.Equal("Emerald", AccentPalette.Catalog[0].Label);
    }

    [Fact]
    public void It_finds_a_key_and_rejects_an_unknown_one()
    {
        var found = AccentPalette.Find("AccentCyan");

        Assert.NotNull(found);
        Assert.Equal("Cyan", found.Label);
        Assert.Null(AccentPalette.Find("AccentNope"));
        Assert.Throws<ArgumentException>(() => AccentPalette.Find(string.Empty));
    }

    [Fact]
    public async Task An_entry_resolves_its_colour_from_the_resources()
    {
        var colour = await Headless.Dispatch(() => AccentPalette.Find("AccentBlue")!.Color);

        Assert.Equal(Color.Parse("#3B82F6"), colour);
    }
}
