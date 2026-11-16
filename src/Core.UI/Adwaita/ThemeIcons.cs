// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Core.UI.Adwaita;

/// <summary>
/// Resolves a symbolic glyph from the application resources for the shell's
/// toolbar, so a command names an icon by key and the theme owns the geometry.
/// </summary>
/// <remarks>
/// An icon is optional cosmetics, so a missing key resolves to <see langword="null"/>
/// and the caller falls back to a text label instead of failing the window.
/// </remarks>
internal static class ThemeIcons
{
    internal static Geometry? Resolve(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        return Application.Current is { } application
            && application.TryFindResource(key, out var value)
            && value is Geometry geometry
            ? geometry
            : null;
    }
}
