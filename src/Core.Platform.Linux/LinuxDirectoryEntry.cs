// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform.Linux;

/// <summary>
/// One entry of a directory stream as <c>getdents64</c> reported it.
/// </summary>
/// <remarks>
/// The name is decoded as UTF-8. When the raw bytes are not valid UTF-8 the
/// decoded name contains the replacement character and
/// <see cref="IsNameValidUtf8"/> is <see langword="false"/>; the walk reports
/// the entry as non-UTF-8 rather than silently hashing a lossy name.
/// <see cref="Type"/> is the raw <c>d_type</c>; when the file system did not
/// report a type it is <see cref="LinuxFileType.Unknown"/> and the caller must
/// stat the entry.
/// </remarks>
public readonly struct LinuxDirectoryEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxDirectoryEntry"/> struct.
    /// </summary>
    /// <param name="name">The decoded entry name.</param>
    /// <param name="type">The raw entry type.</param>
    /// <param name="isNameValidUtf8">Whether the raw name bytes are valid UTF-8.</param>
    public LinuxDirectoryEntry(string name, LinuxFileType type, bool isNameValidUtf8)
        : this(name, type, isNameValidUtf8, inode: 0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxDirectoryEntry"/> struct.
    /// </summary>
    /// <param name="name">The decoded entry name.</param>
    /// <param name="type">The raw entry type.</param>
    /// <param name="isNameValidUtf8">Whether the raw name bytes are valid UTF-8.</param>
    /// <param name="inode">The entry's inode (<c>d_ino</c>), or zero when unknown.</param>
    public LinuxDirectoryEntry(string name, LinuxFileType type, bool isNameValidUtf8, ulong inode)
    {
        Name = name;
        Type = type;
        IsNameValidUtf8 = isNameValidUtf8;
        Inode = inode;
    }

    /// <summary>Gets the decoded entry name.</summary>
    public string Name { get; }

    /// <summary>Gets the raw entry type.</summary>
    public LinuxFileType Type { get; }

    /// <summary>Gets a value indicating whether the raw name bytes are valid UTF-8.</summary>
    public bool IsNameValidUtf8 { get; }

    /// <summary>Gets the entry's inode (<c>d_ino</c>), or zero when the file system did not report one.</summary>
    public ulong Inode { get; }
}
