// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform;
using Core.Results;

namespace Core.IO.Mapped;

/// <summary>
/// A read-only, memory-mapped view of a file, opened without reading it into
/// memory: the pages are faulted in on demand, so a large file costs address
/// space rather than resident memory, and a slice is a zero-copy span.
/// </summary>
/// <remarks>
/// This complements <c>Core.IO.Files.FileContent</c>, which reads a whole file
/// eagerly into a pinned buffer: <see cref="MappedFile"/> is for random access
/// into a file too large to hold. An empty file is valid and yields empty
/// slices. The mapping is read-only and shared; a writer replacing the file does
/// not change the pages already mapped. The default is the portable BCL
/// mapping; pass a platform factory (for example <c>LinuxMemoryMappingFactory</c>)
/// to use the measured raw <c>mmap</c> path. Disposal is not synchronized with
/// <c>Slice</c> or a scan: do not dispose while a caller still holds a span or a
/// parallel scan is in flight.
/// </remarks>
public sealed class MappedFile : IDisposable
{
    private readonly IMemoryMapping _mapping;
    private readonly ReaderWriterLockSlim _lifetime = new(LockRecursionPolicy.SupportsRecursion);
    private bool _disposed;

    private MappedFile(IMemoryMapping mapping)
    {
        _mapping = mapping;
    }

    /// <summary>Gets the length of the mapped file in bytes.</summary>
    public long Length => _mapping.Length;

    /// <summary>
    /// Opens a file as a read-only memory mapping with the portable BCL factory.
    /// </summary>
    /// <param name="path">The file to map.</param>
    /// <returns>The mapping, or a failure with code <c>io.unreadable</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    public static Result<MappedFile> Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Open(path, PortableMemoryMappingFactory.Instance);
    }

    /// <summary>
    /// Opens a file as a read-only memory mapping with an explicit platform factory.
    /// </summary>
    /// <param name="path">The file to map.</param>
    /// <param name="factory">The mapping factory, for example <c>LinuxMemoryMappingFactory</c>.</param>
    /// <returns>The mapping, or the factory's typed failure.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public static Result<MappedFile> Open(string path, IMemoryMappingFactory factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(factory);

        var opened = factory.Open(path);

        if (opened.TryGetError(out var error))
        {
            return Result<MappedFile>.Failure(error);
        }

        return Result<MappedFile>.Success(new MappedFile(opened.Value));
    }

    /// <summary>
    /// Gets a zero-copy view of <paramref name="length"/> bytes at
    /// <paramref name="offset"/>.
    /// </summary>
    /// <param name="offset">The byte offset into the file.</param>
    /// <param name="length">The number of bytes.</param>
    /// <returns>The bytes, valid while the mapping is alive.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The slice is not fully inside the file.</exception>
    /// <exception cref="ObjectDisposedException">The mapping has been disposed.</exception>
    public ReadOnlySpan<byte> Slice(long offset, int length)
    {
        _lifetime.EnterReadLock();

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _mapping.Slice(offset, length);
        }
        finally
        {
            _lifetime.ExitReadLock();
        }
    }

    /// <summary>
    /// Holds the mapping alive for the duration of a scan. The returned lease
    /// must be disposed before the caller disposes the mapping.
    /// </summary>
    internal IDisposable Lease()
    {
        _lifetime.EnterReadLock();

        if (_disposed)
        {
            _lifetime.ExitReadLock();
            throw new ObjectDisposedException(nameof(MappedFile));
        }

        return new LifetimeLease(this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lifetime.EnterWriteLock();

        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _mapping.Dispose();
        }
        finally
        {
            _lifetime.ExitWriteLock();
        }

        GC.SuppressFinalize(this);
    }

    private sealed class LifetimeLease(MappedFile owner) : IDisposable
    {
        private MappedFile? _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);

            owner?._lifetime.ExitReadLock();
        }
    }
}
