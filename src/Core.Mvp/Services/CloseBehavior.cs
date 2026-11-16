// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>
/// What closing the main window does.
/// </summary>
public enum CloseBehavior
{
    /// <summary>Closing the window quits the application.</summary>
    Quit,

    /// <summary>Closing the window hides it and the application keeps running in the tray.</summary>
    HideToTray,
}
