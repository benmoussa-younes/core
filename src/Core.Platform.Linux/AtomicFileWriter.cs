// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Diagnostics.CodeAnalysis;
using Core.Primitives;
using Core.Results;


namespace Core.Platform.Linux;

/// <summary>
/// Writes a named artefact atomically: the bytes go to a temporary file in the
/// target directory and become visible at the target path only when
/// <see cref="Commit"/> renames them over it.
/// </summary>
/// <remarks>
/// A named artefact is therefore either the previous complete content or the
/// new complete content, never a prefix. That is the guarantee SIGKILL cannot
/// break, because no handler runs for it: the process may leave a stale
/// <c>.&lt;target&gt;-*.tmp</c> file behind, which is recognisable by name and is
/// never cleaned by a scan.
/// <para>
/// The sequence is the one durability requires: flush the stream, <c>fsync</c>
/// the file, <c>rename</c> it over the target, then <c>fsync</c> the directory
/// so the rename itself is on stable storage. The cost of that durability is
/// measured, not asserted away.
/// </para>
/// <para>
/// Committing over an existing regular file narrows it to the bits the requested
/// mode and the destination mode share: the mode is read from the destination,
/// intersected with the constructor's mode and applied to the temporary file
/// before the rename, so replacing an artefact never widens its access and an
/// owner-only request repairs an over-permissive file. A destination that does
/// not exist is created with the mode passed to the constructor, as the kernel
/// applies the process umask. A destination that is a symbolic link is refused
/// as a failure value, because the rename would replace the link itself rather
/// than write through it; the check runs before the rename and the window it
/// leaves is stated on <see cref="Commit"/>.
/// </para>
/// </remarks>
public sealed class AtomicFileWriter : IAtomicFileWriter
{
    private readonly string _targetPath;
    private readonly string _directoryPath;
    private readonly string _temporaryPath;
    private readonly UnixFileMode _mode;
    private readonly FileStream _stream;
    private bool _finished;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AtomicFileWriter"/> class
    /// and creates the temporary file in the target's directory.
    /// </summary>
    /// <param name="targetPath">The path the committed content appears at.</param>
    /// <param name="mode">
    /// The permission bits of the temporary file when the destination does not
    /// exist yet, as the kernel applies the process umask. An existing regular
    /// file keeps only the bits the two modes share, so a commit never widens
    /// its access and a narrower request is still honoured.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="targetPath"/> is empty or whitespace.</exception>
    /// <exception cref="IOException">The temporary file cannot be created.</exception>
    /// <exception cref="UnauthorizedAccessException">The target directory is not writable.</exception>
    public AtomicFileWriter(string targetPath, UnixFileMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        _mode = mode;
        _targetPath = Path.GetFullPath(targetPath);
        _directoryPath = Path.GetDirectoryName(_targetPath) ?? ".";
        _temporaryPath = Path.Combine(
            _directoryPath,
            string.Concat(".", Path.GetFileName(_targetPath), "-", Guid.NewGuid().ToString("N"), ".tmp"));

        _stream = new FileStream(
            _temporaryPath,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,

                // The repository is Linux-only; the guard keeps the analyzer
                // honest about an API the BCL does not offer on Windows.
                UnixCreateMode = OperatingSystem.IsLinux() ? mode : throw new PlatformNotSupportedException(),
            });
    }

    /// <summary>
    /// Gets the writable stream the artefact is built in; it is no longer valid
    /// after <see cref="Commit"/> or <see cref="Abort"/>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The writer has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The writer was already committed or aborted.</exception>
    public Stream Stream
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_finished)
            {
                throw new InvalidOperationException("The atomic writer is already finished; its stream is no longer valid.");
            }

            return _stream;
        }
    }

    /// <summary>
    /// Flushes the artefact, syncs it, renames it over the target and syncs the
    /// target directory. An existing regular-file target narrows to the bits the
    /// requested and the previous mode share; a new target keeps the mode the
    /// temporary file was created with, as the kernel applied the process umask.
    /// </summary>
    /// <returns>
    /// A successful result, whose value is always <see langword="true"/>, once
    /// the artefact is committed. A failure with code
    /// <c>io.symbolic_link_target</c> when the destination is a symbolic link, or
    /// <c>io.commit_failed</c> when the flush, the mode change or the rename
    /// fails (a directory destination, a full device). On any failure the
    /// temporary file is removed and the stream is disposed.
    /// </returns>
    /// <remarks>
    /// The symbolic-link refusal is decided by inspecting the destination
    /// before the rename — there is no atomic <c>renameat2</c> guard — so a
    /// destination swapped to a symbolic link in the window between the
    /// inspection and the rename is not guarded.
    /// <para>
    /// The directory <c>fsync</c> that follows the rename is <b>best-effort</b>:
    /// its failure is not surfaced, because the rename has already happened and a
    /// retry would not be a new commit. A caller that needs the strongest
    /// guarantee re-syncs the directory itself.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The writer has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The writer was already committed or aborted.</exception>
    public Result<bool> Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_finished)
        {
            throw new InvalidOperationException("The atomic writer is already finished.");
        }

        try
        {
            if (!TryPrepareDestination(out var error))
            {
                return Result<bool>.Failure(error);
            }

            _stream.Flush(flushToDisk: true);
            _stream.Dispose();
            File.Move(_temporaryPath, _targetPath, overwrite: true);

            // Best-effort: the rename already landed, so its failure is not a
            // failed commit; see the remarks on Commit.
            _ = LinuxFileSystem.TrySyncDirectory(_directoryPath);
            return Result<bool>.Success(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<bool>.Failure(
                new CoreError(
                    "io.commit_failed",
                    $"The artefact could not be committed: '{PathRedaction.ForMessage(_targetPath)}'."));
        }
        finally
        {
            // The stream and the temporary file never outlive the writer. On the
            // success path the stream is already disposed and the temporary file
            // renamed away, so both steps are no-ops; on every other path this is
            // the cleanup the old code skipped once _finished was set.
            _finished = true;
            _stream.Dispose();
            TryDelete(_temporaryPath);
        }
    }

    /// <summary>
    /// Deletes the temporary file, leaving the target exactly as it was.
    /// </summary>
    public void Abort()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _stream.Dispose();
        TryDelete(_temporaryPath);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Abort();
    }

    /// <summary>
    /// Inspects the destination without following a symbolic link. A symbolic
    /// link is refused; an existing regular file has the bits it shares with the
    /// requested mode applied to the temporary file, so the rename carries them
    /// to the committed artefact without ever widening it.
    /// </summary>
    /// <param name="error">The refusal when the destination is a symbolic link.</param>
    /// <returns><see langword="true"/> when the commit may proceed.</returns>
    private bool TryPrepareDestination([NotNullWhen(false)] out CoreError? error)
    {
        error = null;
        FileAttributes attributes;

        try
        {
            attributes = File.GetAttributes(_targetPath);
        }
        catch (FileNotFoundException)
        {
            // The destination does not exist: the rename creates it and the
            // kernel applies the process umask to the constructor's mode.
            return true;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            error = new CoreError(
                "io.symbolic_link_target",
                $"The destination is a symbolic link; refusing to replace it: '{PathRedaction.ForMessage(_targetPath)}'.");
            return false;
        }

        if (OperatingSystem.IsLinux() &&
            (attributes & FileAttributes.Directory) == 0 &&
            LinuxFileSystem.TryStat(_targetPath, out var status) &&
            status.Type == LinuxFileType.RegularFile)
        {
            // A commit never widens: the destination's bits survive only where
            // the caller asked for them, so OwnerOnly (0600) repairs an
            // over-permissive file instead of preserving it.
            File.SetUnixFileMode(_temporaryPath, (UnixFileMode)status.Mode & _mode);
        }

        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The artefact guarantee is about the target, never about cleanup.
        }
    }
}
