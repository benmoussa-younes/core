// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform;

namespace Core.IO.Files;

/// <summary>
/// The metadata of one file: the pinned size and modification time, the
/// physical identity and the file type.
/// </summary>
/// <remarks>
/// The size and modification time are taken immediately before the file is
/// opened, matching the toolchain's pinned-metadata contract. Device and inode
/// are carried for cycle detection; the type is what the kernel reported.
/// </remarks>
public readonly struct FileMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileMetadata"/> struct.
    /// </summary>
    /// <param name="size">The size in bytes.</param>
    /// <param name="mtimeNanoseconds">The modification time in Unix nanoseconds.</param>
    /// <param name="device">The device identity.</param>
    /// <param name="inode">The inode identity.</param>
    /// <param name="type">The file type.</param>
    public FileMetadata(long size, long mtimeNanoseconds, ulong device, ulong inode, FileType type)
    {
        Size = size;
        MtimeNanoseconds = mtimeNanoseconds;
        Device = device;
        Inode = inode;
        Type = type;
    }

    /// <summary>Gets the size in bytes.</summary>
    public long Size { get; }

    /// <summary>Gets the modification time in Unix nanoseconds.</summary>
    public long MtimeNanoseconds { get; }

    /// <summary>Gets the device identity.</summary>
    public ulong Device { get; }

    /// <summary>Gets the inode identity.</summary>
    public ulong Inode { get; }

    /// <summary>Gets the file type.</summary>
    public FileType Type { get; }
}
