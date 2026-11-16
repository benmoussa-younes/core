// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers;
using System.Text.Json.Serialization.Metadata;
using Core.IO.Files;
using Core.Platform;
using Core.Results;
using Core.Serialization.Codecs;
using Core.Serialization.Documents;
using Core.Serialization.Mapping;
using Microsoft.Win32.SafeHandles;

namespace Core.Serialization.Storage;

/// <summary>
/// Reads and writes one <see cref="DataDocument"/> per name below a directory,
/// choosing the format from the file extension and replacing the file atomically.
/// </summary>
/// <remarks>
/// The name must be a single path component. A write goes through the platform's
/// <see cref="IAtomicFileWriter"/>, so the file holds either the previous complete
/// document or the new one, never a prefix; the directory and the files are
/// owner-only (0700/0600) when the factory applies that policy. JSON, CSV and INI
/// are implemented; TOML is recognized by extension but the codec was dropped, so
/// it reports <c>serialization.unsupported_format</c>.
/// </remarks>
public sealed class DataFileStore
{
    private readonly string _directory;
    private readonly Func<SafeFileHandle, Memory<byte>, long, int> _readChunk;
    private readonly IAtomicFileWriterFactory _atomicFiles;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataFileStore"/> class.
    /// </summary>
    /// <param name="directory">The directory the documents live in, created on the first save.</param>
    /// <param name="atomicFiles">The platform atomic-writer factory.</param>
    /// <param name="fileSystem">The platform file system.</param>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="atomicFiles"/> is <see langword="null"/>.</exception>
    public DataFileStore(string directory, IAtomicFileWriterFactory atomicFiles, IFileSystem fileSystem)
        : this(directory, static (handle, buffer, offset) => RandomAccess.Read(handle, buffer.Span, offset), atomicFiles, fileSystem)
    {
    }

    internal DataFileStore(
        string directory,
        Func<SafeFileHandle, Memory<byte>, long, int> readChunk,
        IAtomicFileWriterFactory atomicFiles,
        IFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(readChunk);
        ArgumentNullException.ThrowIfNull(atomicFiles);
        ArgumentNullException.ThrowIfNull(fileSystem);

        _directory = directory;
        _readChunk = readChunk;
        _atomicFiles = atomicFiles;
        _fileSystem = fileSystem;
    }

    /// <summary>The default maximum file size a document load will read.</summary>
    public const long DefaultMaxFileBytes = 268_435_456;

    /// <summary>Gets the directory the documents live in.</summary>
    public string Directory => _directory;

    /// <summary>
    /// Gets the maximum file size a load will read; a larger file fails with
    /// <c>serialization.too_large</c> rather than allocating without bound.
    /// </summary>
    public long MaxFileBytes { get; init; } = DefaultMaxFileBytes;

    /// <summary>Resolves the format from a file name's extension.</summary>
    /// <param name="name">The file name.</param>
    /// <returns>The format, or <see langword="null"/> when the extension is unknown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static DataFormat? FormatOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".json" => DataFormat.Json,
            ".csv" => DataFormat.Csv,
            ".ini" => DataFormat.Ini,
            ".toml" => DataFormat.Toml,
            _ => null,
        };
    }

    /// <summary>Gets the canonical file extension of a format.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The extension, with the leading dot.</returns>
    public static string ExtensionOf(DataFormat format)
    {
        string extension = string.Empty;

        switch (format)
        {
            case DataFormat.Json:
                extension = ".json";
                break;
            case DataFormat.Csv:
                extension = ".csv";
                break;
            case DataFormat.Ini:
                extension = ".ini";
                break;
            case DataFormat.Toml:
                extension = ".toml";
                break;
        }

        return extension;
    }

    /// <summary>
    /// Loads and parses a document, choosing the format from the extension unless
    /// one is given.
    /// </summary>
    /// <param name="name">The file name, a single path component.</param>
    /// <param name="format">The format, or <see langword="null"/> to infer it from the extension.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>The document, or a failure with code <c>serialization.unsupported_format</c>,
    /// <c>serialization.missing</c>, <c>serialization.unreadable</c>,
    /// <c>serialization.syntax</c>, <c>serialization.uneven_row</c> or
    /// <c>serialization.invalid_document</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a single path component.</exception>
    public ValueTask<Result<DataDocument>> LoadAsync(
        string name,
        DataFormat? format = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = DataFilePath.Combine(_directory, name);

        if (Resolve(name, format) is not { } resolved)
        {
            return ValueTask.FromResult(Result<DataDocument>.Failure(UnsupportedFormat(name)));
        }

        if (!DataCodec.IsSupported(resolved))
        {
            return ValueTask.FromResult(Result<DataDocument>.Failure(UnsupportedCodec(resolved)));
        }

        // OpenNoFollow both pins the descriptor (no stat/open TOCTOU) and refuses a
        // link placed in the store, so a state file cannot be redirected outside it.
        var opened = FileReader.OpenNoFollow(_fileSystem, path);

        if (!opened.TryGetValue(out var reader))
        {
            return ValueTask.FromResult(Result<DataDocument>.Failure(
                ClassifyReadFailure(name, path)));
        }

        using (reader)
        {
            var size = reader.Metadata.Size;

            if (size < 0 || size > MaxFileBytes || size > int.MaxValue)
            {
                // The buffer is an int-indexed array, so a caller that raised the
                // maximum past int.MaxValue must still get a typed failure, not an
                // overflowing cast.
                return ValueTask.FromResult(Result<DataDocument>.Failure(TooLarge(name)));
            }

            var bytes = new byte[(int)size];
            var offset = 0;

            while (offset < bytes.Length)
            {
                var read = _readChunk(reader.Handle, bytes.AsMemory(offset), offset);

                if (read <= 0)
                {
                    return ValueTask.FromResult(Result<DataDocument>.Failure(Unreadable(name)));
                }

                offset += read;
            }

            return ValueTask.FromResult(DataCodec.Read(resolved, bytes));
        }
    }

    private static CoreError ClassifyReadFailure(string name, string path)
    {
        if (System.IO.Directory.Exists(path))
        {
            return Unreadable(name);
        }

        return File.Exists(path)
            ? Unreadable(name)
            : new CoreError("serialization.missing", $"The data file '{name}' does not exist.");
    }

    private static CoreError Unreadable(string name) =>
        new("serialization.unreadable", $"The data file '{name}' could not be read.");

    /// <summary>
    /// Saves a document, choosing the format from the extension unless one is
    /// given, creating the directory and replacing the file atomically.
    /// </summary>
    /// <param name="name">The file name, a single path component.</param>
    /// <param name="document">The document to write.</param>
    /// <param name="format">The format, or <see langword="null"/> to infer it from the extension.</param>
    /// <param name="cancellationToken">The token used to cancel the write.</param>
    /// <returns>A successful result whose value is always <see langword="true"/>,
    /// or a failure with code <c>serialization.unsupported_format</c>,
    /// <c>serialization.invalid_document</c> or <c>serialization.unwritable</c>
    /// (the atomic writer's <c>io.*</c> code, for example
    /// <c>io.symbolic_link_target</c>, is propagated unchanged).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a single path component.</exception>
    public async ValueTask<Result<bool>> SaveAsync(
        string name,
        DataDocument document,
        DataFormat? format = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var path = DataFilePath.Combine(_directory, name);

        if (Resolve(name, format) is not { } resolved)
        {
            return Result<bool>.Failure(UnsupportedFormat(name));
        }

        if (!DataCodec.IsSupported(resolved))
        {
            return Result<bool>.Failure(UnsupportedCodec(resolved));
        }

        var buffer = new ArrayBufferWriter<byte>();
        var serialized = DataCodec.Write(resolved, document, buffer);

        if (serialized.IsFailure)
        {
            return Result<bool>.Failure(serialized.Error);
        }

        try
        {
            _atomicFiles.EnsureDirectory(_directory, ownerOnly: true);

            var created = _atomicFiles.Create(path, new AtomicFileOptions { OwnerOnly = true });

            if (created.TryGetError(out var writerError))
            {
                return Result<bool>.Failure(new CoreError(
                    "serialization.unwritable",
                    $"The data file '{name}' could not be written."));
            }

            using var writer = created.Value;
            await writer.Stream.WriteAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
            var committed = writer.Commit();

            return committed.IsSuccess
                ? Result<bool>.Success(true)
                : Result<bool>.Failure(committed.Error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return Result<bool>.Failure(
                new CoreError("serialization.unwritable", $"The data file '{name}' could not be written."));
        }
    }

    /// <summary>
    /// Loads a document and maps it to a typed value.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="name">The file name, a single path component.</param>
    /// <param name="typeInfo">The source-generated type information; must not be <see langword="null"/>.</param>
    /// <param name="format">The format, or <see langword="null"/> to infer it from the extension.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>The value, or a failure from <see cref="LoadAsync(string, DataFormat?, CancellationToken)"/>
    /// or <c>serialization.invalid_document</c> when the document does not map.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a single path component.</exception>
    public async ValueTask<Result<T>> LoadValueAsync<T>(
        string name,
        JsonTypeInfo<T> typeInfo,
        DataFormat? format = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        var loaded = await LoadAsync(name, format, cancellationToken).ConfigureAwait(false);

        return loaded.IsSuccess
            ? DataDocumentMapper.FromDocument(loaded.Value, typeInfo)
            : Result<T>.Failure(loaded.Error);
    }

    /// <summary>
    /// Maps a typed value to a document and saves it.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="name">The file name, a single path component.</param>
    /// <param name="value">The value to map and write.</param>
    /// <param name="typeInfo">The source-generated type information; must not be <see langword="null"/>.</param>
    /// <param name="format">The format, or <see langword="null"/> to infer it from the extension.</param>
    /// <param name="cancellationToken">The token used to cancel the write.</param>
    /// <returns>A successful result whose value is always <see langword="true"/>,
    /// or a failure from the mapping or <see cref="SaveAsync(string, DataDocument, DataFormat?, CancellationToken)"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a single path component.</exception>
    public async ValueTask<Result<bool>> SaveValueAsync<T>(
        string name,
        T value,
        JsonTypeInfo<T> typeInfo,
        DataFormat? format = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        if (value is null)
        {
            // A null reference serializes to JSON "null", which cannot be read
            // back into T: a save that can never be loaded.
            return Result<bool>.Failure(
                new CoreError("serialization.invalid_document", "A null value has no document form and cannot round-trip."));
        }

        // A source-generated type always serializes to valid JSON, so the read
        // back into a document cannot fail; a bad type is a programmer error.
        var document = DataDocumentMapper.ToDocument(value, typeInfo).Value;

        return await SaveAsync(name, document, format, cancellationToken).ConfigureAwait(false);
    }

    private static DataFormat? Resolve(string name, DataFormat? format) => format ?? FormatOf(name);

    private static CoreError TooLarge(string name) =>
        new("serialization.too_large", $"The data file '{name}' is larger than the maximum size.");

    private static CoreError UnsupportedFormat(string name) =>
        new("serialization.unsupported_format", $"The file '{name}' has no known data format; pass one explicitly.");

    private static CoreError UnsupportedCodec(DataFormat format) =>
        new("serialization.unsupported_format", $"The {format} codec is not implemented yet.");
}
