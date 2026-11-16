// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.IO.Walking;

using Core.IO.Files;

/// <summary>
/// One deterministic walk result: a file to process or a path to report as
/// skipped.
/// </summary>
public readonly struct WalkEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WalkEntry"/> struct.
    /// </summary>
    /// <param name="relativePath">The root-relative forward-slash path.</param>
    /// <param name="fullPath">The absolute path.</param>
    /// <param name="kind">Whether the entry is a file or a skip.</param>
    /// <param name="skipReason">The skip reason, or <see langword="null"/> for a file.</param>
    public WalkEntry(string relativePath, string fullPath, WalkEntryKind kind, WalkSkipReason? skipReason)
        : this(relativePath, fullPath, kind, skipReason, FileIdentity.Unknown)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WalkEntry"/> struct.
    /// </summary>
    /// <param name="relativePath">The root-relative forward-slash path.</param>
    /// <param name="fullPath">The absolute path.</param>
    /// <param name="kind">Whether the entry is a file or a skip.</param>
    /// <param name="skipReason">The skip reason, or <see langword="null"/> for a file.</param>
    /// <param name="identity">
    /// The object's device and inode as the pinned walk saw it, so a consumer can
    /// verify a later open has not been redirected; <see cref="FileIdentity.Unknown"/>
    /// when the file system did not report it.
    /// </param>
    public WalkEntry(
        string relativePath,
        string fullPath,
        WalkEntryKind kind,
        WalkSkipReason? skipReason,
        FileIdentity identity)
    {
        RelativePath = relativePath;
        FullPath = fullPath;
        Kind = kind;
        SkipReason = skipReason;
        Identity = identity;
    }

    /// <summary>Gets the root-relative forward-slash path.</summary>
    public string RelativePath { get; }

    /// <summary>Gets the absolute path.</summary>
    public string FullPath { get; }

    /// <summary>Gets whether the entry is a file or a skip.</summary>
    public WalkEntryKind Kind { get; }

    /// <summary>Gets the skip reason, or <see langword="null"/> for a file.</summary>
    public WalkSkipReason? SkipReason { get; }

    /// <summary>
    /// Gets the object's device and inode as the walk saw it. Pass it to
    /// <c>FileReader.Open</c>/<c>FileContent.Read</c> so a path renamed between the
    /// walk and the read is refused rather than read.
    /// </summary>
    public FileIdentity Identity { get; }

    /// <summary>Gets a value indicating whether the entry is a regular file.</summary>
    public bool IsFile => Kind == WalkEntryKind.File;
}
