// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Globalization;
using System.Runtime.Versioning;
using Core.Diagnostics;

namespace Core.Platform.Linux;

/// <summary>
/// The file-backed <see cref="IOperationJournal"/>: one timestamped operation per
/// line, an owner-only file that rotates at a size limit and keeps a bounded
/// number of archives. Recording is best effort, and every value is reduced to
/// one bounded line, so an operator message cannot forge an entry.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class RotatingOperationJournal : IOperationJournal, IDisposable
{
    private readonly RotatingTextFile _file;
    private readonly TimeProvider _timeProvider;
    private int _disposed;

    /// <summary>Initializes the journal.</summary>
    /// <param name="path">The current journal path.</param>
    /// <param name="maxBytes">The size at which the file rotates; must be positive.</param>
    /// <param name="retainedFiles">How many rotated archives to keep; must be positive.</param>
    /// <param name="timeProvider">The clock, for deterministic tests.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> or <paramref name="retainedFiles"/> is not positive.</exception>
    public RotatingOperationJournal(
        string path,
        long maxBytes = 1_048_576,
        int retainedFiles = 5,
        TimeProvider? timeProvider = null)
    {
        _file = new RotatingTextFile(path, maxBytes) { RetainedFiles = retainedFiles };
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the current journal path.</summary>
    public string Path => _file.Path;

    /// <inheritdoc />
    public void Record(string operation, string message)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(message);

        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var timestamp = _timeProvider
            .GetUtcNow()
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var line = string.Concat(
            timestamp,
            " [",
            LogRedaction.SingleLine(operation, 128),
            "] ",
            LogRedaction.SingleLine(message));

        try
        {
            _file.WriteLine(line);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // A journal is best effort; the operation itself has already succeeded
            // or failed for its own reason.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _file.Dispose();
    }
}
