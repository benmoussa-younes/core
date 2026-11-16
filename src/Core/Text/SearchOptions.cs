// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Text;

/// <summary>The rules of a text search.</summary>
public sealed record SearchOptions
{
    /// <summary>Gets whether the search is case sensitive.</summary>
    public bool MatchCase { get; init; }

    /// <summary>Gets whether a match must be a whole word.</summary>
    public bool WholeWord { get; init; }

    /// <summary>Gets whether the query is a regular expression.</summary>
    public bool UseRegex { get; init; }

    /// <summary>Gets the default rules: case insensitive, any position, literal.</summary>
    public static SearchOptions Default { get; } = new();
}
