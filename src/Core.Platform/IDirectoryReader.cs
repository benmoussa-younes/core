// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform;

/// <summary>
/// Reads the entries of an open directory.
/// </summary>
public interface IDirectoryReader : IDisposable
{
    /// <summary>
    /// Reads the next entry.
    /// </summary>
    /// <param name="entry">The entry when one was read; otherwise the default value.</param>
    /// <returns><see langword="false"/> at the end of the directory.</returns>
    bool TryRead(out DirectoryEntry entry);
}
