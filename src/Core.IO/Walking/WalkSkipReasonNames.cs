// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.IO.Walking;

/// <summary>
/// The stable, machine-readable names of the walk skip reasons.
/// </summary>
public static class WalkSkipReasonNames
{
    /// <summary>
    /// Maps a skip reason to its stable lowercase name.
    /// </summary>
    /// <param name="reason">The skip reason.</param>
    /// <returns>The stable name.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason"/> is not a defined reason.</exception>
    public static string ToName(WalkSkipReason reason)
    {
        return reason switch
        {
            WalkSkipReason.SymbolicLink => "symlink",
            WalkSkipReason.Cycle => "cycle",
            WalkSkipReason.NonUtf8 => "non-utf8",
            WalkSkipReason.Unreadable => "unreadable",
            WalkSkipReason.PseudoFilesystem => "pseudo_filesystem",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown walk skip reason."),
        };
    }
}
