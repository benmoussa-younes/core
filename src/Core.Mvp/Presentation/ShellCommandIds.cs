// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Presentation;

/// <summary>
/// The reserved command identifiers the shell handles itself. A product never
/// declares them: the shell appends them to the primary menu and routes them.
/// </summary>
public static class ShellCommandIds
{
    /// <summary>Opens the application's About dialog.</summary>
    public const string About = "core.about";

    /// <summary>Opens the application's Preferences dialog.</summary>
    public const string Preferences = "core.preferences";

    /// <summary>Quits the application.</summary>
    public const string Quit = "core.quit";
}
