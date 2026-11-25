// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform.Linux;

/// <summary>
/// The window size of a terminal, in character cells and pixels.
/// </summary>
/// <param name="Rows">The number of rows.</param>
/// <param name="Columns">The number of columns.</param>
/// <param name="XPixels">The width in pixels; 0 when the terminal does not report it.</param>
/// <param name="YPixels">The height in pixels; 0 when the terminal does not report it.</param>
public readonly record struct UnixTerminalSize(
    ushort Rows,
    ushort Columns,
    ushort XPixels = 0,
    ushort YPixels = 0);
