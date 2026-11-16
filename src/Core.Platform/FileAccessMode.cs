// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform;

/// <summary>
/// The access mode probed by <see cref="IFileSystem.TryAccess"/>.
/// </summary>
/// <remarks>
/// The values match the POSIX <c>F_OK</c>/<c>R_OK</c>/<c>W_OK</c>/<c>X_OK</c>
/// constants; <see cref="Exists"/> is zero so a caller can test only existence.
/// </remarks>
public enum FileAccessMode
{
    /// <summary>The path exists.</summary>
    Exists = 0,

    /// <summary>The path is executable.</summary>
    Execute = 1,

    /// <summary>The path is writable.</summary>
    Write = 2,

    /// <summary>The path is readable.</summary>
    Read = 4,
}
