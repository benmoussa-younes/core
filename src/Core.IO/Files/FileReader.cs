// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform;
using Core.Primitives;
using Microsoft.Win32.SafeHandles;
using Core.Results;


namespace Core.IO.Files;

/// <summary>
/// An open regular file with its pinned metadata.
/// </summary>
/// <remarks>
/// Opening runs a stat and an explicit read-access check before the handle is
/// created, so a missing or unauthorized path is classified without ever
/// opening it. The metadata (including size and modification time) is captured
/// at open time; content written after that point is not part of the pinned
/// size.
/// </remarks>
public sealed class FileReader : IDisposable
{
    private readonly SafeFileHandle _handle;

    private FileReader(SafeFileHandle handle, FileMetadata metadata)
    {
        _handle = handle;
        Metadata = metadata;
    }

    /// <summary>Gets the pinned metadata captured before the open.</summary>
    public FileMetadata Metadata { get; }

    /// <summary>Gets the open read handle.</summary>
    public SafeFileHandle Handle => _handle;

    /// <summary>
    /// Opens a path on an explicit platform file system.
    /// </summary>
    /// <param name="fileSystem">The platform file system.</param>
    /// <param name="path">The file path.</param>
    /// <returns>The open reader, or an error when the path cannot be read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    public static Result<FileReader> Open(IFileSystem fileSystem, string path) =>
        Open(fileSystem, path, FileIdentity.Unknown);

    /// <summary>
    /// Opens a path on an explicit platform file system and verifies the identity.
    /// </summary>
    /// <param name="fileSystem">The platform file system.</param>
    /// <param name="path">The file path.</param>
    /// <param name="expected">The identity the walk captured; <see cref="FileIdentity.Unknown"/> skips the check.</param>
    /// <returns>The open reader, or an error when the path cannot be read or was swapped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    public static Result<FileReader> Open(IFileSystem fileSystem, string path, in FileIdentity expected)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        return OpenCore(fileSystem, path, expected, noFollow: false);
    }

    /// <summary>
    /// Opens a path on an explicit platform file system, refusing a final symbolic link.
    /// </summary>
    /// <param name="fileSystem">The platform file system.</param>
    /// <param name="path">The file path.</param>
    /// <returns>The open reader, or an error when the path is a link or cannot be read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    public static Result<FileReader> OpenNoFollow(IFileSystem fileSystem, string path) =>
        OpenCore(fileSystem, path, FileIdentity.Unknown, noFollow: true);

    private static Result<FileReader> OpenCore(IFileSystem fileSystem, string path, in FileIdentity expected, bool noFollow)
    {
        SafeFileHandle? handle;
        var opened = fileSystem.TryOpenRead(path, noFollow, out handle);

        if (!opened || handle is null)
        {
            return Unreadable(path);
        }

        // fstat the descriptor itself: the handle is already pinned, so the check
        // cannot race a swap after it passes.
        var verified = fileSystem.TryStatDescriptor(handle, out var status)
            && status.Type == FileType.RegularFile
            && (!expected.IsKnown || (status.Device == expected.Device && status.Inode == expected.Inode));

        if (!verified)
        {
            handle.Dispose();
            return Unreadable(path);
        }

        var metadata = new FileMetadata(status.Size, status.MtimeNanoseconds, status.Device, status.Inode, status.Type);

        return Result<FileReader>.Success(new FileReader(handle, metadata));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _handle.Dispose();
    }

    /// <summary>
    /// Opens a path whose status was already read, so the prefilter is not
    /// repeated per file.
    /// </summary>
    /// <param name="fileSystem">The platform file system.</param>
    /// <param name="path">The file path.</param>
    /// <param name="status">The already-read status.</param>
    /// <returns>The open reader, or an error when the path cannot be opened.</returns>
    internal static Result<FileReader> Open(IFileSystem fileSystem, string path, in FileStatus status)
    {
        try
        {
            var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
            var metadata = new FileMetadata(status.Size, status.MtimeNanoseconds, status.Device, status.Inode, status.Type);

            return Result<FileReader>.Success(new FileReader(handle, metadata));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Unreadable(path);
        }
    }


    private static Result<FileReader> Unreadable(string path)
    {
        return Result<FileReader>.Failure(new CoreError(
            "io.unreadable",
            $"Unable to read '{PathRedaction.ForMessage(path)}'."));
    }
}
