// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform.Linux;

/// <summary>
/// The metadata of one Linux file-system object.
/// </summary>
/// <remarks>
/// The shape matches what the walk and the read prefilter need: the type, the
/// pinned size, the modification time in Unix nanoseconds, the physical
/// identity (device and inode) used for cycle detection, and the ownership the
/// checks of a privileged tool need (owner uid, owner gid and the permission
/// bits). A value is produced by
/// <c>LinuxFileSystem.TryStat</c>; the
/// device and inode of a directory handle come from
/// <c>LinuxDirectoryHandle.TryGetStatus</c>.
/// <para>
/// <see cref="Mode"/> carries the permission bits only — set-user-ID,
/// set-group-ID, sticky and the nine <c>rwx</c> bits, that is
/// <c>st_mode &amp; 0o7777</c>, the value GNU <c>stat -c %a</c> prints. The
/// object type is exposed separately by <see cref="Type"/>.
/// </para>
/// </remarks>
public readonly struct LinuxFileStatus
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxFileStatus"/> struct.
    /// </summary>
    /// <param name="type">The file-system object type.</param>
    /// <param name="size">The size in bytes.</param>
    /// <param name="mtimeNanoseconds">The modification time in Unix nanoseconds.</param>
    /// <param name="device">The device identity.</param>
    /// <param name="inode">The inode identity.</param>
    /// <param name="ownerUserId">The owning user id.</param>
    /// <param name="ownerGroupId">The owning group id.</param>
    /// <param name="mode">The permission bits (<c>st_mode &amp; 0o7777</c>).</param>
    public LinuxFileStatus(
        LinuxFileType type,
        long size,
        long mtimeNanoseconds,
        ulong device,
        ulong inode,
        uint ownerUserId,
        uint ownerGroupId,
        uint mode)
    {
        Type = type;
        Size = size;
        MtimeNanoseconds = mtimeNanoseconds;
        Device = device;
        Inode = inode;
        OwnerUserId = ownerUserId;
        OwnerGroupId = ownerGroupId;
        Mode = mode;
    }

    /// <summary>Gets the file-system object type.</summary>
    public LinuxFileType Type { get; }

    /// <summary>Gets the size in bytes.</summary>
    public long Size { get; }

    /// <summary>Gets the modification time in Unix nanoseconds.</summary>
    public long MtimeNanoseconds { get; }

    /// <summary>Gets the device identity.</summary>
    public ulong Device { get; }

    /// <summary>Gets the inode identity.</summary>
    public ulong Inode { get; }

    /// <summary>Gets the id of the user that owns the object.</summary>
    public uint OwnerUserId { get; }

    /// <summary>Gets the id of the group that owns the object.</summary>
    public uint OwnerGroupId { get; }

    /// <summary>
    /// Gets the permission bits of the object (<c>st_mode &amp; 0o7777</c>): the
    /// set-user-ID, set-group-ID and sticky bits and the nine <c>rwx</c> bits,
    /// without the file type.
    /// </summary>
    public uint Mode { get; }
}
