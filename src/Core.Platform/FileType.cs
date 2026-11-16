// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform;

/// <summary>
/// The portable kind of a file-system object.
/// </summary>
/// <remarks>
/// The values match the Linux <c>d_type</c> values, so the Linux adapter casts a
/// raw directory entry without a lookup; a Windows or macOS adapter maps its own
/// kinds onto the same set. <see cref="Unknown"/> means the platform did not
/// report the type and the caller must stat the entry.
/// </remarks>
public enum FileType : byte
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
