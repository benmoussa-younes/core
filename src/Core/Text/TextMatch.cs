// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Text;

/// <summary>One match, as a range in the searched text.</summary>
/// <param name="Start">The index of the first matching character.</param>
/// <param name="Length">The number of matching characters.</param>
public readonly record struct TextMatch(int Start, int Length)
{
    /// <summary>Gets the index just past the match.</summary>
    public int End => Start + Length;
}
