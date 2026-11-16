// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.IO.MemoryMappedFiles;
using Core.Platform;
using Core.Primitives;
using Core.Results;

namespace Core.IO.Mapped;

/// <summary>
/// The portable <see cref="IMemoryMapping"/> over the BCL
/// <see cref="MemoryMappedFile"/>; the default when no platform factory is
/// supplied.
/// </summary>
internal sealed unsafe class PortableMemoryMapping : IMemoryMapping
{
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _accessor;
    private byte* _pointer;
    private bool _disposed;

    private PortableMemoryMapping(long length)
    {
        Length = length;
    }

    public long Length { get; }

    internal static Result<PortableMemoryMapping> Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        MemoryMappedFile? mapping = null;
        MemoryMappedViewAccessor? accessor = null;

        try
        {
            var length = new FileInfo(path).Length;

            if (length == 0)
            {
                return Result<PortableMemoryMapping>.Success(new PortableMemoryMapping(0));
            }

            mapping = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            accessor = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            var file = new PortableMemoryMapping(accessor.Capacity);
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref file._pointer);
            file._pointer += accessor.PointerOffset;
            file._mapping = mapping;
            file._accessor = accessor;

            return Result<PortableMemoryMapping>.Success(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            accessor?.Dispose();
            mapping?.Dispose();

            return Result<PortableMemoryMapping>.Failure(Unreadable(path));
        }
    }

    public ReadOnlySpan<byte> Slice(long offset, int length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (offset < 0 || length < 0 || offset > Length - length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                $"The slice (offset {offset}, length {length}) is outside the {Length}-byte file.");
        }

        return length == 0 ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>(_pointer + offset, length);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_accessor is not null && _pointer is not null)
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _pointer = null;
        }

        _accessor?.Dispose();
        _mapping?.Dispose();
        GC.SuppressFinalize(this);
    }

    private static CoreError Unreadable(string path)
    {
        return new CoreError("io.unreadable", $"The file could not be mapped: '{PathRedaction.ForMessage(path)}'.");
    }
}

/// <summary>
/// The default factory: the portable BCL mapping.
/// </summary>
internal sealed class PortableMemoryMappingFactory : IMemoryMappingFactory
{
    internal static PortableMemoryMappingFactory Instance { get; } = new();

    public Result<IMemoryMapping> Open(string path)
    {
        var opened = PortableMemoryMapping.Open(path);

        if (opened.TryGetError(out var error))
        {
            return Result<IMemoryMapping>.Failure(error);
        }

        return Result<IMemoryMapping>.Success(opened.Value);
    }
}
