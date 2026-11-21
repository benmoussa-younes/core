// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Globalization;
using System.Runtime.Versioning;
using Core.Diagnostics;
using Core.Primitives;
using Core.Security;

namespace Core.Platform.Linux;

/// <summary>
/// Persists a last-chance exception to an owner-only, size-rotated file: a header
/// with the UTC timestamp and an optional context, then up to a bounded number of
/// stack lines. Writing is best effort and never throws, so the process can still
/// terminate cleanly.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class CrashLog : ICrashLog
{
    private readonly RotatingTextFile _file;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, string> _redact;
    private readonly int _maximumLines;
    private int _disposed;

    /// <summary>Initializes the crash log.</summary>
    /// <param name="path">The current crash file path.</param>
    /// <param name="maxBytes">The size at which the file rotates; must be positive.</param>
    /// <param name="retainedFiles">How many rotated archives to keep; must be positive.</param>
    /// <param name="maximumLines">How many stack lines to write per crash; must be positive.</param>
    /// <param name="timeProvider">The clock, for deterministic tests.</param>
    /// <param name="redact">
    /// The per-line redaction, or <see langword="null"/> to redact paths below the
    /// user's home with <see cref="PathRedaction"/>.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A numeric argument is not positive.</exception>
    public CrashLog(
        string path,
        long maxBytes = 1_048_576,
        int retainedFiles = 5,
        int maximumLines = 200,
        TimeProvider? timeProvider = null,
        Func<string, string>? redact = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLines, 1);

        _file = new RotatingTextFile(path, maxBytes) { RetainedFiles = retainedFiles };
        _timeProvider = timeProvider ?? TimeProvider.System;
        _redact = redact ?? (static line => PathRedaction.Scrub(line));
        _maximumLines = maximumLines;
    }

    /// <summary>Gets the current crash file path.</summary>
    public string Path => _file.Path;

    /// <summary>Writes one crash entry.</summary>
    /// <param name="exception">The exception.</param>
    /// <param name="context">An optional context, for example the operation that was running.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public void Write(Exception exception, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var timestamp = _timeProvider
            .GetUtcNow()
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        try
        {
            _file.WriteLine(context is null
                ? string.Concat(timestamp, " [crash]")
                : string.Concat(timestamp, " [crash] ", _redact(SecretRedaction.Scrub(LogRedaction.SingleLine(context, 256)))));

            var lines = exception.ToString().Split('\n');
            var written = 0;

            foreach (var line in lines)
            {
                if (written == _maximumLines)
                {
                    break;
                }

                _file.WriteLine(_redact(SecretRedaction.Scrub(LogRedaction.SingleLine(line.TrimEnd('\r'), 2_048))));
                written++;
            }
        }
        catch (Exception failure) when (
            failure is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // A crash log must never throw while the process is already failing.
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
