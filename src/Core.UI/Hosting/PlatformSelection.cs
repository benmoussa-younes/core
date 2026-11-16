// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.UI.Hosting;

/// <summary>
/// Chooses the graphics backend: Win32 on Windows, macOS on macOS, native Wayland
/// on a Wayland session, X11 (through XWayland) everywhere else.
/// </summary>
public static class PlatformSelection
{
    /// <summary>The environment variable that names the Wayland display.</summary>
    public const string WaylandDisplayVariable = "WAYLAND_DISPLAY";

    /// <summary>The environment variable that forces the X11 backend when it is set.</summary>
    public const string DisableWaylandVariable = "CORE_DISABLE_WAYLAND";

    /// <summary>
    /// Selects the backend from the current process environment.
    /// </summary>
    /// <returns>The selected backend.</returns>
    public static GraphicsBackend FromEnvironment()
    {
        if (OperatingSystem.IsWindows())
        {
            return GraphicsBackend.Win32;
        }

        if (OperatingSystem.IsMacOS())
        {
            return GraphicsBackend.MacOS;
        }

        return Select(
            OperatingSystem.IsLinux(),
            Environment.GetEnvironmentVariable(WaylandDisplayVariable),
            Environment.GetEnvironmentVariable(DisableWaylandVariable) is not null);
    }

    /// <summary>
    /// Selects the backend from explicit values.
    /// </summary>
    /// <param name="isLinux">Whether the current platform is Linux.</param>
    /// <param name="waylandDisplay">The <c>$WAYLAND_DISPLAY</c> value, or <see langword="null"/> when unset.</param>
    /// <param name="waylandDisabled">Whether Wayland is explicitly disabled.</param>
    /// <returns>The selected backend.</returns>
    public static GraphicsBackend Select(bool isLinux, string? waylandDisplay, bool waylandDisabled)
    {
        if (!isLinux || waylandDisabled || string.IsNullOrEmpty(waylandDisplay))
        {
            return GraphicsBackend.X11;
        }

        return GraphicsBackend.Wayland;
    }
}
