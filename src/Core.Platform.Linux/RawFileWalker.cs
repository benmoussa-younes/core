// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.IO.Walking;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Unicode;
using Core.IO.Files;


namespace Core.Platform.Linux;

/// <summary>
/// A low-allocation Linux walk that yields regular files with their parent
/// directory descriptor and name bytes, so a consumer opens each file with
/// <c>openat</c> instead of building a full path string.
/// </summary>
/// <remarks>
/// <para>
/// This is the measured fast path for a full-tree read: the spike on 1 024 files
/// measured 0,871 ms and 66,8 KiB per walk against 1,547 ms and 283 KiB for the
/// string <see cref="DirectoryWalker"/>. Names, relative paths and the directory
/// entries live in pooled buffers; the only per-walk state is the ancestor list.
/// </para>
/// <para>
/// Version 1 does not follow symbolic links: a link is reported to the skip
/// handler with <see cref="WalkSkipReason.SymbolicLink"/>. Include/exclude
/// patterns are not evaluated here; use <see cref="DirectoryWalker"/> when the
/// walk must apply <see cref="WalkOptions"/> patterns or follow links.
/// </para>
/// <para>
/// A walker instance is not thread-safe and must not be used concurrently. The
/// spans passed to the handlers are valid only during the call; a handler that
/// needs to keep one must copy it.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed unsafe class RawFileWalker
{
    private const int AtCurrentWorkingDirectory = -100;
    private const int OpenReadOnly = 0;
    private const int OpenDirectory = 0x10000;
    private const int OpenCloseOnExec = 0x80000;
    private const int OpenNoFollow = 0x20000;
    private const int AtSymlinkNoFollow = 0x100;
    private const int DirectoryType = 4;
    private const int RegularType = 8;
    private const int SymbolicLinkType = 10;
    private const int DirectoryBufferSize = 1 << 15;
    private const int InitialNameBufferSize = 1 << 16;
    private const int HeaderSize = 19;
    private const int MaximumNameBytes = 64 * 1024 * 1024;
    private const int MaximumEntries = 1_000_000;

    private static readonly byte[] Proc = "proc"u8.ToArray();
    private static readonly byte[] Sys = "sys"u8.ToArray();
    private static readonly byte[] Dev = "dev"u8.ToArray();
    private static readonly byte[] Run = "run"u8.ToArray();

    private readonly int _maximumDepth;
    private readonly bool _includePseudoFilesystems;
    private readonly byte[] _path = new byte[4096];
    private readonly List<(ulong Device, ulong Inode)> _ancestors = [];
    private readonly Comparison<Entry> _compare;

    private byte[] _names = [];
    private int _pathLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="RawFileWalker"/> class.
    /// </summary>
    /// <param name="maximumDepth">The deepest entry to read; 0 means no limit.</param>
    /// <param name="includePseudoFilesystems">Whether the top-level pseudo filesystems are walked.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumDepth"/> is negative.</exception>
    public RawFileWalker(int maximumDepth = 0, bool includePseudoFilesystems = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDepth);

        _maximumDepth = maximumDepth;
        _includePseudoFilesystems = includePseudoFilesystems;
        _compare = CompareEntries;
    }

    /// <summary>
    /// Walks a root and reports every regular file it can open.
    /// </summary>
    /// <param name="root">The root directory, or a single file.</param>
    /// <param name="onFile">Receives every regular file.</param>
    /// <param name="onSkip">Receives every path that was not read; may be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> or <paramref name="onFile"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="root"/> is empty.</exception>
    public void Walk(string root, RawFileHandler onFile, RawFileSkipHandler? onSkip = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(onFile);
        ArgumentException.ThrowIfNullOrEmpty(root);

        var rootFull = Path.GetFullPath(root);
        var rootName = Path.GetFileName(rootFull);
        var rootBytes = Encoding.UTF8.GetBytes(rootName);

        if (!_includePseudoFilesystems && IsPseudoFileSystem(rootFull))
        {
            onSkip?.Invoke(rootBytes, WalkSkipReason.PseudoFilesystem);
            return;
        }

        if (!LinuxFileSystem.TryStat(rootFull, out var rootStatus))
        {
            onSkip?.Invoke(rootBytes, WalkSkipReason.Unreadable);
            return;
        }

        if (rootStatus.Type != LinuxFileType.Directory)
        {
            if (rootStatus.Type == LinuxFileType.SymbolicLink)
            {
                onSkip?.Invoke(rootBytes, WalkSkipReason.SymbolicLink);
                return;
            }

            onFile(
                rootBytes,
                directoryDescriptor: -1,
                rootBytes,
                new FileIdentity(rootStatus.Device, rootStatus.Inode));
            return;
        }

        var rootPath = Encoding.UTF8.GetBytes(rootFull + "\0");
        int descriptor;

        fixed (byte* pointer = rootPath)
        {
            descriptor = Interop.OpenAt(
                AtCurrentWorkingDirectory,
                pointer,
                OpenReadOnly | OpenDirectory | OpenCloseOnExec,
                0);
        }

        if (descriptor < 0)
        {
            onSkip?.Invoke(rootBytes, WalkSkipReason.Unreadable);
            return;
        }

        try
        {
            _ancestors.Clear();
            _pathLength = 0;
            WalkDirectory(descriptor, depth: 0, onFile, onSkip);
        }
        finally
        {
            _ = Interop.Close(descriptor);
        }
    }

    private void WalkDirectory(int descriptor, int depth, RawFileHandler onFile, RawFileSkipHandler? onSkip)
    {
        if (!LinuxFileSystem.TryStatDescriptor(descriptor, out var directoryStatus))
        {
            onSkip?.Invoke(_path.AsSpan(0, _pathLength), WalkSkipReason.Unreadable);
            return;
        }

        var identity = (directoryStatus.Device, directoryStatus.Inode);

        if (_ancestors.Contains(identity))
        {
            onSkip?.Invoke(_path.AsSpan(0, _pathLength), WalkSkipReason.Cycle);
            return;
        }

        _ancestors.Add(identity);
        var directoryBuffer = ArrayPool<byte>.Shared.Rent(DirectoryBufferSize);
        var names = ArrayPool<byte>.Shared.Rent(InitialNameBufferSize);
        var entries = ArrayPool<Entry>.Shared.Rent(64);

        try
        {
            var count = ReadEntries(descriptor, directoryBuffer, ref names, ref entries);
            _names = names;
            entries.AsSpan(0, count).Sort(_compare);

            for (var index = 0; index < count; index++)
            {
                var entry = entries[index];
                var name = names.AsSpan(entry.Offset, entry.Length);
                var type = entry.Type == 0 ? ResolveType(descriptor, name) : entry.Type;

                if (type == SymbolicLinkType)
                {
                    AppendPath(name, separator: false);
                    onSkip?.Invoke(_path.AsSpan(0, _pathLength), WalkSkipReason.SymbolicLink);
                    _pathLength -= name.Length;
                    continue;
                }

                if (type == DirectoryType)
                {
                    if (depth == 0 && !_includePseudoFilesystems && IsPseudoName(name))
                    {
                        AppendPath(name, separator: false);
                        onSkip?.Invoke(_path.AsSpan(0, _pathLength), WalkSkipReason.PseudoFilesystem);
                        _pathLength -= name.Length;
                        continue;
                    }

                    if (_maximumDepth != 0 && depth + 1 >= _maximumDepth)
                    {
                        continue;
                    }

                    AppendPath(name, separator: true);
                    int child;

                    fixed (byte* namesPointer = &names[entry.Offset])
                    {
                        child = Interop.OpenAt(
                            descriptor,
                            namesPointer,
                            OpenReadOnly | OpenDirectory | OpenNoFollow | OpenCloseOnExec,
                            0);
                    }

                    if (child >= 0)
                    {
                        try
                        {
                            WalkDirectory(child, depth + 1, onFile, onSkip);
                        }
                        finally
                        {
                            _ = Interop.Close(child);
                        }
                    }
                    else
                    {
                        onSkip?.Invoke(_path.AsSpan(0, _pathLength), WalkSkipReason.Unreadable);
                    }

                    _pathLength -= name.Length + 1;
                    continue;
                }

                if (!Utf8.IsValid(name))
                {
                    AppendPath(name, separator: false);
                    onSkip?.Invoke(_path.AsSpan(0, _pathLength), WalkSkipReason.NonUtf8);
                    _pathLength -= name.Length;
                    continue;
                }

                if (_maximumDepth != 0 && depth + 1 > _maximumDepth)
                {
                    continue;
                }

                AppendPath(name, separator: false);
                var deviceKnown = directoryStatus.Device != 0;
                onFile(
                    _path.AsSpan(0, _pathLength),
                    descriptor,
                    name,
                    deviceKnown && entry.Inode != 0
                        ? new FileIdentity(directoryStatus.Device, entry.Inode)
                        : FileIdentity.Unknown);
                _pathLength -= name.Length;
            }
        }
        finally
        {
            _ancestors.RemoveAt(_ancestors.Count - 1);
            _names = [];
            ArrayPool<byte>.Shared.Return(directoryBuffer);
            ArrayPool<byte>.Shared.Return(names);
            ArrayPool<Entry>.Shared.Return(entries);
        }
    }

    private int ReadEntries(int descriptor, byte[] directoryBuffer, ref byte[] names, ref Entry[] entries)
    {
        var count = 0;
        var nameLength = 0;

        fixed (byte* buffer = directoryBuffer)
        {
            while (true)
            {
                int read;

                do
                {
                    read = Interop.GetDents64(descriptor, buffer, DirectoryBufferSize);
                }
                while (read < 0 && Marshal.GetLastPInvokeError() == (int)LinuxErrno.Interrupted);

                if (read < 0)
                {
                    throw new IOException("Unable to read the directory stream.");
                }

                if (read == 0)
                {
                    break;
                }

                var offset = 0;

                while (offset < read)
                {
                    if (read - offset < HeaderSize)
                    {
                        throw new IOException("Malformed directory record: the header is truncated.");
                    }

                    var recordLength = (ushort)(directoryBuffer[offset + 16] | (directoryBuffer[offset + 17] << 8));

                    if (recordLength < HeaderSize)
                    {
                        throw new IOException("Malformed directory record: the length is below the header size.");
                    }

                    if (recordLength > read - offset)
                    {
                        throw new IOException("Malformed directory record: the length exceeds the block.");
                    }

                    var type = directoryBuffer[offset + 18];
                    var nameOffset = offset + HeaderSize;
                    var length = recordLength - HeaderSize;
                    var terminator = Array.IndexOf(directoryBuffer, (byte)0, nameOffset, length);

                    if (terminator >= 0)
                    {
                        length = terminator - nameOffset;
                    }

                    if (length > 0 &&
                        !(length == 1 && directoryBuffer[nameOffset] == (byte)'.') &&
                        !(length == 2 && directoryBuffer[nameOffset] == (byte)'.' && directoryBuffer[nameOffset + 1] == (byte)'.'))
                    {
                        if (nameLength + length + 1 > MaximumNameBytes || count >= MaximumEntries)
                        {
                            throw new IOException("The directory has too many entries or names.");
                        }

                        if (nameLength + length + 1 > names.Length)
                        {
                            var grown = ArrayPool<byte>.Shared.Rent(Math.Max(names.Length * 2, nameLength + length + 1));
                            Array.Copy(names, grown, nameLength);
                            ArrayPool<byte>.Shared.Return(names);
                            names = grown;
                        }

                        Buffer.BlockCopy(directoryBuffer, nameOffset, names, nameLength, length);
                        names[nameLength + length] = 0;

                        if (count == entries.Length)
                        {
                            var grown = ArrayPool<Entry>.Shared.Rent(entries.Length * 2);
                            Array.Copy(entries, grown, count);
                            ArrayPool<Entry>.Shared.Return(entries);
                            entries = grown;
                        }

                        entries[count++] = new Entry(
                            nameLength,
                            length,
                            type,
                            BinaryPrimitives.ReadUInt64LittleEndian(directoryBuffer.AsSpan(offset, 8)));
                        nameLength += length + 1;
                    }

                    offset += recordLength;
                }
            }
        }

        return count;
    }

    private int CompareEntries(Entry left, Entry right)
    {
        return _names.AsSpan(left.Offset, left.Length)
            .SequenceCompareTo(_names.AsSpan(right.Offset, right.Length));
    }

    private static byte ResolveType(int descriptor, ReadOnlySpan<byte> name)
    {
        if (name.Length >= 256)
        {
            return 0;
        }

        Span<byte> status = stackalloc byte[LinuxStatLayout.StatBufferSize];
        Span<byte> terminated = stackalloc byte[256];
        name.CopyTo(terminated);
        terminated[name.Length] = 0;

        fixed (byte* namePointer = terminated)
        {
            fixed (byte* statusPointer = status)
            {
                if (Interop.FStatAt(descriptor, namePointer, statusPointer, AtSymlinkNoFollow) != 0)
                {
                    return 0;
                }

                return LinuxStatLayout.TryParseStat(
                    new ReadOnlySpan<byte>(statusPointer, LinuxStatLayout.StatBufferSize),
                    RuntimeInformation.ProcessArchitecture,
                    out var parsed)
                    ? (byte)parsed.Type
                    : (byte)0;
            }
        }
    }

    private void AppendPath(ReadOnlySpan<byte> name, bool separator)
    {
        var needed = _pathLength + name.Length + (separator ? 1 : 0);

        if (needed > _path.Length)
        {
            throw new IOException("The relative path exceeds the walker buffer.");
        }

        name.CopyTo(_path.AsSpan(_pathLength));
        _pathLength += name.Length;

        if (separator)
        {
            _path[_pathLength++] = (byte)'/';
        }
    }

    private static bool IsPseudoName(ReadOnlySpan<byte> name) =>
        name.SequenceEqual(Proc) || name.SequenceEqual(Sys) || name.SequenceEqual(Dev) || name.SequenceEqual(Run);

    private static bool IsPseudoFileSystem(string path) =>
        path is "/proc" or "/sys" or "/dev" or "/run";

    private readonly record struct Entry(int Offset, int Length, byte Type, ulong Inode);
}
