// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>
/// The persisted appearance preferences: the theme variant, an optional accent
/// palette key, and whether the choices are remembered across restarts.
/// </summary>
/// <param name="Theme">The theme variant choice.</param>
/// <param name="AccentKey">The accent palette key, or <see langword="null"/> to keep the theme's default accent.</param>
/// <param name="Remember">Whether the choices are persisted.</param>
public sealed record AppearanceSettings(
    ThemePreference Theme = ThemePreference.System,
    string? AccentKey = null,
    bool Remember = false)
{
    /// <summary>Gets the settings a fresh application starts with.</summary>
    public static AppearanceSettings Default { get; } = new();
}
