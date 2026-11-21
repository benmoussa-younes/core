// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Primitives;
using Core.Results;

namespace Core.Platform.Linux;

/// <summary>
/// The Linux <see cref="IAtomicFileWriterFactory"/>: the POSIX atomic writer with
/// owner-only (0600/0700) or default permissions.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class LinuxAtomicFileWriterFactory : IAtomicFileWriterFactory
{
    private const UnixFileMode OwnerOnlyFile =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode DefaultFile =
        UnixFileMode.UserRead | UnixFileMode.UserWrite |
        UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    /// <inheritdoc />
    public Result<IAtomicFileWriter> Create(string targetPath, AtomicFileOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            var mode = options.OwnerOnly ? OwnerOnlyFile : DefaultFile;
            return Result<IAtomicFileWriter>.Success(new AtomicFileWriter(targetPath, mode));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Result<IAtomicFileWriter>.Failure(new CoreError(
                "io.commit_failed",
                $"The artefact writer could not be created: '{PathRedaction.ForMessage(targetPath)}'."));
        }
    }

    /// <inheritdoc />
    public void EnsureDirectory(string path, bool ownerOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var created = !Directory.Exists(path);
        Directory.CreateDirectory(path);

        if (!ownerOnly || !OperatingSystem.IsLinux())
        {
            return;
        }

        if (created)
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return;
        }

        // A pre-existing directory belongs to the application, not to this writer:
        // tightening it would fight the host's own policy (a 0750 state directory
        // is legitimate) and fails with EPERM on a directory the process does not
        // own. The attack that matters is another local user replacing the file,
        // which needs write access, so only that is refused.
        if (LinuxFileSystem.TryStat(path, out var status) &&
            status.Type == LinuxFileType.Directory &&
            (status.Mode & 0x12) != 0)
        {
            throw new IOException("The directory is group- or world-writable.");
        }
    }
}
