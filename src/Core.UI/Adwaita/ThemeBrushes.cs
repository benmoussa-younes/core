// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Core.UI.Adwaita;

/// <summary>
/// Resolves a semantic brush from the application resources for the controls the
/// shell composes in code, so they follow the shared theme instead of a literal
/// colour.
/// </summary>
/// <remarks>
/// The lookup is variant-aware: it resolves against the application's current
/// <see cref="ThemeVariant"/>, so calling it again after a variant change returns
/// the new value. A missing key is a programmer error (a typo or an application
/// that did not include <c>Themes/Tokens.axaml</c>) and throws instead of hiding
/// the control behind a transparent brush.
/// </remarks>
internal static class ThemeBrushes
{
    internal static IBrush Resolve(string key)
    {
        return Resolve(key, Application.Current?.ActualThemeVariant ?? ThemeVariant.Default);
    }

    internal static IBrush Resolve(string key, ThemeVariant? variant)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (Application.Current is not { } application)
        {
            // No UI application in scope (a pure unit context): there is no theme to
            // resolve against, and the control is never rendered.
            return Brushes.Transparent;
        }

        if (application.TryFindResource(key, variant, out var value) && value is IBrush brush)
        {
            return brush;
        }

        throw new KeyNotFoundException(
            $"The theme key '{key}' is not defined. Include the shared Themes/Tokens.axaml and name an existing token.");
    }
}
