// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Microsoft.Win32.SafeHandles;

namespace Core.Platform;

/// <summary>
/// The minimal file-system port the portable read and walk layers consume: stat,
/// access, a non-blocking read open and a directory sync.
/// </summary>
/// <remarks>
/// A platform adapter implements it (the Linux one lives in
/// <c>Core.Platform.Linux</c> until the adapter split completes). A caller that
/// needs directory enumeration uses a directory-walk adapter, not this interface:
/// the port stays small so a fake is cheap and a new OS is one class.
/// </remarks>
public interface IFileSystem
{
    /// <summary>
    /// Stats a path.
    /// </summary>
    /// <param name="path">The path to stat.</param>
    /// <param name="status">The status when the call succeeds; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the path was stat-ed.</returns>
    bool TryStat(string path, out FileStatus status);

    /// <summary>
    /// Checks whether the process may access a path in the given mode.
    /// </summary>
    /// <param name="path">The path to probe.</param>
    /// <param name="mode">The access mode.</param>
    /// <returns><see langword="true"/> when access is permitted.</returns>
    bool TryAccess(string path, FileAccessMode mode);

    /// <summary>
    /// Opens a directory.
    /// </summary>
    /// <param name="path">The directory path.</param>
    /// <param name="handle">The open handle when the call succeeds; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the directory was opened.</returns>
    bool TryOpenDirectory(string path, out IDirectoryHandle? handle);

    /// <summary>
    /// Opens a path read-only without blocking, optionally refusing a final
    /// symbolic link.
    /// </summary>
    /// <param name="path">The path to open.</param>
    /// <param name="noFollow">Whether a final symbolic link is refused.</param>
    /// <param name="handle">The open handle when the call succeeds; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the path was opened.</returns>
    bool TryOpenRead(string path, bool noFollow, out SafeFileHandle? handle);

    /// <summary>
    /// Stats an already open descriptor.
    /// </summary>
    /// <param name="handle">The open handle.</param>
    /// <param name="status">The status when the call succeeds; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the handle was stat-ed.</returns>
    bool TryStatDescriptor(SafeFileHandle handle, out FileStatus status);

    /// <summary>
    /// Syncs a directory to stable storage.
    /// </summary>
    /// <param name="path">The directory to sync.</param>
    /// <returns><see langword="true"/> when the sync was accepted.</returns>
    bool TrySyncDirectory(string path);
}
