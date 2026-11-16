// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Primitives;
namespace Core.IO.Walking;

/// <summary>
/// The policy of a directory walk: which files match and how deep to descend.
/// </summary>
public readonly struct WalkOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WalkOptions"/> struct.
    /// </summary>
    /// <param name="includes">The include patterns; empty matches every file.</param>
    /// <param name="excludes">The exclude patterns; empty excludes nothing.</param>
    /// <param name="maximumDepth">The maximum file depth, or <c>0</c> for unlimited.</param>
    /// <param name="followSymbolicLinks">Whether symbolic links are followed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="includes"/> or <paramref name="excludes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumDepth"/> is negative.</exception>
    public WalkOptions(
        IReadOnlyList<PathPattern> includes,
        IReadOnlyList<PathPattern> excludes,
        int maximumDepth,
        bool followSymbolicLinks)
        : this(includes, excludes, maximumDepth, followSymbolicLinks, includePseudoFilesystems: false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WalkOptions"/> struct with
    /// control over the pseudo-filesystem exclusion.
    /// </summary>
    /// <param name="includes">The include patterns; empty matches every file.</param>
    /// <param name="excludes">The exclude patterns; empty excludes nothing.</param>
    /// <param name="maximumDepth">The maximum file depth, or <c>0</c> for unlimited.</param>
    /// <param name="followSymbolicLinks">Whether symbolic links are followed.</param>
    /// <param name="includePseudoFilesystems">
    /// Whether <c>/proc</c>, <c>/sys</c>, <c>/dev</c> and <c>/run</c> are walked
    /// instead of being reported as <see cref="WalkSkipReason.PseudoFilesystem"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="includes"/> or <paramref name="excludes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumDepth"/> is negative.</exception>
    public WalkOptions(
        IReadOnlyList<PathPattern> includes,
        IReadOnlyList<PathPattern> excludes,
        int maximumDepth,
        bool followSymbolicLinks,
        bool includePseudoFilesystems)
    {
        ArgumentNullException.ThrowIfNull(includes);
        ArgumentNullException.ThrowIfNull(excludes);

        if (maximumDepth < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDepth),
                maximumDepth,
                "The maximum depth must be zero (unlimited) or greater.");
        }

        Includes = includes;
        Excludes = excludes;
        MaximumDepth = maximumDepth;
        FollowSymbolicLinks = followSymbolicLinks;
        IncludePseudoFilesystems = includePseudoFilesystems;
    }

    /// <summary>Gets the include patterns; empty matches every file.</summary>
    public IReadOnlyList<PathPattern> Includes { get; }

    /// <summary>Gets the exclude patterns; empty excludes nothing.</summary>
    public IReadOnlyList<PathPattern> Excludes { get; }

    /// <summary>Gets the maximum file depth, or <c>0</c> for unlimited.</summary>
    public int MaximumDepth { get; }

    /// <summary>Gets a value indicating whether symbolic links are followed.</summary>
    public bool FollowSymbolicLinks { get; }

    /// <summary>
    /// Gets a value indicating whether the pseudo filesystems (<c>/proc</c>,
    /// <c>/sys</c>, <c>/dev</c>, <c>/run</c>) are walked. When
    /// <see langword="false"/>, they are reported as
    /// <see cref="WalkSkipReason.PseudoFilesystem"/> instead of being entered.
    /// </summary>
    public bool IncludePseudoFilesystems { get; }
}
