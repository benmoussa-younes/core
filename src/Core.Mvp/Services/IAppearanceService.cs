// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>
/// The application's appearance preferences: it owns the current choices, applies
/// them to the running application and persists them when the operator asked to
/// remember.
/// </summary>
public interface IAppearanceService : IService
{
    /// <summary>Gets the current settings.</summary>
    AppearanceSettings Current { get; }

    /// <summary>Chooses a theme variant and applies it at once.</summary>
    /// <param name="theme">The chosen variant.</param>
    void ChooseTheme(ThemePreference theme);

    /// <summary>Chooses an accent palette key and applies it at once.</summary>
    /// <param name="key">The accent palette key.</param>
    void ChooseAccent(string key);

    /// <summary>Sets whether the choices are persisted, saving or clearing the store.</summary>
    /// <param name="remember">Whether to remember the choices.</param>
    void SetRemember(bool remember);

    /// <summary>Applies the current settings to the running application.</summary>
    /// <remarks>
    /// The service loads the stored settings in its constructor but does not
    /// apply them; a product calls this once after <c>Application.Current</c>
    /// exists, before showing its first window.
    /// </remarks>
    void Apply();
}
