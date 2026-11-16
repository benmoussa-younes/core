// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>
/// The persistence seam for <see cref="AppearanceSettings"/>: a load may find no
/// saved settings, a save replaces the previous settings, and a clear forgets
/// them.
/// </summary>
public interface IAppearanceStore
{
    /// <summary>Loads the saved settings.</summary>
    /// <returns>The settings, or <see langword="null"/> when none are saved or the file is unreadable.</returns>
    AppearanceSettings? Load();

    /// <summary>Saves the settings, replacing any previous ones.</summary>
    /// <param name="settings">The settings to save.</param>
    void Save(AppearanceSettings settings);

    /// <summary>Forgets the saved settings.</summary>
    void Clear();
}
