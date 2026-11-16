// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;

namespace Core.Platform;

/// <summary>
/// Creates an <see cref="IAtomicFileWriter"/> for a target path and ensures the
/// target directory exists with the requested permission policy.
/// </summary>
/// <remarks>
/// A product supplies the platform factory; the Linux implementation is
/// <c>Core.Platform.Linux.LinuxAtomicFileWriterFactory</c>. A failure is a typed
/// <see cref="CoreError"/>, never an untyped platform exception.
/// </remarks>
public interface IAtomicFileWriterFactory
{
    /// <summary>
    /// Creates the temporary writer for <paramref name="targetPath"/>.
    /// </summary>
    /// <param name="targetPath">The path the committed content appears at.</param>
    /// <param name="options">The permission and ownership policy.</param>
    /// <returns>The writer, or a typed failure when the target directory is not writable.</returns>
    Result<IAtomicFileWriter> Create(string targetPath, AtomicFileOptions options);

    /// <summary>
    /// Creates the target directory when it does not exist and applies the
    /// owner-only mode on platforms that have one.
    /// </summary>
    /// <param name="path">The directory to create.</param>
    /// <param name="ownerOnly">Whether the directory must be owner-only.</param>
    void EnsureDirectory(string path, bool ownerOnly);
}
