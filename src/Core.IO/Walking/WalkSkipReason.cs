// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.IO.Walking;

/// <summary>
/// Why the walk skipped a path. The names map byte-exactly to the toolchain's
/// NDJSON skip records.
/// </summary>
public enum WalkSkipReason
{
    /// <summary>A symbolic link that was not followed.</summary>
    SymbolicLink,

    /// <summary>A directory whose physical identity already appears among its ancestors.</summary>
    Cycle,

    /// <summary>A name whose raw bytes are not valid UTF-8.</summary>
    NonUtf8,

    /// <summary>A directory that could not be opened or read, or a root that could not be stat-ed.</summary>
    Unreadable,

    /// <summary>A top-level pseudo filesystem (<c>/proc</c>, <c>/sys</c>, <c>/dev</c>, <c>/run</c>) that is not walked by default.</summary>
    PseudoFilesystem,
}
