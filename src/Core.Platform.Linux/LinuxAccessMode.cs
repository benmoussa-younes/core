// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform.Linux;

/// <summary>
/// The access mode probed by the Linux file-system adapter.
/// </summary>
/// <remarks>
/// The values are the POSIX <c>R_OK</c>/<c>W_OK</c>/<c>X_OK</c>/<c>F_OK</c>
/// constants, so they pass to <c>faccessat</c> unchanged.
/// </remarks>
public enum LinuxAccessMode
{
    /// <summary>The path exists (<c>F_OK</c>).</summary>
    Exists = 0,

    /// <summary>The path is executable (<c>X_OK</c>).</summary>
    Execute = 1,

    /// <summary>The path is writable (<c>W_OK</c>).</summary>
    Write = 2,

    /// <summary>The path is readable (<c>R_OK</c>).</summary>
    Read = 4,
}
