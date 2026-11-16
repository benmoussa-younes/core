// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform.Linux;

/// <summary>
/// The caching advice passed to <see cref="LinuxFileSystem.TryAdvise"/>.
/// </summary>
/// <remarks>
/// The values are the POSIX <c>POSIX_FADV_*</c> constants; the libc wrapper
/// <c>posix_fadvise</c> is the userspace entry point for the <c>fadvise64</c>
/// kernel call.
/// </remarks>
public enum LinuxFileAdvice
{
    /// <summary>No advice (<c>POSIX_FADV_NORMAL</c>).</summary>
    Normal = 0,

    /// <summary>Random access (<c>POSIX_FADV_RANDOM</c>).</summary>
    Random = 1,

    /// <summary>Sequential access (<c>POSIX_FADV_SEQUENTIAL</c>).</summary>
    Sequential = 2,

    /// <summary>The pages will be needed soon (<c>POSIX_FADV_WILLNEED</c>).</summary>
    WillNeed = 3,

    /// <summary>The pages will not be needed soon (<c>POSIX_FADV_DONTNEED</c>).</summary>
    DoNotNeed = 4,
}
