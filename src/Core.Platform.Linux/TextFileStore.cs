// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.Versioning;
using System.Text;
using Core.IO.Files;
using Core.Primitives;
using Core.Results;
using Core.Text;

namespace Core.Platform.Linux;

/// <summary>
/// Reads and writes one UTF-8 text file atomically. The reader returns the text
/// normalized to line feeds with the file's original line ending and BOM, so the
/// writer can restore the file byte-identically; invalid UTF-8, a file over the
/// configured maximum and an unwritable destination are typed failures, never
/// exceptions.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class TextFileStore
{
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Initializes a new instance of the <see cref="TextFileStore"/> class.</summary>
    /// <param name="maximumBytes">The maximum file size read or written; must be positive.</param>
    /// <remarks>
    /// The default (256 MiB) is also a memory ceiling: a read allocates one buffer
    /// of the file size. A UI host that edits ordinary documents should pass a
    /// smaller value.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumBytes"/> is not positive.</exception>
    public TextFileStore(long maximumBytes = 268_435_456)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        MaximumBytes = maximumBytes;
    }

    /// <summary>Gets the maximum file size read or written.</summary>
    public long MaximumBytes { get; }

    /// <summary>Reads one text file, following a final symbolic link.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The document, or a failure with code <c>io.unreadable</c>,
    /// <c>text.too_large</c> or <c>text.invalid_utf8</c>.</returns>
    /// <remarks>An editor opens the target the user named; the write path refuses a
    /// symbolic-link destination. A caller that must not follow a link uses
    /// <see cref="ReadNoFollow"/>.</remarks>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty, whitespace, or too long to encode.</exception>
    public Result<TextDocument> Read(string path) => ReadCore<OperatingSystemFileChunkReader>(path, noFollow: false);

    /// <summary>Reads one text file, refusing a final symbolic link.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The document, or a typed failure.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty, whitespace, or too long to encode.</exception>
    public Result<TextDocument> ReadNoFollow(string path) => ReadCore<OperatingSystemFileChunkReader>(path, noFollow: true);

    internal Result<TextDocument> ReadCore<TChunkReader>(string path, bool noFollow = false)
        where TChunkReader : IFileChunkReader
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fileSystem = new LinuxFileSystemAdapter();
        var opened = noFollow
            ? FileReader.OpenNoFollow(fileSystem, path)
            : FileReader.Open(fileSystem, path);
        if (opened.TryGetError(out var error))
        {
            return Result<TextDocument>.Failure(error);
        }

        using var reader = opened.Value;
        var size = reader.Metadata.Size;

        if (size < 0 || size > MaximumBytes || size > int.MaxValue)
        {
            return Result<TextDocument>.Failure(TooLarge(path));
        }

        var bytes = new byte[(int)size];
        var offset = 0;

        while (offset < bytes.Length)
        {
            var read = TChunkReader.Read(reader.Handle, bytes.AsSpan(offset), offset);

            if (read <= 0)
            {
                return Result<TextDocument>.Failure(Unreadable(path));
            }

            offset += read;
        }

        var hasBom = bytes.Length >= 3 && bytes[0] == Bom[0] && bytes[1] == Bom[1] && bytes[2] == Bom[2];
        string text;

        try
        {
            text = StrictUtf8.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        }
        catch (DecoderFallbackException)
        {
            return Result<TextDocument>.Failure(InvalidUtf8(path));
        }

        var lineEnding = LineEndingDetector.Detect(text);

        return Result<TextDocument>.Success(new TextDocument(
            LineEndingConverter.ToLineFeed(text),
            lineEnding,
            hasBom));
    }

    /// <summary>Writes a document, restoring its line ending and BOM.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="document">The document.</param>
    /// <returns>The written path, or a failure.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty, whitespace, or too long to encode.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public Result<string> Write(string path, TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Write(path, document.Text, document.LineEnding, document.HasBom);
    }

    /// <summary>Writes text atomically with the requested line ending and BOM.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="text">The text; it is normalized to line feeds before the requested ending is applied.</param>
    /// <param name="lineEnding">The line ending to write.</param>
    /// <param name="writeBom">Whether to prefix a UTF-8 byte-order mark.</param>
    /// <returns>The written path, or a failure with code <c>text.too_large</c>,
    /// <c>text.unwritable</c> or the atomic writer's <c>io.*</c> code.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty, whitespace, or too long to encode.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public Result<string> Write(
        string path,
        string text,
        LineEnding lineEnding = LineEnding.LineFeed,
        bool writeBom = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);

        var fileText = LineEndingConverter.FromLineFeed(LineEndingConverter.ToLineFeed(text), lineEnding);
        byte[] content;

        try
        {
            content = StrictUtf8.GetBytes(fileText);
        }
        catch (EncoderFallbackException)
        {
            return Result<string>.Failure(InvalidUtf8(path));
        }

        if (content.Length + (writeBom ? Bom.Length : 0) > MaximumBytes)
        {
            return Result<string>.Failure(TooLarge(path));
        }

        try
        {
            using var writer = new AtomicFileWriter(path, FileMode);

            if (writeBom)
            {
                writer.Stream.Write(Bom);
            }

            writer.Stream.Write(content);
            var committed = writer.Commit();

            return committed.IsSuccess
                ? Result<string>.Success(path)
                : Result<string>.Failure(committed.Error);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return Result<string>.Failure(
                new CoreError(
                    CoreErrorCodes.TextUnwritable,
                    $"The text file '{PathRedaction.ForMessage(path)}' could not be written."));
        }
    }

    private static CoreError TooLarge(string path) =>
        new(CoreErrorCodes.TextTooLarge, $"The text file '{PathRedaction.ForMessage(path)}' is larger than the maximum size.");

    private static CoreError Unreadable(string path) =>
        new(CoreErrorCodes.IoUnreadable, $"The text file '{PathRedaction.ForMessage(path)}' could not be read.");

    private static CoreError InvalidUtf8(string path) =>
        new(CoreErrorCodes.TextInvalidUtf8, $"The text file '{PathRedaction.ForMessage(path)}' is not valid UTF-8.");
}
