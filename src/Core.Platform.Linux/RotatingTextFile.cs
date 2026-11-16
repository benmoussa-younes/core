// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.Versioning;

namespace Core.Platform.Linux;

/// <summary>
/// An append-only, size-rotated text file: lines are appended, and once the file
/// reaches the maximum size it is moved to a single <c>.1</c> companion so a log
/// never grows without bound.
/// </summary>
/// <remarks>
/// The file is owner-only (0600) and its directory is created on first write. The
/// rotation keeps <see cref="RetainedFiles"/> previous generations; a caller that
/// needs the timestamp or the level formats the line itself.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class RotatingTextFile : IDisposable
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly string _path;
    private readonly long _maxBytes;
    private int _retainedFiles = 1;
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="RotatingTextFile"/> class.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="maxBytes">The size at which the file rotates; must be positive.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> is not positive.</exception>
    public RotatingTextFile(string path, long maxBytes = 1_048_576)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        _path = path;
        _maxBytes = maxBytes;
    }

    /// <summary>Gets the file path.</summary>
    public string Path => _path;

    /// <summary>
    /// Gets how many rotated archives the file keeps; one by default. Set it in an
    /// object initializer, before the first write.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one.</exception>
    public int RetainedFiles
    {
        get => _retainedFiles;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _retainedFiles = value;
        }
    }

    /// <summary>Appends one line, rotating the file when it reaches its maximum size.</summary>
    /// <param name="line">The line to append.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The file has been disposed.</exception>
    public void WriteLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            EnsureWriter();
            _writer!.WriteLine(line);
            _writer.Flush();

            if (_writer.BaseStream.Position >= _maxBytes)
            {
                Rotate();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void EnsureWriter()
    {
        if (_writer is not null)
        {
            return;
        }

        if (System.IO.Path.GetDirectoryName(_path) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);

            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        // O_NOFOLLOW closes the link-check/open race: a path swapped for a symbolic
        // link between the check and the open can no longer redirect the write.
        if (!LinuxFileSystem.TryOpenAppendNoFollow(_path, OwnerOnly, out var handle) || handle is null)
        {
            throw new IOException($"The log path '{_path}' could not be opened for append.");
        }

        _writer = new StreamWriter(new FileStream(handle, FileAccess.Write));

        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(_path, OwnerOnly);
        }
    }

    private void Rotate()
    {
        _writer!.Dispose();
        _writer = null;

        var oldest = string.Concat(_path, ".", _retainedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = _retainedFiles - 1; index >= 1; index--)
        {
            var source = string.Concat(_path, ".", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (File.Exists(source))
            {
                File.Move(source, string.Concat(_path, ".", (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)), overwrite: true);
            }
        }

        File.Move(_path, string.Concat(_path, ".1"), overwrite: true);
    }
}
