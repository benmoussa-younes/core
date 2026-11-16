// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.UI.Adwaita;

/// <summary>
/// The studio accent palette: the ordered choices a Preferences page offers and
/// <see cref="Hosting.CoreApplication.ApplyAccent(string)"/> applies. The
/// colours themselves live in <c>Themes/Accents.axaml</c>; this only names them.
/// </summary>
public static class AccentPalette
{
    /// <summary>Gets the palette in display order; the first entry is the default.</summary>
    public static IReadOnlyList<AccentDefinition> Catalog { get; } =
    [
        new("AccentEmerald", "Emerald"),
        new("AccentBlue", "Blue"),
        new("AccentViolet", "Violet"),
        new("AccentAmber", "Amber"),
        new("AccentRose", "Rose"),
        new("AccentCyan", "Cyan"),
    ];

    /// <summary>Finds a palette entry by its token key.</summary>
    /// <param name="key">The token key.</param>
    /// <returns>The entry, or <see langword="null"/> when the key is not in the palette.</returns>
    public static AccentDefinition? Find(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        foreach (var definition in Catalog)
        {
            if (string.Equals(definition.Key, key, StringComparison.Ordinal))
            {
                return definition;
            }
        }

        return null;
    }
}
