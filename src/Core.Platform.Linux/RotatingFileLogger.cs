// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Globalization;
using System.Runtime.Versioning;
using Core.Diagnostics;

namespace Core.Platform.Linux;

/// <summary>
/// The file-backed <see cref="IApplicationLogger"/>: one timestamped, single-line
/// entry per call, in an owner-only file that rotates at a size limit and keeps a
/// bounded number of archives.
/// </summary>
/// <remarks>
/// Logging is best effort: a full or read-only disk is not fatal, so an IO failure
/// is swallowed. The line is passed through <see cref="LogRedaction.SingleLine"/>,
/// so an untrusted message cannot inject a carriage return or a line feed and
/// cannot grow a line without bound.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class RotatingFileLogger : IApplicationLogger, IDisposable
{
    private readonly RotatingTextFile _file;
    private readonly TimeProvider _timeProvider;
    private int _disposed;

    /// <summary>Initializes a new instance of the <see cref="RotatingFileLogger"/> class.</summary>
    /// <param name="path">The current log file path.</param>
    /// <param name="maxBytes">The size at which the file rotates; must be positive.</param>
    /// <param name="retainedFiles">How many rotated archives to keep; must be positive.</param>
    /// <param name="timeProvider">The clock, for deterministic tests.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> or <paramref name="retainedFiles"/> is not positive.</exception>
    public RotatingFileLogger(
        string path,
        long maxBytes = 1_048_576,
        int retainedFiles = 5,
        TimeProvider? timeProvider = null)
    {
        _file = new RotatingTextFile(path, maxBytes) { RetainedFiles = retainedFiles };
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the current log file path.</summary>
    public string Path => _file.Path;

    /// <summary>Gets how many rotated archives the logger keeps.</summary>
    public int RetainedFiles => _file.RetainedFiles;

    /// <inheritdoc />
    public void Log(LogLevel level, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var timestamp = _timeProvider
            .GetUtcNow()
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var line = string.Concat(timestamp, " [", level.ToString(), "] ", LogRedaction.SingleLine(message));

        try
        {
            _file.WriteLine(line);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // A logger must never take the application down, including a concurrent
            // dispose that wins the race with this write.
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
