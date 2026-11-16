// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Microsoft.Win32.SafeHandles;


namespace Core.Platform.Linux;

/// <summary>
/// The Linux <see cref="IFileSystem"/> adapter over the static
/// <see cref="LinuxFileSystem"/> surface.
/// </summary>
/// <remarks>
/// The adapter is a thin stateless mapping: the static Linux surface stays the
/// syscall implementation, and the portable read layers consume only
/// <see cref="IFileSystem"/>. A Windows or macOS adapter implements the same port
/// with its own calls.
/// </remarks>
public sealed class LinuxFileSystemAdapter : IFileSystem
{
    bool IFileSystem.TryStat(string path, out FileStatus status)
    {
        var stat = LinuxFileSystem.TryStat(path, out var linux);
        status = stat ? ToFileStatus(linux) : default;
        return stat;
    }

    bool IFileSystem.TryOpenDirectory(string path, out IDirectoryHandle? handle)
    {
        var opened = LinuxDirectoryHandle.Open(path);

        if (opened.TryGetError(out _))
        {
            handle = null;
            return false;
        }

        handle = opened.Value;
        return true;
    }

    bool IFileSystem.TryAccess(string path, FileAccessMode mode) =>
        LinuxFileSystem.TryAccess(path, (LinuxAccessMode)(int)mode);

    bool IFileSystem.TryOpenRead(string path, bool noFollow, out SafeFileHandle? handle) =>
        noFollow
            ? LinuxFileSystem.TryOpenReadNoBlockNoFollow(path, out handle)
            : LinuxFileSystem.TryOpenReadNoBlock(path, out handle);

    bool IFileSystem.TryStatDescriptor(SafeFileHandle handle, out FileStatus status)
    {
        ArgumentNullException.ThrowIfNull(handle);

        var stat = LinuxFileSystem.TryStatDescriptor(handle.DangerousGetHandle().ToInt32(), out var linux);
        status = stat ? ToFileStatus(linux) : default;
        return stat;
    }

    bool IFileSystem.TrySyncDirectory(string path) => LinuxFileSystem.TrySyncDirectory(path);

    private static FileStatus ToFileStatus(LinuxFileStatus status) =>
        new(
            (FileType)(byte)status.Type,
            status.Size,
            status.MtimeNanoseconds,
            status.Device,
            status.Inode,
            status.Mode,
            status.OwnerUserId,
            status.OwnerGroupId);
}
