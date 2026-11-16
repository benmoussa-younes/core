// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.UI.Hosting;

/// <summary>
/// The graphics backend the application builder selects.
/// </summary>
public enum GraphicsBackend
{
    /// <summary>
    /// The X11 backend, which also serves Wayland sessions through XWayland.
    /// </summary>
    X11,

    /// <summary>
    /// The native Wayland backend, which talks to the compositor directly.
    /// </summary>
    Wayland,

    /// <summary>
    /// The Windows backend, selected automatically on Windows.
    /// </summary>
    Win32,

    /// <summary>
    /// The macOS backend, selected automatically on macOS.
    /// </summary>
    MacOS,
}
