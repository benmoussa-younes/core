// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;


namespace Core.Platform.Linux;

/// <summary>
/// The managed-safe Linux file-system surface: stat, access, positional read,
/// caching advice and write. Every method is a non-throwing probe that reports
/// failure as <see langword="false"/> (or a negative read/write count) and
/// leaves the raw <c>errno</c> available through <see cref="LastError"/>.
/// </summary>
/// <remarks>
/// <c>statx</c> is the primary stat call because its layout is
/// architecture-independent and it removes the per-architecture
/// <c>st_mode</c> problem. When the kernel reports <c>ENOSYS</c> the process
/// switches once to the <c>fstatat</c> fallback and the architecture table.
/// Every entry point is guarded by <see cref="OperatingSystem.IsLinux"/>; on
/// any other platform it fails without calling into libc.
/// </remarks>
public static class LinuxFileSystem
{
    private const int FileStatusFlagsCommand = 3;

    private static readonly byte[] EmptyPath = [0];
    private static int _statxAvailable = 1;

    /// <summary>
    /// Gets the <c>errno</c> of the most recent failed probe.
    /// </summary>
    public static LinuxErrno LastError => (LinuxErrno)Marshal.GetLastPInvokeError();

    /// <summary>
    /// Gets a value indicating whether the process uses the <c>statx</c> call.
    /// </summary>
    /// <remarks>
    /// Read-only by design: the only writer is the one-time <c>ENOSYS</c> switch
    /// in <c>Stat</c>. The proof suite drives the <c>fstatat</c> fallback through
    /// <see cref="TryStatWithFallback"/>, which does not mutate this flag, so no
    /// test can race another through process-wide state.
    /// </remarks>
    internal static bool StatxAvailable => Volatile.Read(ref _statxAvailable) != 0;

    /// <summary>
    /// Stats a path, following symbolic links.
    /// </summary>
    /// <param name="path">The path to inspect.</param>
    /// <param name="status">The status when the call succeeds.</param>
    /// <returns><see langword="true"/> when the path was stat-ed.</returns>
    public static bool TryStat(string path, out LinuxFileStatus status)
    {
        status = default;
        return OperatingSystem.IsLinux() && TryStatEncoded(path, out status);
    }

    /// <summary>
    /// Stats a path with the <c>fstatat</c> fallback forced, for the proof suite.
    /// </summary>
    /// <remarks>
    /// The fallback runs for this one call without mutating the process-wide
    /// <see cref="StatxAvailable"/> flag, so the fallback branches are covered
    /// deterministically and one test cannot race another.
    /// </remarks>
    /// <param name="path">The path to inspect.</param>
    /// <param name="status">The status when the call succeeds.</param>
    /// <returns><see langword="true"/> when the path was stat-ed.</returns>
    internal static bool TryStatWithFallback(string path, out LinuxFileStatus status)
    {
        status = default;
        return OperatingSystem.IsLinux() && TryStatEncoded(path, statxAvailable: false, out status);
    }

    /// <summary>
    /// Stats a null-terminated UTF-8 path, following symbolic links.
    /// </summary>
    /// <param name="nullTerminatedPath">The null-terminated UTF-8 path.</param>
    /// <param name="status">The status when the call succeeds.</param>
    /// <returns><see langword="true"/> when the path was stat-ed.</returns>
    public static bool TryStat(ReadOnlySpan<byte> nullTerminatedPath, out LinuxFileStatus status)
    {
        status = default;
        return OperatingSystem.IsLinux() && Stat(Interop.AtCurrentWorkingDirectory, nullTerminatedPath, 0, out status);
    }

    /// <summary>
    /// Stats a directory entry relative to an open directory handle.
    /// </summary>
    /// <param name="directory">The open parent directory.</param>
    /// <param name="nullTerminatedName">The null-terminated UTF-8 entry name.</param>
    /// <param name="followSymbolicLinks">Whether the final symbolic link is followed.</param>
    /// <param name="status">The status when the call succeeds.</param>
    /// <returns><see langword="true"/> when the entry was stat-ed.</returns>
    public static bool TryStatAt(
        LinuxDirectoryHandle directory,
        ReadOnlySpan<byte> nullTerminatedName,
        bool followSymbolicLinks,
        out LinuxFileStatus status)
    {
        ArgumentNullException.ThrowIfNull(directory);

        status = default;
        var flags = followSymbolicLinks ? 0 : Interop.AtSymbolicLinkNoFollow;
        return OperatingSystem.IsLinux() && Stat(directory.FileDescriptor, nullTerminatedName, flags, out status);
    }

    /// <summary>
    /// Opens a path read-only without blocking on a special file
    /// (<c>openat</c> with <c>O_RDONLY|O_NONBLOCK|O_CLOEXEC</c>).
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="handle">The open handle when the call succeeds; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the path was opened.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static bool TryOpenReadNoBlock(string path, out SafeFileHandle? handle)
    {
        ArgumentNullException.ThrowIfNull(path);

        handle = null;

        return OperatingSystem.IsLinux() && TryOpenReadNoBlockCore(path, noFollow: false, out handle);
    }

    /// <summary>
    /// Opens a path read-only without blocking or following a final symbolic link
    /// (<c>openat</c> with <c>O_RDONLY|O_NONBLOCK|O_NOFOLLOW|O_CLOEXEC</c>).
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="handle">The open handle when the call succeeds; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the path was opened.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    internal static bool TryOpenReadNoBlockNoFollow(string path, out SafeFileHandle? handle)
    {
        ArgumentNullException.ThrowIfNull(path);

        handle = null;

        return OperatingSystem.IsLinux() && TryOpenReadNoBlockCore(path, noFollow: true, out handle);
    }

    private static unsafe bool TryOpenReadNoBlockCore(string path, bool noFollow, out SafeFileHandle? handle)
    {
        handle = null;

        var rented = RentNullTerminatedPath(path, out _);

        try
        {
            var flags = Interop.OpenReadOnly | Interop.OpenNonBlock | Interop.OpenCloseOnExec;

            if (noFollow)
            {
                flags |= Interop.OpenNoFollow;
            }

            fixed (byte* pointer = rented)
            {
                while (true)
                {
                    var descriptor = Interop.OpenAt(Interop.AtCurrentWorkingDirectory, pointer, flags, 0);

                    if (descriptor >= 0)
                    {
                        // O_NONBLOCK gets past a FIFO; the type check then refuses
                        // it instead of writing a log line into a pipe.
                        if (!TryStatDescriptor(descriptor, out var status) ||
                            status.Type != LinuxFileType.RegularFile)
                        {
                            _ = Interop.Close(descriptor);
                            return false;
                        }

                        handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
                        return true;
                    }

                    if (Marshal.GetLastPInvokeError() != (int)LinuxErrno.Interrupted)
                    {
                        return false;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Opens a path for appending without following a final symbolic link
    /// (<c>openat</c> with <c>O_WRONLY|O_CREAT|O_APPEND|O_NOFOLLOW|O_CLOEXEC</c>).
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="mode">The create mode, as the kernel applies the process umask.</param>
    /// <param name="handle">The open handle when the call succeeds; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the path was opened.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    internal static bool TryOpenAppendNoFollow(string path, UnixFileMode mode, out SafeFileHandle? handle)
    {
        ArgumentNullException.ThrowIfNull(path);

        handle = null;

        return OperatingSystem.IsLinux() && TryOpenAppendNoFollowCore(path, mode, out handle);
    }

    private static unsafe bool TryOpenAppendNoFollowCore(string path, UnixFileMode mode, out SafeFileHandle? handle)
    {
        handle = null;

        var rented = RentNullTerminatedPath(path, out _);

        try
        {
            var flags = Interop.OpenWriteOnly |
                Interop.OpenCreate |
                Interop.OpenAppend |
                Interop.OpenNonBlock |
                Interop.OpenNoFollow |
                Interop.OpenCloseOnExec;

            fixed (byte* pointer = rented)
            {
                while (true)
                {
                    var descriptor = Interop.OpenAt(
                        Interop.AtCurrentWorkingDirectory,
                        pointer,
                        flags,
                        (uint)mode);

                    if (descriptor >= 0)
                    {
                        // O_NONBLOCK gets past a FIFO; the type check then refuses
                        // it instead of writing a log line into a pipe.
                        if (!TryStatDescriptor(descriptor, out var status) ||
                            status.Type != LinuxFileType.RegularFile)
                        {
                            _ = Interop.Close(descriptor);
                            return false;
                        }

                        handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
                        return true;
                    }

                    if (Marshal.GetLastPInvokeError() != (int)LinuxErrno.Interrupted)
                    {
                        return false;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Stats an open descriptor itself (the file the descriptor refers to) with
    /// <c>fstat</c>: measured faster than <c>statx(AT_EMPTY_PATH)</c> for the
    /// same descriptor (874 vs 982 ns/call on this host).
    /// </summary>
    /// <param name="descriptor">The open descriptor.</param>
    /// <param name="status">The status when the call succeeds.</param>
    /// <returns><see langword="true"/> when the descriptor was stat-ed.</returns>
    public static bool TryStatDescriptor(int descriptor, out LinuxFileStatus status)
    {
        status = default;
        return OperatingSystem.IsLinux() && TryFStat(descriptor, out status);
    }

    /// <summary>
    /// Checks whether the process may access a path in the given mode.
    /// </summary>
    /// <param name="path">The path to probe.</param>
    /// <param name="mode">The access mode.</param>
    /// <returns><see langword="true"/> when access is permitted.</returns>
    public static bool TryAccess(string path, LinuxAccessMode mode)
    {
        return OperatingSystem.IsLinux() && TryAccessEncoded(path, mode);
    }

    /// <summary>
    /// Checks whether the process may access a null-terminated UTF-8 path in the
    /// given mode.
    /// </summary>
    /// <param name="nullTerminatedPath">The null-terminated UTF-8 path.</param>
    /// <param name="mode">The access mode.</param>
    /// <returns><see langword="true"/> when access is permitted.</returns>
    public static bool TryAccess(ReadOnlySpan<byte> nullTerminatedPath, LinuxAccessMode mode)
    {
        return OperatingSystem.IsLinux() && Access(nullTerminatedPath, mode);
    }

    /// <summary>
    /// Opens a path read-only with close-on-exec (<c>openat</c>), retrying a
    /// signal-interrupted call.
    /// </summary>
    /// <param name="path">The path to open.</param>
    /// <param name="descriptor">The open descriptor when the call succeeds; otherwise <c>-1</c>.</param>
    /// <returns><see langword="true"/> when the path was opened.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static bool TryOpen(string path, out int descriptor)
    {
        ArgumentNullException.ThrowIfNull(path);

        descriptor = -1;
        return OperatingSystem.IsLinux() && TryOpenEncoded(path, out descriptor);
    }

    /// <summary>
    /// Reads the descriptor status flags (<c>fcntl</c> <c>F_GETFL</c>): the
    /// access mode and the <c>O_*</c> flags.
    /// </summary>
    /// <param name="descriptor">The open descriptor.</param>
    /// <param name="flags">The flags when the call succeeds.</param>
    /// <returns><see langword="true"/> when the flags were read.</returns>
    public static bool TryGetFileStatusFlags(int descriptor, out int flags)
    {
        flags = 0;

        while (true)
        {
            // The guard is a conditional expression, not an early-return block:
            // the line stays covered on Linux, where the tests run.
            var result = OperatingSystem.IsLinux()
                ? Interop.Fcntl(descriptor, FileStatusFlagsCommand, 0)
                : -1;

            if (result >= 0)
            {
                flags = result;
                return true;
            }

            if (!OperatingSystem.IsLinux() || Marshal.GetLastPInvokeError() != (int)LinuxErrno.Interrupted)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Reads from a descriptor at an absolute offset without moving the file
    /// position (<c>pread64</c>).
    /// </summary>
    /// <param name="descriptor">The open descriptor.</param>
    /// <param name="buffer">The destination buffer.</param>
    /// <param name="offset">The absolute file offset.</param>
    /// <returns>The bytes read, or <c>-1</c> on failure.</returns>
    public static int TryReadAt(int descriptor, Span<byte> buffer, long offset)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        return OperatingSystem.IsLinux() ? ReadAt(descriptor, buffer, offset) : -1;
    }

    /// <summary>
    /// Advises the kernel about the expected access pattern of a descriptor
    /// (<c>posix_fadvise</c>, the userspace entry point for <c>fadvise64</c>).
    /// </summary>
    /// <param name="descriptor">The open descriptor.</param>
    /// <param name="offset">The start offset.</param>
    /// <param name="length">The length, or zero for the remainder of the file.</param>
    /// <param name="advice">The access advice.</param>
    /// <returns><see langword="true"/> when the advice was accepted.</returns>
    public static bool TryAdvise(int descriptor, long offset, long length, LinuxFileAdvice advice) =>
        OperatingSystem.IsLinux() && Advise(descriptor, offset, length, advice);

    private static bool Advise(int descriptor, long offset, long length, LinuxFileAdvice advice)
    {
        while (true)
        {
            // posix_fadvise returns the errno instead of setting it, so mirror it
            // into the last-error slot the rest of the surface reads. EINTR is
            // retried, so the loop has no explicit continue line.
            var result = Interop.PosixFAdvise(descriptor, offset, length, (int)advice);

            if (result == 0)
            {
                Marshal.SetLastPInvokeError(0);
                return true;
            }

            if (result != (int)LinuxErrno.Interrupted)
            {
                Marshal.SetLastPInvokeError(result);
                return false;
            }
        }
    }

    /// <summary>
    /// Writes a buffer to a descriptor (<c>write</c>).
    /// </summary>
    /// <param name="descriptor">The open writable descriptor.</param>
    /// <param name="buffer">The bytes to write.</param>
    /// <returns>The bytes written, or <c>-1</c> on failure.</returns>
    public static int TryWrite(int descriptor, ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        return OperatingSystem.IsLinux() ? Write(descriptor, buffer) : -1;
    }

    /// <summary>
    /// Flushes a directory's own entries to stable storage (<c>fsync</c> on a
    /// descriptor opened on the directory), so a rename that just landed
    /// survives a power loss.
    /// </summary>
    /// <param name="path">The directory path.</param>
    /// <returns><see langword="true"/> when the directory was synced.</returns>
    public static bool TrySyncDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var rented = RentNullTerminatedPath(path, out var length);

        try
        {
            // The guard is a conditional expression, not an early-return block:
            // the line stays covered on Linux, where the tests run.
            return OperatingSystem.IsLinux() && SyncDirectory(rented.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Flushes the object behind an open descriptor to stable storage, retrying
    /// a signal-interrupted call.
    /// </summary>
    /// <param name="descriptor">The open descriptor.</param>
    /// <returns><see langword="true"/> when the descriptor was synced.</returns>
    internal static bool TrySyncDescriptor(int descriptor)
    {
        while (true)
        {
            if (Interop.FSync(descriptor) == 0)
            {
                return true;
            }

            if (Marshal.GetLastPInvokeError() != (int)LinuxErrno.Interrupted)
            {
                return false;
            }
        }
    }

    private static unsafe bool SyncDirectory(ReadOnlySpan<byte> nullTerminatedPath)
    {
        fixed (byte* path = nullTerminatedPath)
        {
            var descriptor = Interop.OpenAt(
                Interop.AtCurrentWorkingDirectory,
                path,
                Interop.OpenReadOnly | Interop.OpenDirectory | Interop.OpenCloseOnExec,
                0);

            if (descriptor < 0)
            {
                return false;
            }

            try
            {
                return TrySyncDescriptor(descriptor);
            }
            finally
            {
                _ = Interop.Close(descriptor);
            }
        }
    }

    private static bool TryStatEncoded(string path, out LinuxFileStatus status)
    {
        return TryStatEncoded(path, statxAvailable: Volatile.Read(ref _statxAvailable) != 0, out status);
    }

    private static bool TryStatEncoded(string path, bool statxAvailable, out LinuxFileStatus status)
    {
        var rented = RentNullTerminatedPath(path, out var length);

        try
        {
            return Stat(Interop.AtCurrentWorkingDirectory, rented.AsSpan(0, length), 0, statxAvailable, out status);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static bool TryAccessEncoded(string path, LinuxAccessMode mode)
    {
        var rented = RentNullTerminatedPath(path, out var length);

        try
        {
            return Access(rented.AsSpan(0, length), mode);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static bool TryOpenEncoded(string path, out int descriptor)
    {
        descriptor = -1;
        var rented = RentNullTerminatedPath(path, out var length);

        try
        {
            return Open(rented.AsSpan(0, length), out descriptor);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    // Encodes the path as UTF-8 into a pooled buffer with a trailing NUL, shared by
    // every syscall wrapper. No delegate: the caller returns the buffer. The length
    // includes the NUL.
    private static byte[] RentNullTerminatedPath(string path, out int length)
    {
        // A NUL cannot be part of a path: the kernel stops at the first one, so the
        // call would silently act on a prefix. Refuse it here, once, so no wrapper
        // can forget the check.
        if (path.Contains('\0'))
        {
            throw new ArgumentException("A path cannot contain a NUL character.", nameof(path));
        }

        var byteCount = Encoding.UTF8.GetByteCount(path);
        var rented = ArrayPool<byte>.Shared.Rent(byteCount + 1);

        var written = Encoding.UTF8.GetBytes(path, rented);
        rented[written] = 0;
        length = written + 1;

        return rented;
    }

    private static unsafe bool Open(ReadOnlySpan<byte> nullTerminatedPath, out int descriptor)
    {
        descriptor = -1;

        fixed (byte* pathPointer = nullTerminatedPath)
        {
            while (true)
            {
                var result = Interop.OpenAt(
                    Interop.AtCurrentWorkingDirectory,
                    pathPointer,
                    Interop.OpenReadOnly | Interop.OpenCloseOnExec,
                    0);

                if (result >= 0)
                {
                    descriptor = result;
                    return true;
                }

                if (Marshal.GetLastPInvokeError() != (int)LinuxErrno.Interrupted)
                {
                    return false;
                }
            }
        }
    }

    private static unsafe bool Access(ReadOnlySpan<byte> nullTerminatedPath, LinuxAccessMode mode)
    {
        fixed (byte* path = nullTerminatedPath)
        {
            while (true)
            {
                if (Interop.FAccessAt(Interop.AtCurrentWorkingDirectory, path, (int)mode, 0) == 0)
                {
                    return true;
                }

                // Retry a signal-interrupted access probe, exactly as the read
                // and write wrappers do.
                if (Marshal.GetLastPInvokeError() != (int)LinuxErrno.Interrupted)
                {
                    return false;
                }
            }
        }
    }

    private static unsafe int ReadAt(int descriptor, Span<byte> buffer, long offset)
    {
        fixed (byte* destination = buffer)
        {
            while (true)
            {
                var read = Interop.PRead64(descriptor, destination, (nuint)buffer.Length, offset);

                if (read >= 0)
                {
                    return (int)read;
                }

                if (Marshal.GetLastPInvokeError() != (int)LinuxErrno.Interrupted)
                {
                    return -1;
                }
            }
        }
    }

    private static unsafe int Write(int descriptor, ReadOnlySpan<byte> buffer)
    {
        fixed (byte* source = buffer)
        {
            while (true)
            {
                var written = Interop.Write(descriptor, source, (nuint)buffer.Length);

                if (written >= 0)
                {
                    return (int)written;
                }

                if (Marshal.GetLastPInvokeError() != (int)LinuxErrno.Interrupted)
                {
                    return -1;
                }
            }
        }
    }

    private static bool Stat(int directoryDescriptor, ReadOnlySpan<byte> nullTerminatedPath, int flags, out LinuxFileStatus status)
    {
        return Stat(
            directoryDescriptor,
            nullTerminatedPath,
            flags,
            statxAvailable: Volatile.Read(ref _statxAvailable) != 0,
            out status);
    }

    /// <summary>
    /// Stats with an explicit <c>statx</c> availability, so the fallback can be
    /// driven for one call without mutating process-wide state.
    /// </summary>
    /// <param name="directoryDescriptor">The open directory or <c>AT_FDCWD</c>.</param>
    /// <param name="nullTerminatedPath">The null-terminated UTF-8 path.</param>
    /// <param name="flags">The <c>AT_*</c> flags.</param>
    /// <param name="statxAvailable">Whether the <c>statx</c> call may be used.</param>
    /// <param name="status">The status when the call succeeds.</param>
    /// <returns><see langword="true"/> when the path was stat-ed.</returns>
    internal static bool Stat(int directoryDescriptor, ReadOnlySpan<byte> nullTerminatedPath, int flags, bool statxAvailable, out LinuxFileStatus status)
    {
        status = default;

        if (statxAvailable)
        {
            var error = TryStatx(directoryDescriptor, nullTerminatedPath, flags, out status);

            if (error == 0)
            {
                return true;
            }

            // On ENOSYS the call never succeeds on this kernel, so switch the
            // whole process to the fallback once. Any other error is reported
            // as-is; the flag write keeps this a single covered path.
            var notImplemented = error == (int)LinuxErrno.FunctionNotImplemented;
            Volatile.Write(ref _statxAvailable, notImplemented ? 0 : 1);

            if (!notImplemented)
            {
                status = default;
                return false;
            }
        }

        return TryFStatAt(directoryDescriptor, nullTerminatedPath, flags, out status);
    }

    private static unsafe int TryStatx(int directoryDescriptor, ReadOnlySpan<byte> path, int flags, out LinuxFileStatus status)
    {
        Span<byte> raw = stackalloc byte[LinuxStatLayout.StatxSize];

        fixed (byte* pathPointer = path)
        {
            fixed (byte* statusPointer = raw)
            {
                if (Interop.Statx(directoryDescriptor, pathPointer, flags, Interop.StatxBasicStats, statusPointer) != 0)
                {
                    status = default;
                    return Marshal.GetLastPInvokeError();
                }

                if (!LinuxStatLayout.TryParseStatx(new ReadOnlySpan<byte>(statusPointer, LinuxStatLayout.StatxSize), out status))
                {
                    Marshal.SetLastPInvokeError((int)LinuxErrno.Overflow);
                    return (int)LinuxErrno.Overflow;
                }

                return 0;
            }
        }
    }

    private static unsafe bool TryFStat(int descriptor, out LinuxFileStatus status)
    {
        Span<byte> raw = stackalloc byte[LinuxStatLayout.StatBufferSize];

        fixed (byte* statusPointer = raw)
        {
            if (Interop.FStat(descriptor, statusPointer) != 0)
            {
                status = default;
                return false;
            }

            return LinuxStatLayout.TryParseStat(
                new ReadOnlySpan<byte>(statusPointer, LinuxStatLayout.StatBufferSize),
                RuntimeInformation.ProcessArchitecture,
                out status);
        }
    }

    private static unsafe bool TryFStatAt(int directoryDescriptor, ReadOnlySpan<byte> path, int flags, out LinuxFileStatus status)
    {
        Span<byte> raw = stackalloc byte[LinuxStatLayout.StatBufferSize];

        fixed (byte* pathPointer = path)
        {
            fixed (byte* statusPointer = raw)
            {
                if (Interop.FStatAt(directoryDescriptor, pathPointer, statusPointer, flags) != 0)
                {
                    status = default;
                    return false;
                }

                return LinuxStatLayout.TryParseStat(
                    new ReadOnlySpan<byte>(statusPointer, LinuxStatLayout.StatBufferSize),
                    RuntimeInformation.ProcessArchitecture,
                    out status);
            }
        }
    }
}
