// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform.Linux;

/// <summary>
/// The type of a Linux file-system object.
/// </summary>
/// <remarks>
/// The values are the kernel's <c>d_type</c> values, so a raw
/// <c>getdents64</c> entry maps without a lookup. <see cref="Unknown"/> is the
/// kernel's <c>DT_UNKNOWN</c>: the file system did not report a type and the
/// caller must resolve it with <c>statx</c> or <c>fstatat</c>.
/// </remarks>
public enum LinuxFileType : byte
{
    /// <summary>The type is unknown; the caller must stat the entry.</summary>
    Unknown = 0,

    /// <summary>A named pipe.</summary>
    Fifo = 1,

    /// <summary>A character device.</summary>
    CharacterDevice = 2,

    /// <summary>A directory.</summary>
    Directory = 4,

    /// <summary>A block device.</summary>
    BlockDevice = 6,

    /// <summary>A regular file.</summary>
    RegularFile = 8,

    /// <summary>A symbolic link.</summary>
    SymbolicLink = 10,

    /// <summary>A socket.</summary>
    Socket = 12,
}
