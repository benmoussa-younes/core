// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers;
using System.Text;
using Core.IO.Files;
using Core.Platform;
using Core.Primitives;

namespace Core.IO.Walking;

/// <summary>
/// A deterministic, descriptor-pinned depth-first walk of one root.
/// </summary>
/// <remarks>
/// Directory entries come from <c>getdents64</c>'s <c>d_type</c>, so a regular
/// file costs no metadata syscall; only <c>DT_UNKNOWN</c> falls back to a stat.
/// Each directory is opened with <c>openat</c> from its parent handle, so a
/// rename of an ancestor cannot redirect the traversal. Entries of each
/// directory are sorted by ordinal name. Exclude patterns prune files and
/// directories; include patterns filter files only, because a directory must be
/// descended for its children to have a chance to match. Symbolic links are
/// reported as skipped unless following is enabled, and every directory is
/// refused as a cycle when its physical identity already appears among its
/// ancestors.
/// <para>
/// A root that is not a directory is a single entry under its own name, whatever
/// its type: a regular file, a FIFO or a device yields a file entry (the
/// consumer decides whether to read it), while a root whose stat fails — a
/// dangling symbolic link or a missing path — yields an unreadable skip. This is
/// the pre-extraction tool contract and must not change silently.
/// </para>
/// </remarks>
public sealed class DirectoryWalker
{
    /// <summary>The upper bound of one name in bytes (<c>NAME_MAX</c>, 255) plus its terminator.</summary>
    private const int MaximumNameBytes = 256;

    /// <summary>
    /// The pseudo filesystems that are never walked by default: they have no
    /// meaningful content hash and some entries block forever or reject reads.
    /// </summary>
    private static readonly string[] PseudoFileSystems = ["/proc", "/sys", "/dev", "/run"];

    private readonly WalkOptions _options;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryWalker"/> class over
    /// an explicit platform file system.
    /// </summary>
    /// <param name="options">The walk policy.</param>
    /// <param name="fileSystem">The platform file system.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> has null pattern lists, or <paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    public DirectoryWalker(WalkOptions options, IFileSystem fileSystem)
    {
        // A default(WalkOptions) bypasses the struct constructor and leaves the
        // lists null, which would fail later mid-walk; reject it here.
        ArgumentNullException.ThrowIfNull(options.Includes);
        ArgumentNullException.ThrowIfNull(options.Excludes);
        ArgumentNullException.ThrowIfNull(fileSystem);

        _options = options;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Walks a root and yields its files and skipped paths in deterministic order.
    /// </summary>
    /// <param name="root">The root directory, or a single file.</param>
    /// <returns>The ordered walk entries.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="root"/> is empty.</exception>
    public IEnumerable<WalkEntry> Walk(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);

        var rootFull = Path.GetFullPath(root);
        var rootName = Path.GetFileName(rootFull);

        if (!_options.IncludePseudoFilesystems && IsPseudoFileSystem(rootFull))
        {
            yield return new WalkEntry(
                rootName.Length == 0 ? "root" : rootName,
                rootFull,
                WalkEntryKind.Skipped,
                WalkSkipReason.PseudoFilesystem);
            yield break;
        }

        // A root that is not a directory is a single entry, whatever its type:
        // a FIFO or a device is yielded as a file so the reader's zero-byte fast
        // path decides it, and a root whose stat fails (a dangling symbolic
        // link) is unreadable. Both keep the root's own name rather than the
        // directory walk's "." placeholder.
        if (_fileSystem.TryStat(rootFull, out var status))
        {
            if (status.Type != FileType.Directory)
            {
                yield return new WalkEntry(rootName, rootFull, WalkEntryKind.File, null);
                yield break;
            }
        }
        else
        {
            yield return new WalkEntry(rootName, rootFull, WalkEntryKind.Skipped, WalkSkipReason.Unreadable);
            yield break;
        }

        if (!_fileSystem.TryOpenDirectory(rootFull, out var handle) || handle is null)
        {
            yield return new WalkEntry(".", rootFull, WalkEntryKind.Skipped, WalkSkipReason.Unreadable);
            yield break;
        }

        using (handle)
        {
            foreach (var entry in WalkDirectory(handle, rootFull, string.Empty, 0, new List<(ulong Device, ulong Inode)>()))
            {
                yield return entry;
            }
        }
    }

    private static DirectoryEntry[] ReadEntries(IDirectoryHandle handle, out int count)
    {
        var buffer = ArrayPool<DirectoryEntry>.Shared.Rent(64);
        count = 0;

        try
        {
            using var reader = handle.Enumerate();

            while (reader.TryRead(out var entry))
            {
                if (count == buffer.Length)
                {
                    var grown = ArrayPool<DirectoryEntry>.Shared.Rent(buffer.Length * 2);
                    Array.Copy(buffer, grown, count);
                    ArrayPool<DirectoryEntry>.Shared.Return(buffer);
                    buffer = grown;
                }

                buffer[count++] = entry;
            }
        }
        catch (IOException)
        {
            ArrayPool<DirectoryEntry>.Shared.Return(buffer);
            throw;
        }

        return buffer;
    }

    internal static int EncodeName(string name, byte[] buffer)
    {
        // Bound the write before touching the rented buffer: a file system that
        // reported a name longer than the buffer must fail clearly, not overrun it.
        var required = Encoding.UTF8.GetByteCount(name) + 1;

        if (required > buffer.Length)
        {
            throw new InvalidOperationException(
                $"The directory entry name needs {required} bytes, more than the {buffer.Length}-byte name buffer.");
        }

        var written = Encoding.UTF8.GetBytes(name, buffer);
        buffer[written] = 0;
        return written + 1;
    }

    /// <summary>
    /// Resolves an entry whose <c>d_type</c> was <c>DT_UNKNOWN</c> with a
    /// no-follow stat; a known type is returned unchanged.
    /// </summary>
    /// <param name="handle">The open parent directory.</param>
    /// <param name="nullTerminatedName">The null-terminated UTF-8 entry name.</param>
    /// <param name="type">The raw entry type.</param>
    /// <returns>The resolved type, or <see cref="FileType.Unknown"/>.</returns>
    internal static FileType ResolveType(
        IDirectoryHandle handle,
        ReadOnlySpan<byte> nullTerminatedName,
        FileType type)
    {
        if (type != FileType.Unknown)
        {
            return type;
        }

        return handle.TryStatAt(nullTerminatedName, followSymbolicLinks: false, out var resolved)
            ? resolved.Type
            : FileType.Unknown;
    }

    /// <summary>
    /// Determines whether an absolute path is one of the pseudo filesystems
    /// (<c>/proc</c>, <c>/sys</c>, <c>/dev</c>, <c>/run</c>) or lives under one.
    /// </summary>
    /// <param name="fullPath">An absolute path.</param>
    /// <returns><see langword="true"/> when the path is inside a pseudo filesystem.</returns>
    internal static bool IsPseudoFileSystem(string fullPath)
    {
        foreach (var pseudo in PseudoFileSystems)
        {
            if (string.Equals(fullPath, pseudo, StringComparison.Ordinal))
            {
                return true;
            }

            if (fullPath.Length > pseudo.Length &&
                fullPath[pseudo.Length] == '/' &&
                fullPath.StartsWith(pseudo, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Matches(IReadOnlyList<PathPattern> patterns, string path)
    {
        for (var index = 0; index < patterns.Count; index++)
        {
            if (patterns[index].IsMatch(path.AsSpan()))
            {
                return true;
            }
        }

        return false;
    }

    internal IEnumerable<WalkEntry> WalkDirectory(
        IDirectoryHandle handle,
        string directoryPath,
        string relativeDirectory,
        int depth,
        List<(ulong Device, ulong Inode)> ancestors)
    {
        var isRoot = relativeDirectory.Length == 0;
        var displayRelative = isRoot ? "." : relativeDirectory;
        var added = false;
        var directoryDevice = 0UL;
        var deviceKnown = false;

        // The ancestor chain is maintained unconditionally. A bind mount is a
        // plain directory whose identity repeats an ancestor, so gating the guard
        // on FollowSymbolicLinks would recurse without bound on the default walk.
        // Only *following* a symbolic link stays behind the option.
        if (handle.TryGetStatus(out var status))
        {
            directoryDevice = status.Device;
            deviceKnown = true;
            var identity = (Device: status.Device, Inode: status.Inode);

            if (ancestors.Contains(identity))
            {
                yield return new WalkEntry(displayRelative, directoryPath, WalkEntryKind.Skipped, WalkSkipReason.Cycle);
                yield break;
            }

            ancestors.Add(identity);
            added = true;
        }

        try
        {
            DirectoryEntry[]? entries;
            int entryCount;

            try
            {
                entries = ReadEntries(handle, out entryCount);
            }
            catch (IOException)
            {
                entries = null;
                entryCount = 0;
            }

            if (entries is null)
            {
                yield return new WalkEntry(displayRelative, directoryPath, WalkEntryKind.Skipped, WalkSkipReason.Unreadable);
                yield break;
            }

            entries.AsSpan(0, entryCount).Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

            var nameBuffer = ArrayPool<byte>.Shared.Rent(MaximumNameBytes);

            try
            {
                for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
                {
                    var item = entries[entryIndex];
                    var relative = isRoot ? item.Name : string.Concat(relativeDirectory, "/", item.Name);
                    var entryPath = Path.Combine(directoryPath, item.Name);

                    // A pseudo-filesystem skip is part of the completeness
                    // contract, not ordinary content, so it stays visible even
                    // when an exclude pattern also matches the path. The check
                    // is four fixed name comparisons and only applies at the top
                    // of a root walk.
                    var pseudoFileSystem = isRoot &&
                        !_options.IncludePseudoFilesystems &&
                        IsPseudoFileSystem(entryPath);

                    if (!pseudoFileSystem && Matches(_options.Excludes, relative))
                    {
                        continue;
                    }

                    var entryDepth = depth + 1;

                    if (!item.IsNameValidUtf8)
                    {
                        yield return new WalkEntry(relative, entryPath, WalkEntryKind.Skipped, WalkSkipReason.NonUtf8);
                        continue;
                    }

                    var nameLength = EncodeName(item.Name, nameBuffer);
                    var name = nameBuffer.AsSpan(0, nameLength);
                    var type = ResolveType(handle, name, item.Type);

                    if (type == FileType.SymbolicLink)
                    {
                        if (!_options.FollowSymbolicLinks)
                        {
                            yield return new WalkEntry(relative, entryPath, WalkEntryKind.Skipped, WalkSkipReason.SymbolicLink);
                            continue;
                        }

                        if (handle.TryStatAt(name, followSymbolicLinks: true, out var target) &&
                            target.Type == FileType.Directory)
                        {
                            if (DepthAllowsDirectory(entryDepth))
                            {
                                if (handle.TryOpenDirectory(name, followSymbolicLinks: true, out var linked))
                                {
                                    using (linked)
                                    {
                                        foreach (var child in WalkDirectory(linked!, entryPath, relative, entryDepth, ancestors))
                                        {
                                            yield return child;
                                        }
                                    }
                                }
                                else
                                {
                                    yield return new WalkEntry(relative, entryPath, WalkEntryKind.Skipped, WalkSkipReason.Unreadable);
                                }
                            }

                            continue;
                        }

                        // A followed symbolic link to a non-directory falls through
                        // and is reported as a file.
                    }
                    else if (type == FileType.Directory)
                    {
                        // The pseudo filesystems are only ever mounted at the
                        // filesystem root, so the check is scoped to the top
                        // level of the root walk and never descends into them.
                        if (pseudoFileSystem)
                        {
                            yield return new WalkEntry(relative, entryPath, WalkEntryKind.Skipped, WalkSkipReason.PseudoFilesystem);
                            continue;
                        }

                        if (DepthAllowsDirectory(entryDepth))
                        {
                            if (handle.TryOpenDirectory(name, followSymbolicLinks: false, out var child))
                            {
                                using (child)
                                {
                                    foreach (var descendant in WalkDirectory(child!, entryPath, relative, entryDepth, ancestors))
                                    {
                                        yield return descendant;
                                    }
                                }
                            }
                            else
                            {
                                yield return new WalkEntry(relative, entryPath, WalkEntryKind.Skipped, WalkSkipReason.Unreadable);
                            }
                        }

                        continue;
                    }

                    if (!DepthAllowsFile(entryDepth) ||
                        (_options.Includes.Count > 0 && !Matches(_options.Includes, relative)))
                    {
                        continue;
                    }

                    yield return new WalkEntry(
                        relative,
                        entryPath,
                        WalkEntryKind.File,
                        null,
                        deviceKnown && item.Inode != 0
                            ? new FileIdentity(directoryDevice, item.Inode)
                            : FileIdentity.Unknown);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(nameBuffer);
                ArrayPool<DirectoryEntry>.Shared.Return(entries);
            }
        }
        finally
        {
            if (added)
            {
                ancestors.RemoveAt(ancestors.Count - 1);
            }
        }
    }

    private bool DepthAllowsDirectory(int entryDepth)
    {
        return _options.MaximumDepth == 0 || entryDepth < _options.MaximumDepth;
    }

    private bool DepthAllowsFile(int entryDepth)
    {
        return _options.MaximumDepth == 0 || entryDepth <= _options.MaximumDepth;
    }
}
