// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;

namespace Core.Platform;

/// <summary>
/// Writes one named artefact atomically: the bytes go to a temporary file and
/// become visible at the target only when <see cref="Commit"/> renames them.
/// </summary>
/// <remarks>
/// The platform implementation owns the durability sequence (flush, sync,
/// rename, directory sync) and the permission policy; the caller only writes the
/// stream and commits or aborts. The default Linux implementation is
/// <c>Core.Platform.Linux.AtomicFileWriter</c>.
/// </remarks>
public interface IAtomicFileWriter : IDisposable
{
    /// <summary>Gets the writable temporary stream; it is invalid after commit or abort.</summary>
    Stream Stream { get; }

    /// <summary>
    /// Flushes, syncs and renames the temporary file over the target.
    /// </summary>
    /// <returns>A success, or the typed commit failure.</returns>
    Result<bool> Commit();

    /// <summary>Deletes the temporary file, leaving the target unchanged.</summary>
    void Abort();
}
