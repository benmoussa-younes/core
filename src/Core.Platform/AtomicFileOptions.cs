// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform;

/// <summary>
/// The permission policy of one atomic write.
/// </summary>
/// <remarks>
/// A store that may hold a secret sets <see cref="OwnerOnly"/>; the Linux
/// implementation creates the temporary file 0600 and the directory 0700. A
/// platform without POSIX modes applies the closest equivalent and documents
/// the difference.
/// </remarks>
public sealed class AtomicFileOptions
{
    /// <summary>
    /// Gets a value indicating whether the artefact and its directory are
    /// readable and writable by the owner only.
    /// </summary>
    public bool OwnerOnly { get; init; } = true;
}
