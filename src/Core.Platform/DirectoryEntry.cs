// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform;

/// <summary>
/// One directory entry as a platform directory reader reported it: the decoded
/// name, the kind, whether the raw bytes were valid UTF-8, and the inode when the
/// file system reported one.
/// </summary>
public readonly struct DirectoryEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryEntry"/> struct.
    /// </summary>
    /// <param name="name">The decoded entry name.</param>
    /// <param name="type">The entry kind.</param>
    /// <param name="isNameValidUtf8">Whether the raw name bytes are valid UTF-8.</param>
    /// <param name="inode">The inode, or zero when unknown.</param>
    public DirectoryEntry(string name, FileType type, bool isNameValidUtf8, ulong inode)
    {
        ArgumentNullException.ThrowIfNull(name);

        Name = name;
        Type = type;
        IsNameValidUtf8 = isNameValidUtf8;
        Inode = inode;
    }

    /// <summary>Gets the decoded entry name.</summary>
    public string Name { get; }

    /// <summary>Gets the entry kind.</summary>
    public FileType Type { get; }

    /// <summary>Gets a value indicating whether the raw name bytes are valid UTF-8.</summary>
    public bool IsNameValidUtf8 { get; }

    /// <summary>Gets the inode, or zero when unknown.</summary>
    public ulong Inode { get; }
}
