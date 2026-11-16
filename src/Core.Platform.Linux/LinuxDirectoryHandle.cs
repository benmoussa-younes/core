// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using Core.Primitives;
using Core.Results;


namespace Core.Platform.Linux;

/// <summary>
/// An open Linux directory descriptor.
/// </summary>
/// <remarks>
/// A directory is opened with <c>openat</c> and <c>O_DIRECTORY</c>. Opening a
/// child relative to its parent descriptor pins the traversal: a rename of an
/// ancestor cannot redirect the walk to a different directory between the list
/// and the descent. The handle owns its descriptor and closes it on disposal.
/// </remarks>
public sealed class LinuxDirectoryHandle : IDirectoryHandle
{
    private readonly LinuxFileDescriptor _descriptor;

    private LinuxDirectoryHandle(LinuxFileDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    /// <summary>
    /// Gets the raw descriptor. Valid until the handle is disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The handle has been disposed.</exception>
    public int FileDescriptor =>
        _descriptor.IsClosed
            ? throw new ObjectDisposedException(nameof(LinuxDirectoryHandle))
            : (int)_descriptor.DangerousGetHandle();

    /// <summary>
    /// Gets a value indicating whether the descriptor has been closed.
    /// </summary>
    internal bool IsDisposed => _descriptor.IsClosed;

    /// <summary>
    /// Wraps an already-open descriptor and takes ownership of it.
    /// </summary>
    /// <param name="descriptor">The open descriptor to own.</param>
    /// <returns>A handle that closes the descriptor on disposal.</returns>
    internal static LinuxDirectoryHandle FromDescriptor(int descriptor)
    {
        return new LinuxDirectoryHandle(new LinuxFileDescriptor(descriptor));
    }

    /// <summary>
    /// Opens a directory path.
    /// </summary>
    /// <param name="path">The directory path.</param>
    /// <returns>The open handle, or an error when the directory cannot be opened.</returns>
    public static Result<LinuxDirectoryHandle> Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var byteCount = Encoding.UTF8.GetByteCount(path);
        var rented = ArrayPool<byte>.Shared.Rent(byteCount + 1);

        try
        {
            var written = Encoding.UTF8.GetBytes(path, rented);
            rented[written] = 0;

            if (TryOpen(Interop.AtCurrentWorkingDirectory, rented.AsSpan(0, written + 1), noFollow: false, out var handle))
            {
                return Result<LinuxDirectoryHandle>.Success(handle!);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        return Result<LinuxDirectoryHandle>.Failure(
            new CoreError("platform.directory_open_failed", $"Unable to open directory '{PathRedaction.ForMessage(path)}'."));
    }

    /// <summary>
    /// Opens a child directory relative to this handle.
    /// </summary>
    /// <param name="nullTerminatedName">The null-terminated UTF-8 child name.</param>
    /// <param name="followSymbolicLinks">Whether a symbolic link child is followed.</param>
    /// <param name="handle">The child handle when the open succeeded.</param>
    /// <returns><see langword="true"/> when the child was opened.</returns>
    public bool TryOpenAt(ReadOnlySpan<byte> nullTerminatedName, bool followSymbolicLinks, out LinuxDirectoryHandle? handle)
    {
        return TryOpen(FileDescriptor, nullTerminatedName, noFollow: !followSymbolicLinks, out handle);
    }

    /// <summary>
    /// Reads the status of the directory this handle refers to.
    /// </summary>
    /// <param name="status">The status when the call succeeds.</param>
    /// <returns><see langword="true"/> when the directory was stat-ed.</returns>
    public bool TryGetStatus(out LinuxFileStatus status)
    {
        return LinuxFileSystem.TryStatDescriptor(FileDescriptor, out status);
    }

    /// <summary>
    /// Creates a reader over this directory's entries.
    /// </summary>
    /// <returns>The directory reader. It must be disposed.</returns>
    public LinuxDirectoryReader Enumerate()
    {
        return new LinuxDirectoryReader(this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _descriptor.Dispose();
    }

    private static unsafe bool TryOpen(int directoryDescriptor, ReadOnlySpan<byte> path, bool noFollow, out LinuxDirectoryHandle? handle)
    {
        handle = null;

        if (path.IsEmpty || !OperatingSystem.IsLinux())
        {
            return false;
        }

        var flags = Interop.OpenReadOnly | Interop.OpenDirectory | Interop.OpenCloseOnExec;

        if (noFollow)
        {
            flags |= Interop.OpenNoFollow;
        }

        fixed (byte* pathPointer = path)
        {
            while (true)
            {
                var descriptor = Interop.OpenAt(directoryDescriptor, pathPointer, flags, 0);

                if (descriptor >= 0)
                {
                    handle = new LinuxDirectoryHandle(new LinuxFileDescriptor(descriptor));
                    return true;
                }

                // A signal can interrupt openat before the descriptor is built;
                // retry on EINTR, exactly as the read and write wrappers do.
                if (Marshal.GetLastPInvokeError() != (int)LinuxErrno.Interrupted)
                {
                    return false;
                }
            }
        }
    }

    bool IDirectoryHandle.TryGetStatus(out FileStatus status)
    {
        var stat = TryGetStatus(out var linux);
        status = stat ? ToFileStatus(linux) : default;
        return stat;
    }

    IDirectoryReader IDirectoryHandle.Enumerate() => Enumerate();

    bool IDirectoryHandle.TryOpenDirectory(ReadOnlySpan<byte> name, bool followSymbolicLinks, out IDirectoryHandle? handle)
    {
        var opened = TryOpenAt(name, followSymbolicLinks, out var linux);
        handle = opened ? linux : null;
        return opened;
    }

    bool IDirectoryHandle.TryStatAt(ReadOnlySpan<byte> name, bool followSymbolicLinks, out FileStatus status)
    {
        var stat = LinuxFileSystem.TryStatAt(this, name, followSymbolicLinks, out var linux);
        status = stat ? ToFileStatus(linux) : default;
        return stat;
    }

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
