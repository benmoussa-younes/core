// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform.Linux;

/// <summary>
/// The access-pattern advice passed to <see cref="LinuxMemoryMapping.Advise"/>.
/// </summary>
/// <remarks>
/// The values are the POSIX <c>MADV_*</c> constants for <c>madvise</c>.
/// </remarks>
public enum LinuxMemoryAdvice
{
    /// <summary>No special handling (<c>MADV_NORMAL</c>).</summary>
    Normal = 0,

    /// <summary>Random access; read-ahead is reduced (<c>MADV_RANDOM</c>).</summary>
    Random = 1,

    /// <summary>Sequential access; read-ahead is increased (<c>MADV_SEQUENTIAL</c>).</summary>
    Sequential = 2,

    /// <summary>The pages will be needed soon; start the read-ahead now (<c>MADV_WILLNEED</c>).</summary>
    WillNeed = 3,

    /// <summary>The pages will not be needed soon (<c>MADV_DONTNEED</c>).</summary>
    DoNotNeed = 4,
}
