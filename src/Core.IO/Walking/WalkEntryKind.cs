// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.IO.Walking;

/// <summary>
/// What a directory walk found at a path.
/// </summary>
public enum WalkEntryKind
{
    /// <summary>A file to process.</summary>
    File,

    /// <summary>A path deliberately not processed, with a stable reason.</summary>
    Skipped,
}
