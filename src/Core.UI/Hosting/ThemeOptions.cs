// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Media;

namespace Core.UI.Hosting;

/// <summary>
/// The supported per-application typography overrides: a product supplies its own
/// fonts or base size without shadowing the merged token dictionary by merge order.
/// Every member is optional; a <see langword="null"/> member keeps the shared
/// default.
/// </summary>
/// <remarks>
/// The application applies the overrides after its resources are loaded (see
/// <see cref="CoreApplication"/>), so a product only derives and returns this
/// object; it never has to know whether its <c>App.axaml</c> is merged before or
/// after the base.
/// </remarks>
public sealed record ThemeOptions
{
    /// <summary>Gets the UI font family, or <see langword="null"/> for the shared default.</summary>
    public FontFamily? UiFont { get; init; }

    /// <summary>Gets the monospace font family, or <see langword="null"/> for the shared default.</summary>
    public FontFamily? MonospaceFont { get; init; }

    /// <summary>Gets the base UI font size, or <see langword="null"/> for the shared default.</summary>
    public double? BaseFontSize { get; init; }
}
