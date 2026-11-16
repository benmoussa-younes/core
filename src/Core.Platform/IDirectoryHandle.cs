// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform;

/// <summary>
/// An open directory handle: status, entry enumeration and the descriptor-pinned
/// open of a child.
/// </summary>
public interface IDirectoryHandle : IDisposable
{
    /// <summary>
    /// Stats the open directory descriptor.
    /// </summary>
    /// <param name="status">The status when the call succeeds; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the directory was stat-ed.</returns>
    bool TryGetStatus(out FileStatus status);

    /// <summary>Enumerates the directory's entries.</summary>
    /// <returns>The reader; the caller disposes it.</returns>
    IDirectoryReader Enumerate();

    /// <summary>
    /// Opens a child directory by its raw null-terminated name.
    /// </summary>
    /// <param name="name">The null-terminated child name.</param>
    /// <param name="followSymbolicLinks">Whether a final symbolic link is followed.</param>
    /// <param name="handle">The child handle when the call succeeds; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the child was opened.</returns>
    bool TryOpenDirectory(ReadOnlySpan<byte> name, bool followSymbolicLinks, out IDirectoryHandle? handle);

    /// <summary>
    /// Stats a child by its raw null-terminated name.
    /// </summary>
    /// <param name="name">The null-terminated child name.</param>
    /// <param name="followSymbolicLinks">Whether a final symbolic link is followed.</param>
    /// <param name="status">The status when the call succeeds; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the child was stat-ed.</returns>
    bool TryStatAt(ReadOnlySpan<byte> name, bool followSymbolicLinks, out FileStatus status);
}
