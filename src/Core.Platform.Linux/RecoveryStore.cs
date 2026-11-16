// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.Versioning;
using Core.Primitives;
using Core.Results;
using Core.Text;

namespace Core.Platform.Linux;

/// <summary>
/// Stores an unsaved document's recovery text beside the document, through the
/// atomic <see cref="TextFileStore"/>, so a crash never leaves a partial recovery.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class RecoveryStore
{
    /// <summary>The suffix appended to the document path.</summary>
    public const string Suffix = ".recovery";

    private readonly TextFileStore _store;

    /// <summary>Initializes a new instance of the <see cref="RecoveryStore"/> class.</summary>
    /// <param name="store">The text store, or <see langword="null"/> for the default one.</param>
    public RecoveryStore(TextFileStore? store = null)
    {
        _store = store ?? new TextFileStore();
    }

    /// <summary>Resolves the sibling recovery path of a document.</summary>
    /// <param name="documentPath">The document path.</param>
    /// <returns>The recovery path.</returns>
    /// <exception cref="ArgumentException"><paramref name="documentPath"/> is empty or whitespace.</exception>
    public static string PathFor(string documentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);

        return string.Concat(documentPath, Suffix);
    }

    /// <summary>Loads the recovery text, or <see langword="null"/> when none exists.</summary>
    /// <param name="documentPath">The document path.</param>
    /// <returns>The recovery document, or <see langword="null"/>.</returns>
    public Result<TextDocument?> Load(string documentPath)
    {
        var path = PathFor(documentPath);

        if (!File.Exists(path))
        {
            return Result<TextDocument?>.Success(null);
        }

        var read = _store.ReadNoFollow(path);

        return read.TryGetValue(out var document)
            ? Result<TextDocument?>.Success(document)
            : Result<TextDocument?>.Failure(read.Error);
    }

    /// <summary>Writes the recovery text atomically.</summary>
    /// <param name="documentPath">The document path.</param>
    /// <param name="document">The recovery document.</param>
    /// <returns>The recovery path, or a failure.</returns>
    public Result<string> Save(string documentPath, TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return _store.Write(PathFor(documentPath), document);
    }

    /// <summary>Removes the recovery file; a missing file is a success.</summary>
    /// <param name="documentPath">The document path.</param>
    /// <returns>Whether the file is gone.</returns>
    public static Result<bool> Clear(string documentPath)
    {
        var path = PathFor(documentPath);

        try
        {
            File.Delete(path);
            return Result<bool>.Success(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<bool>.Failure(new CoreError(
                CoreErrorCodes.TextUnwritable,
                $"The recovery file '{PathRedaction.ForMessage(path)}' could not be removed: {exception.Message}"));
        }
    }
}
