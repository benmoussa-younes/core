// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform;

/// <summary>
/// The portable status of one file-system object: the fields the read and walk
/// layers need, with no platform handle.
/// </summary>
/// <remarks>
/// The Linux adapter fills every field from one <c>statx</c>/<c>fstat</c> result.
/// A platform that cannot report a field leaves it at zero; the type is the only
/// field a caller must always trust.
/// </remarks>
public readonly struct FileStatus
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileStatus"/> struct.
    /// </summary>
    /// <param name="type">The object kind.</param>
    /// <param name="size">The size in bytes.</param>
    /// <param name="mtimeNanoseconds">The modification time, in nanoseconds.</param>
    /// <param name="device">The device identity, or zero when unknown.</param>
    /// <param name="inode">The inode, or zero when unknown.</param>
    /// <param name="mode">The POSIX permission bits, or zero when unknown.</param>
    /// <param name="ownerUserId">The owner user id, or zero when unknown.</param>
    /// <param name="ownerGroupId">The owner group id, or zero when unknown.</param>
    public FileStatus(
        FileType type,
        long size,
        long mtimeNanoseconds,
        ulong device,
        ulong inode,
        uint mode,
        uint ownerUserId,
        uint ownerGroupId)
    {
        Type = type;
        Size = size;
        MtimeNanoseconds = mtimeNanoseconds;
        Device = device;
        Inode = inode;
        Mode = mode;
        OwnerUserId = ownerUserId;
        OwnerGroupId = ownerGroupId;
    }

    /// <summary>Gets the object kind.</summary>
    public FileType Type { get; }

    /// <summary>Gets the size in bytes.</summary>
    public long Size { get; }

    /// <summary>Gets the modification time, in nanoseconds.</summary>
    public long MtimeNanoseconds { get; }

    /// <summary>Gets the device identity, or zero when unknown.</summary>
    public ulong Device { get; }

    /// <summary>Gets the inode, or zero when unknown.</summary>
    public ulong Inode { get; }

    /// <summary>Gets the POSIX permission bits, or zero when unknown.</summary>
    public uint Mode { get; }

    /// <summary>Gets the owner user id, or zero when unknown.</summary>
    public uint OwnerUserId { get; }

    /// <summary>Gets the owner group id, or zero when unknown.</summary>
    public uint OwnerGroupId { get; }
}
