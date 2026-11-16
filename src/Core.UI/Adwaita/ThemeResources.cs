// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace Core.UI.Adwaita;

/// <summary>
/// Resolves the shared Core theme tokens for a control composed in code, and
/// binds a property to a token so the control follows a live light/dark or accent
/// switch.
/// </summary>
/// <remarks>
/// This is the public face of the shell's variant-aware resolution. A brush uses
/// <see cref="ResolveBrush"/>, a font, a size or a corner uses the typed resolver,
/// and <see cref="Bind"/> installs a dynamic resource. A missing token is a
/// programmer error and throws, so an application that forgot to merge
/// <c>Themes/Tokens.axaml</c> fails loudly instead of rendering transparent.
/// </remarks>
public static class ThemeResources
{
    /// <summary>Resolves a semantic brush token for the active theme variant.</summary>
    /// <param name="key">The token key (for example <c>SurfaceBrush</c>).</param>
    /// <returns>The brush.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
    /// <exception cref="KeyNotFoundException">The token is not defined.</exception>
    public static IBrush ResolveBrush(string key) => ThemeBrushes.Resolve(key);

    /// <summary>Resolves a colour token for the active theme variant.</summary>
    /// <param name="key">The token key.</param>
    /// <returns>The colour.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
    /// <exception cref="KeyNotFoundException">The token is not defined.</exception>
    public static Color ResolveColor(string key) => Resolve<Color>(key);

    /// <summary>Resolves a numeric token (a size) for the active theme variant.</summary>
    /// <param name="key">The token key (for example <c>FontSize</c>).</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
    /// <exception cref="KeyNotFoundException">The token is not defined.</exception>
    public static double ResolveDouble(string key) => Resolve<double>(key);

    /// <summary>Resolves a corner-radius token.</summary>
    /// <param name="key">The token key (for example <c>SurfaceCornerRadius</c>).</param>
    /// <returns>The corner radius.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
    /// <exception cref="KeyNotFoundException">The token is not defined.</exception>
    public static CornerRadius ResolveCornerRadius(string key) => Resolve<CornerRadius>(key);

    /// <summary>Resolves a font-family token.</summary>
    /// <param name="key">The token key (for example <c>MonospaceFont</c>).</param>
    /// <returns>The font family.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
    /// <exception cref="KeyNotFoundException">The token is not defined.</exception>
    public static FontFamily ResolveFontFamily(string key) => Resolve<FontFamily>(key);

    /// <summary>
    /// Binds a property to a theme token as a dynamic resource, so a control built
    /// in code follows a live theme switch instead of keeping the value it was
    /// created with.
    /// </summary>
    /// <param name="target">The object whose property is bound.</param>
    /// <param name="property">The property to bind.</param>
    /// <param name="key">The token key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
    public static void Bind(AvaloniaObject target, AvaloniaProperty property, string key)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentException.ThrowIfNullOrEmpty(key);

        target.Bind(property, new DynamicResourceExtension(key));
    }

    private static T Resolve<T>(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (Application.Current is { } application
            && application.TryFindResource(key, application.ActualThemeVariant, out var value))
        {
            if (value is T typed)
            {
                return typed;
            }
        }

        throw new KeyNotFoundException(
            $"The theme key '{key}' is not defined. Include the shared Themes/Tokens.axaml and name an existing token.");
    }
}
