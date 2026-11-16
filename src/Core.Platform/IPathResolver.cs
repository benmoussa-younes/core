// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Paths;
using Core.Results;

namespace Core.Platform;

/// <summary>
/// Resolves the platform's base directories into a <see cref="PathSet"/>.
/// </summary>
/// <remarks>
/// The resolver is the only platform-specific path code: a Linux implementation
/// reads the XDG base directories, a Windows implementation the known folders,
/// and a test an explicit fixture. A failure is a typed <see cref="CoreError"/>,
/// never a thrown platform exception.
/// </remarks>
public interface IPathResolver
{
    /// <summary>
    /// Resolves the base directories.
    /// </summary>
    /// <returns>The resolved directories, or a typed failure when the platform cannot provide them.</returns>
    Result<PathSet> Resolve();
}
