// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Media;

namespace Core.UI.Adwaita;

/// <summary>
/// One choice in the studio accent palette: the token key the application applies
/// and the label a Preferences page shows.
/// </summary>
/// <param name="Key">The token key in <c>Themes/Accents.axaml</c> (for example <c>AccentEmerald</c>).</param>
/// <param name="Label">The human-readable label (for example <c>Emerald</c>).</param>
public sealed record AccentDefinition(string Key, string Label)
{
    /// <summary>Gets the accent colour from the application resources.</summary>
    /// <exception cref="KeyNotFoundException">The application did not merge <c>Themes/Accents.axaml</c>.</exception>
    public Color Color => ThemeResources.ResolveColor(Key);
}
