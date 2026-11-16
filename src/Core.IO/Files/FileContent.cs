// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform;
using Microsoft.Win32.SafeHandles;
using Core.Results;


namespace Core.IO.Files;

/// <summary>
/// Reads a file into a caller-supplied buffer, streaming each chunk to a sink.
/// </summary>
public static class FileContent
{
    /// <summary>
    /// Reads a file through an explicit platform file system.
    /// </summary>
    /// <param name="fileSystem">The platform file system.</param>
    /// <param name="path">The file path.</param>
    /// <param name="readBuffer">The caller-owned read buffer; must not be empty.</param>
    /// <param name="sink">Receives each chunk read.</param>
    /// <returns>The pinned metadata, or an error when the path cannot be read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileSystem"/>, <paramref name="readBuffer"/> or <paramref name="sink"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="readBuffer"/> is empty.</exception>
    public static Result<FileMetadata> Read(IFileSystem fileSystem, string path, byte[] readBuffer, ByteSpanSink sink)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(readBuffer);
        ArgumentNullException.ThrowIfNull(sink);

        if (readBuffer.Length == 0)
        {
            throw new ArgumentException("The read buffer must not be empty.", nameof(readBuffer));
        }

        // Open first, then fstat the pinned descriptor: the public read must not
        // stat-then-open (a swap to a FIFO would block) and must refuse anything
        // that is not a regular file, including a zero-size FIFO.
        var opened = FileReader.Open(fileSystem, path, FileIdentity.Unknown);

        if (opened.IsFailure)
        {
            return Result<FileMetadata>.Failure(opened.Error);
        }

        using var reader = opened.Value;
        var metadata = reader.Metadata;

        if (metadata.Size == 0)
        {
            return Result<FileMetadata>.Success(metadata);
        }

        return Stream<OperatingSystemFileChunkReader>(path, reader, readBuffer, sink, metadata);
    }

    /// <summary>
    /// Reads a file through an explicit platform file system and verifies the identity.
    /// </summary>
    /// <param name="fileSystem">The platform file system.</param>
    /// <param name="path">The file path.</param>
    /// <param name="readBuffer">The caller-owned read buffer; must not be empty.</param>
    /// <param name="sink">Receives each chunk read.</param>
    /// <param name="expected">The identity the walk captured; unknown skips the check.</param>
    /// <returns>The pinned metadata, or an error when the path cannot be read or was swapped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileSystem"/>, <paramref name="readBuffer"/> or <paramref name="sink"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="readBuffer"/> is empty.</exception>
    public static Result<FileMetadata> Read(
        IFileSystem fileSystem,
        string path,
        byte[] readBuffer,
        ByteSpanSink sink,
        in FileIdentity expected)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(readBuffer);
        ArgumentNullException.ThrowIfNull(sink);

        if (readBuffer.Length == 0)
        {
            throw new ArgumentException("The read buffer must not be empty.", nameof(readBuffer));
        }

        var opened = FileReader.Open(fileSystem, path, expected);

        if (opened.IsFailure)
        {
            return Result<FileMetadata>.Failure(opened.Error);
        }

        using var reader = opened.Value;
        var metadata = reader.Metadata;

        if (metadata.Size == 0)
        {
            return Result<FileMetadata>.Success(metadata);
        }

        return Stream<OperatingSystemFileChunkReader>(path, reader, readBuffer, sink, metadata);
    }

    /// <summary>
    /// Reads a file whose status was already probed, with the chunk reader
    /// supplied by the caller.
    /// </summary>
    /// <remarks>
    /// The chunk reader is a generic type parameter so the JIT devirtualises the
    /// call on the shipped path; the parameter exists so a test can inject a
    /// failing <c>pread</c> (EPERM/EIO) without a kernel fixture.
    /// </remarks>
    /// <typeparam name="TChunkReader">The read operation to perform.</typeparam>
    /// <param name="fileSystem">The platform file system.</param>
    /// <param name="path">The file path.</param>
    /// <param name="readBuffer">The caller-owned read buffer; must not be empty.</param>
    /// <param name="sink">Receives each chunk read.</param>
    /// <param name="status">The already-probed status.</param>
    /// <returns>The pinned metadata, or an error when the path cannot be read.</returns>
    internal static Result<FileMetadata> Read<TChunkReader>(
        IFileSystem fileSystem,
        string path,
        byte[] readBuffer,
        ByteSpanSink sink,
        in FileStatus status)
        where TChunkReader : IFileChunkReader
    {
        var metadata = new FileMetadata(status.Size, status.MtimeNanoseconds, status.Device, status.Inode, status.Type);

        // A directory is never readable content, whatever its reported size:
        // some file systems report st_size == 0 for a directory.
        if (status.Type == FileType.Directory)
        {
            return Result<FileMetadata>.Failure(
                new CoreError("io.unreadable", $"Unable to read '{path}'."));
        }

        if (status.Size == 0)
        {
            return Result<FileMetadata>.Success(metadata);
        }

        var opened = FileReader.Open(fileSystem, path, status);

        if (opened.IsFailure)
        {
            return Result<FileMetadata>.Failure(opened.Error);
        }

        using var reader = opened.Value;

        return Stream<TChunkReader>(path, reader, readBuffer, sink, metadata);
    }


    private static Result<FileMetadata> Stream<TChunkReader>(
        string path,
        FileReader reader,
        byte[] readBuffer,
        ByteSpanSink sink,
        FileMetadata metadata)
        where TChunkReader : IFileChunkReader
    {
        long offset = 0;

        while (offset < metadata.Size)
        {
            var count = (int)Math.Min(readBuffer.Length, metadata.Size - offset);
            int read;

            try
            {
                read = TChunkReader.Read(reader.Handle, readBuffer.AsSpan(0, count), offset);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A special file (a sysfs attribute, for example) may pass the
                // stat and access prefilter and open, then fail the read itself
                // with EPERM, ENODATA or EIO. That is an unreadable input, not a
                // crash and not an output failure.
                return Result<FileMetadata>.Failure(
                    new CoreError("io.unreadable", $"Unable to read '{path}'."));
            }

            // The read must produce the full pinned chunk. A short read (a
            // zero return before the size is reached, or fewer bytes than the
            // pinned chunk) means the pinned content cannot be produced in
            // full: the file shrank, or a special file returned less than it
            // promised. A truncated hash is never success.
            if (read < count)
            {
                return Result<FileMetadata>.Failure(
                    new CoreError("io.unreadable", $"Unable to read '{path}'."));
            }

            sink(readBuffer.AsSpan(0, read));
            offset += read;
        }

        return Result<FileMetadata>.Success(metadata);
    }
}

/// <summary>
/// The chunk read operation used by <see cref="FileContent"/>.
/// </summary>
internal interface IFileChunkReader
{
    /// <summary>
    /// Reads up to <paramref name="buffer"/>.Length bytes at
    /// <paramref name="offset"/> from an open read handle.
    /// </summary>
    /// <param name="handle">The open read handle.</param>
    /// <param name="buffer">The destination buffer.</param>
    /// <param name="offset">The byte offset to read from.</param>
    /// <returns>The number of bytes read, or zero at end of file.</returns>
    static abstract int Read(SafeFileHandle handle, Span<byte> buffer, long offset);
}

/// <summary>
/// The production <see cref="IFileChunkReader"/>, backed by
/// <see cref="RandomAccess.Read(SafeFileHandle, Span{byte}, long)"/>.
/// </summary>
internal readonly struct OperatingSystemFileChunkReader : IFileChunkReader
{
    /// <inheritdoc />
    public static int Read(SafeFileHandle handle, Span<byte> buffer, long offset)
    {
        return RandomAccess.Read(handle, buffer, offset);
    }
}
