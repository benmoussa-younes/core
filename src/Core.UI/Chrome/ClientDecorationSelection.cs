// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.UI.Hosting;
namespace Core.UI.Chrome;

/// <summary>
/// Decides whether a window uses client-side decorations.
/// </summary>
/// <remarks>
/// Client-side decorations are only drawn on a native Wayland session, where
/// they are the platform convention, or when explicitly forced for testing. On
/// X11 the window manager keeps its server-side decorations, so the header bar
/// still renders inside the client area and the window stays movable.
/// <para>
/// Avalonia's drawn decorations for X11 (<c>AVALONIA_X11_CSD</c> /
/// <c>EnableDrawnDecorations</c>) are experimental; this library deliberately
/// does not opt into them, so an X11 session always keeps the window manager's
/// decorations.
/// </para>
/// </remarks>
public static class ClientDecorationSelection
{
    /// <summary>The environment variable that forces client-side decorations when it is set.</summary>
    public const string ForceVariable = "CORE_FORCE_CSD";

    /// <summary>
    /// Decides whether to use client-side decorations from explicit values.
    /// </summary>
    /// <param name="requested">Whether the window asked for client-side decorations.</param>
    /// <param name="backend">The selected graphics backend.</param>
    /// <param name="force">Whether client-side decorations are forced.</param>
    /// <returns><see langword="true"/> when client-side decorations should be used.</returns>
    public static bool ShouldUseClientSideDecorations(bool requested, GraphicsBackend backend, bool force)
    {
        return requested && (backend == GraphicsBackend.Wayland || force);
    }

    /// <summary>
    /// Decides whether to use client-side decorations from the current process
    /// environment.
    /// </summary>
    /// <param name="requested">Whether the window asked for client-side decorations.</param>
    /// <param name="backend">The selected graphics backend.</param>
    /// <returns><see langword="true"/> when client-side decorations should be used.</returns>
    public static bool FromEnvironment(bool requested, GraphicsBackend backend)
    {
        return ShouldUseClientSideDecorations(
            requested,
            backend,
            Environment.GetEnvironmentVariable(ForceVariable) is not null);
    }
}
