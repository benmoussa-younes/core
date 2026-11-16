// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.InteropServices;
using Core.Primitives;
using Core.Results;

namespace Core.Platform.Linux;

/// <summary>
/// A read-only file mapping built directly on <c>mmap</c>/<c>munmap</c>, the raw
/// counterpart of <c>Core.IO.Mapped.MappedFile</c>: pages are faulted in on
/// demand and a slice is a zero-copy span, without the .NET mapping's safe-handle
/// objects.
/// </summary>
/// <remarks>
/// The mapping is private and copy-on-write, and the file descriptor is closed
/// once the mapping exists, because the mapping holds its own reference to the
/// file. An empty file maps to a zero-length mapping. The mapping is not
/// synchronised with a concurrent truncation of the file, nor with
/// <see cref="Dispose"/>: do not dispose while a caller still holds a
/// <see cref="Slice"/> span or a scan is in flight.
/// </remarks>
public sealed unsafe class LinuxMemoryMapping : IMemoryMapping
{
    private void* _address;
    private bool _disposed;

    private LinuxMemoryMapping(void* address, long length)
    {
        _address = address;
        Length = length;
    }

    /// <summary>
    /// Gets the length of the mapping in bytes.
    /// </summary>
    public long Length { get; }

    /// <summary>
    /// Opens a file as a read-only mapping.
    /// </summary>
    /// <param name="path">The file to map.</param>
    /// <returns>The mapping, or a failure with code <c>io.unreadable</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static Result<LinuxMemoryMapping> Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        // O_NONBLOCK prevents a FIFO from wedging open(2), and the no-follow
        // open refuses a final symbolic link; MapDescriptor then requires a
        // regular file before mmap.
        if (!LinuxFileSystem.TryOpenReadNoBlockNoFollow(path, out var handle) || handle is null)
        {
            return Result<LinuxMemoryMapping>.Failure(Unreadable(
                path,
                new IOException($"open failed with errno {(int)LinuxFileSystem.LastError}.")));
        }

        try
        {
            return MapDescriptor((int)handle.DangerousGetHandle(), path);
        }
        finally
        {
            handle.Dispose();
        }
    }

    /// <summary>
    /// Maps an open descriptor after reading its size. Exposed to the test
    /// assembly so the stat failure path can be exercised with a bad descriptor.
    /// </summary>
    internal static Result<LinuxMemoryMapping> MapDescriptor(int descriptor, string path)
    {
        if (!LinuxFileSystem.TryStatDescriptor(descriptor, out var status))
        {
            return Result<LinuxMemoryMapping>.Failure(Unreadable(
                path,
                new IOException($"stat failed with errno {(int)LinuxFileSystem.LastError}.")));
        }

        if (status.Type != LinuxFileType.RegularFile || status.Size < 0)
        {
            return Result<LinuxMemoryMapping>.Failure(Unreadable(
                path,
                new IOException("the object is not a regular file.")));
        }

        return TryMap(descriptor, status.Size, path);
    }

    /// <summary>
    /// Maps <paramref name="length"/> bytes of an open descriptor. Exposed to the
    /// test assembly so the <c>mmap</c> failure path can be exercised with a bad
    /// descriptor.
    /// </summary>
    internal static Result<LinuxMemoryMapping> TryMap(int descriptor, long length, string path)
    {
        if (length == 0)
        {
            return Result<LinuxMemoryMapping>.Success(new LinuxMemoryMapping(null, 0));
        }

        var address = Interop.MMap(
            null,
            (nuint)length,
            Interop.ProtectionRead,
            Interop.MapPrivate,
            descriptor,
            0);

        if (address == Interop.MapFailed)
        {
            return Result<LinuxMemoryMapping>.Failure(Unreadable(
                path,
                new IOException($"mmap failed with errno {Marshal.GetLastPInvokeError()}.")));
        }

        return Result<LinuxMemoryMapping>.Success(new LinuxMemoryMapping(address, length));
    }

    /// <summary>
    /// Gets a zero-copy view of <paramref name="length"/> bytes at
    /// <paramref name="offset"/>.
    /// </summary>
    /// <param name="offset">The byte offset into the mapping.</param>
    /// <param name="length">The number of bytes.</param>
    /// <returns>The bytes, valid while the mapping is alive.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The slice is not fully inside the mapping.</exception>
    /// <exception cref="ObjectDisposedException">The mapping has been disposed.</exception>
    public ReadOnlySpan<byte> Slice(long offset, int length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (offset < 0 || length < 0 || offset > Length - length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                $"The slice (offset {offset}, length {length}) is outside the {Length}-byte mapping.");
        }

        return length == 0 ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>((byte*)_address + offset, length);
    }

    /// <summary>
    /// Advises the kernel about the expected access pattern of the mapping.
    /// </summary>
    /// <param name="advice">The access-pattern advice.</param>
    /// <returns><see langword="true"/> when the advice was accepted; a zero-length mapping has nothing to advise and returns <see langword="true"/>.</returns>
    /// <exception cref="ObjectDisposedException">The mapping has been disposed.</exception>
    public bool Advise(LinuxMemoryAdvice advice)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Length == 0 || TryAdviseAddress(_address, (nuint)Length, (int)advice);
    }

    /// <summary>
    /// Advises an arbitrary address range. Exposed to the test assembly so the
    /// failure path can be exercised with an unbacked address.
    /// </summary>
    internal static bool TryAdviseAddress(void* address, nuint length, int advice)
    {
        return OperatingSystem.IsLinux() && Interop.MAdvise(address, length, advice) == 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_address is not null)
        {
            _ = Interop.MUnmap(_address, (nuint)Length);
            _address = null;
        }

        GC.SuppressFinalize(this);
    }

    private static CoreError Unreadable(string path, Exception exception)
    {
        return new CoreError("io.unreadable", $"The file could not be mapped: '{PathRedaction.ForMessage(path)}': {exception.Message}");
    }
}
