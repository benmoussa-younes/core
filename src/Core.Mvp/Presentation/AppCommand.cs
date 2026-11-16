// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Presentation;

/// <summary>
/// One command the shell can render in its contextual toolbar, its menus or a
/// context menu. The presenter publishes commands; the shell renders them and
/// raises the id and the active view back through <c>IViewHost.CommandInvoked</c>.
/// </summary>
/// <param name="Id">The stable identifier the activation carries back.</param>
/// <param name="Label">The visible label.</param>
/// <param name="IconKey">The symbolic icon key, or <see langword="null"/> for a text-only command.</param>
/// <param name="ToolTip">The tooltip, or <see langword="null"/>.</param>
/// <param name="Shortcut">The keyboard shortcut shown next to a menu item, or <see langword="null"/>.</param>
/// <param name="IsEnabled">Whether the command can be activated right now.</param>
/// <param name="IsChecked">Whether the command is a toggled-on state (a toggle button or a checked menu item).</param>
/// <param name="Group">The separator group the command belongs to; commands of different groups are separated.</param>
public sealed record AppCommand(
    string Id,
    string Label,
    string? IconKey = null,
    string? ToolTip = null,
    string? Shortcut = null,
    bool IsEnabled = true,
    bool IsChecked = false,
    string Group = "");
