// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.InteropServices;

namespace Core.Platform.Linux;

/// <summary>
/// The raw libc surface of the Linux platform layer.
/// </summary>
/// <remarks>
/// This is the only place in the portfolio that declares <c>LibraryImport</c>.
/// The source-generated imports are NativeAOT-safe and allocate nothing. Paths
/// are raw, null-terminated UTF-8 byte pointers so the caller controls the
/// buffer; the managed wrappers own the encoding. <c>errno</c> is read with
/// <see cref="Marshal.GetLastPInvokeError"/> immediately after a call whose
/// import sets the last error.
/// </remarks>
internal static unsafe partial class Interop
{
    /// <summary>The special directory descriptor value meaning "the current directory" (<c>AT_FDCWD</c>).</summary>
    internal const int AtCurrentWorkingDirectory = -100;

    /// <summary>Do not follow a final symbolic link (<c>AT_SYMLINK_NOFOLLOW</c>).</summary>
    internal const int AtSymbolicLinkNoFollow = 0x100;

    /// <summary>Operate on the descriptor itself with an empty path (<c>AT_EMPTY_PATH</c>).</summary>
    internal const int AtEmptyPath = 0x1000;

    /// <summary>Open write-only (<c>O_WRONLY</c>).</summary>
    internal const int OpenWriteOnly = 1;

    /// <summary>Create the file when it does not exist (<c>O_CREAT</c>).</summary>
    internal const int OpenCreate = 0x40;

    /// <summary>Append every write at the end (<c>O_APPEND</c>).</summary>
    internal const int OpenAppend = 0x400;

    /// <summary>Open read-only (<c>O_RDONLY</c>).</summary>
    internal const int OpenReadOnly = 0;

    /// <summary>Open with close-on-exec (<c>O_CLOEXEC</c>).</summary>
    internal const int OpenCloseOnExec = 0x80000;

    /// <summary>Open a directory only (<c>O_DIRECTORY</c>).</summary>
    internal const int OpenDirectory = 0x10000;

    /// <summary>Do not follow a final symbolic link when opening (<c>O_NOFOLLOW</c>).</summary>
    internal const int OpenNoFollow = 0x20000;

    /// <summary>Do not block on opening a special file (<c>O_NONBLOCK</c>).</summary>
    internal const int OpenNonBlock = 0x800;

    /// <summary>The basic stat mask: type, mode, links, ownership, times, inode, size and blocks (<c>STATX_BASIC_STATS</c>).</summary>
    internal const uint StatxBasicStats = 0x07ff;

    [LibraryImport("libc", EntryPoint = "openat", SetLastError = true)]
    internal static partial int OpenAt(int directoryDescriptor, byte* path, int flags, uint mode);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    internal static partial int Close(int descriptor);

    [LibraryImport("libc", EntryPoint = "getdents64", SetLastError = true)]
    internal static partial int GetDents64(int descriptor, byte* buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "statx", SetLastError = true)]
    internal static partial int Statx(int directoryDescriptor, byte* path, int flags, uint mask, byte* status);

    [LibraryImport("libc", EntryPoint = "fstatat", SetLastError = true)]
    internal static partial int FStatAt(int directoryDescriptor, byte* path, byte* status, int flags);

    [LibraryImport("libc", EntryPoint = "geteuid")]
    internal static partial uint GetEuid();

    [LibraryImport("libc", EntryPoint = "fstat", SetLastError = true)]
    internal static partial int FStat(int descriptor, byte* status);

    [LibraryImport("libc", EntryPoint = "faccessat", SetLastError = true)]
    internal static partial int FAccessAt(int directoryDescriptor, byte* path, int mode, int flags);

    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    internal static partial int Fcntl(int descriptor, int command, int argument);

    [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
    internal static partial int FSync(int descriptor);

    [LibraryImport("libc", EntryPoint = "pread64", SetLastError = true)]
    internal static partial nint PRead64(int descriptor, byte* buffer, nuint count, long offset);

    // posix_fadvise returns the errno; it does not set the thread's last error.
    [LibraryImport("libc", EntryPoint = "posix_fadvise")]
    internal static partial int PosixFAdvise(int descriptor, long offset, long length, int advice);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    internal static partial nint Write(int descriptor, byte* buffer, nuint count);

    /// <summary>Read the mapping (<c>PROT_READ</c>).</summary>
    internal const int ProtectionRead = 0x1;

    /// <summary>Copy-on-write, private mapping (<c>MAP_PRIVATE</c>).</summary>
    internal const int MapPrivate = 0x2;

    /// <summary>The value <c>mmap</c> returns on failure (<c>MAP_FAILED</c>).</summary>
    internal static readonly void* MapFailed = unchecked((void*)(-1));

    [LibraryImport("libc", EntryPoint = "mmap", SetLastError = true)]
    internal static partial void* MMap(void* address, nuint length, int protection, int flags, int descriptor, long offset);

    [LibraryImport("libc", EntryPoint = "munmap", SetLastError = true)]
    internal static partial int MUnmap(void* address, nuint length);

    [LibraryImport("libc", EntryPoint = "madvise", SetLastError = true)]
    internal static partial int MAdvise(void* address, nuint length, int advice);
}
