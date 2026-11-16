// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>The operator's appearance choice for the application theme.</summary>
public enum ThemePreference
{
    /// <summary>Follow the desktop's own light or dark variant.</summary>
    System = 0,

    /// <summary>Force the light variant.</summary>
    Light = 1,

    /// <summary>Force the dark variant.</summary>
    Dark = 2,
}
