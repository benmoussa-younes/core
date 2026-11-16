// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Models;

/// <summary>
/// A window's saved placement: its size, its screen position and whether it was
/// maximised when it closed.
/// </summary>
/// <param name="X">The left position in screen pixels.</param>
/// <param name="Y">The top position in screen pixels.</param>
/// <param name="Width">The width in device-independent pixels.</param>
/// <param name="Height">The height in device-independent pixels.</param>
/// <param name="Maximized">Whether the window was maximised.</param>
public sealed record WindowPlacement(double X, double Y, double Width, double Height, bool Maximized = false);
