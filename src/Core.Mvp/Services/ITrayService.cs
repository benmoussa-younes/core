// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Presentation;

namespace Core.Mvp.Services;

/// <summary>
/// The system-tray presence of the application: an icon, a tooltip and a menu
/// built from the same command model as the chrome, so a tray entry and a menu
/// entry cannot drift.
/// </summary>
public interface ITrayService
{
    /// <summary>Shows the tray icon, or updates it when it is already visible.</summary>
    /// <param name="toolTip">The tooltip.</param>
    /// <param name="menu">The tray menu, grouped like the toolbar.</param>
    void Show(string toolTip, CommandSet menu);

    /// <summary>Hides the tray icon.</summary>
    void Hide();
}
