// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>The operator's answer to the quit prompt.</summary>
public enum QuitChoice
{
    /// <summary>Quit the application.</summary>
    Quit,

    /// <summary>Keep running.</summary>
    Cancel,
}
