// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform.Linux;

/// <summary>
/// A Linux <c>errno</c> value.
/// </summary>
/// <remarks>
/// Only the errors the toolchain acts on are named; the numeric values are the
/// architecture-independent asm-generic values shared by x86-64 and AArch64.
/// Any value not named here is preserved as an <see cref="int"/> by
/// <c>LinuxFileSystem.LastError</c>.
/// </remarks>
public enum LinuxErrno
{
    /// <summary>No error (<c>0</c>).</summary>
    None = 0,

    /// <summary>Operation not permitted (<c>EPERM</c>, 1).</summary>
    OperationNotPermitted = 1,

    /// <summary>No such file or directory (<c>ENOENT</c>, 2).</summary>
    NoEntry = 2,

    /// <summary>No such process (<c>ESRCH</c>, 3).</summary>
    NoSuchProcess = 3,

    /// <summary>Interrupted system call (<c>EINTR</c>, 4).</summary>
    Interrupted = 4,

    /// <summary>Input/output error (<c>EIO</c>, 5).</summary>
    InputOutput = 5,

    /// <summary>Argument list too long (<c>E2BIG</c>, 7).</summary>
    ArgumentListTooLong = 7,

    /// <summary>Exec format error (<c>ENOEXEC</c>, 8).</summary>
    ExecFormatError = 8,

    /// <summary>Bad file descriptor (<c>EBADF</c>, 9).</summary>
    BadFileDescriptor = 9,

    /// <summary>No child processes (<c>ECHILD</c>, 10).</summary>
    NoChild = 10,

    /// <summary>Resource temporarily unavailable (<c>EAGAIN</c>, 11).</summary>
    TryAgain = 11,

    /// <summary>Cannot allocate memory (<c>ENOMEM</c>, 12).</summary>
    OutOfMemory = 12,

    /// <summary>Permission denied (<c>EACCES</c>, 13).</summary>
    PermissionDenied = 13,

    /// <summary>Bad address (<c>EFAULT</c>, 14).</summary>
    BadAddress = 14,

    /// <summary>Device or resource busy (<c>EBUSY</c>, 16).</summary>
    Busy = 16,

    /// <summary>File exists (<c>EEXIST</c>, 17).</summary>
    AlreadyExists = 17,

    /// <summary>Not a directory (<c>ENOTDIR</c>, 20).</summary>
    NotDirectory = 20,

    /// <summary>Is a directory (<c>EISDIR</c>, 21).</summary>
    IsDirectory = 21,

    /// <summary>Invalid argument (<c>EINVAL</c>, 22).</summary>
    InvalidArgument = 22,

    /// <summary>Too many open files in the system (<c>ENFILE</c>, 23).</summary>
    FileTableOverflow = 23,

    /// <summary>Too many open files (<c>EMFILE</c>, 24).</summary>
    TooManyOpenFiles = 24,

    /// <summary>Inappropriate ioctl for a non-terminal descriptor (<c>ENOTTY</c>, 25).</summary>
    NotATty = 25,

    /// <summary>Text file busy (<c>ETXTBSY</c>, 26).</summary>
    TextFileBusy = 26,

    /// <summary>No space left on device (<c>ENOSPC</c>, 28).</summary>
    NoSpaceLeft = 28,

    /// <summary>Read-only file system (<c>EROFS</c>, 30).</summary>
    ReadOnlyFileSystem = 30,

    /// <summary>Broken pipe (<c>EPIPE</c>, 32).</summary>
    BrokenPipe = 32,

    /// <summary>File name too long (<c>ENAMETOOLONG</c>, 36).</summary>
    NameTooLong = 36,

    /// <summary>Function not implemented (<c>ENOSYS</c>, 38).</summary>
    FunctionNotImplemented = 38,

    /// <summary>Too many levels of symbolic links (<c>ELOOP</c>, 40).</summary>
    TooManySymbolicLinks = 40,

    /// <summary>Value too large for the data type (<c>EOVERFLOW</c>, 75).</summary>
    Overflow = 75,

    /// <summary>Message too long or control data truncated (<c>EMSGSIZE</c>, 90).</summary>
    MessageTooLong = 90,

    /// <summary>Protocol not available (<c>ENOPROTOOPT</c>, 92).</summary>
    ProtocolNotAvailable = 92,

    /// <summary>Transport endpoint is not connected (<c>ENOTCONN</c>, 107).</summary>
    NotConnected = 107,
}
