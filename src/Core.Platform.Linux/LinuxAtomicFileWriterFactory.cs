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

        Directory.CreateDirectory(path);

        // Tighten a pre-existing directory too: a shared 0775 state directory
        // would let another local user replace the artefact being committed.
        if (ownerOnly && OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
