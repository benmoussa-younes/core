// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Core.Results;

namespace Core.Platform.Linux;

/// <summary>
/// A pseudo-terminal pair: the master descriptor owned by this object and the
/// slave device path a child opens on its standard descriptors.
/// </summary>
/// <remarks>
/// The master is <c>posix_openpt(O_RDWR | O_NOCTTY | O_CLOEXEC)</c>, then
/// <c>grantpt</c> and <c>unlockpt</c>. On devpts, <c>grantpt</c> makes no system
/// call: the slave belongs to the caller and takes its group and mode from the
/// mount options (<c>gid=</c>, <c>mode=</c>; 0600 by default, 0620 on some
/// hosts). The slave is not opened here: an open slave keeps the pair alive
/// after the child exits and hides the end of the stream, which the kernel
/// reports as <c>EIO</c> when the master is read. The window size is set on the master before the spawn; the kernel
/// keeps it on the pair, so the child sees it when its first <c>open</c> attaches
/// the terminal. Reading the size back on the master before a slave is open
/// reports zeros. The object owns the master and closes it on dispose; a
/// disposed terminal is refused by the spawn, and the spawn holds the master
/// open so the slave path cannot be recycled during the call. It is not
/// thread-safe.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class UnixPseudoTerminal : IDisposable
{
    private const int SlaveNameBuffer = 128;
    private const int WindowSizeValues = 4;

    private readonly LinuxFileDescriptor _master;
    private readonly string _slavePath;

    private UnixPseudoTerminal(LinuxFileDescriptor master, string slavePath)
    {
        _master = master;
        _slavePath = slavePath;
    }

    /// <summary>Gets the master descriptor number.</summary>
    /// <remarks>
    /// The object owns the master; the caller must not close it, and the number
    /// is not valid after <see cref="Dispose"/>.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The terminal is disposed.</exception>
    public int MasterDescriptor => Descriptor();

    /// <summary>Gets the absolute path of the slave device, such as <c>/dev/pts/3</c>.</summary>
    public string SlavePath => _slavePath;

    /// <summary>Gets whether the master is closed.</summary>
    internal bool IsDisposed => _master.IsClosed;

    /// <summary>Gets the owning handle, so a spawn can hold the master across the call.</summary>
    internal LinuxFileDescriptor MasterHandle => _master;

    /// <summary>Creates a pseudo-terminal pair.</summary>
    /// <returns>The terminal, or a failure with code <see cref="CoreErrorCodes.UnixTerminalFailed"/>.</returns>
    public static unsafe Result<UnixPseudoTerminal> Create()
    {
        if (!OperatingSystem.IsLinux())
        {
            return Failure("posix_openpt", (int)LinuxErrno.FunctionNotImplemented);
        }

        var master = Interop.PosixOpenPt(
            Interop.OpenReadWrite | Interop.OpenNoControlTerminal | Interop.OpenCloseOnExec);

        if (master < 0)
        {
            return Failure("posix_openpt", Marshal.GetLastPInvokeError());
        }

        var handle = new LinuxFileDescriptor(master);

        if (Interop.GrantPt(master) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            return Failure("grantpt", error);
        }

        if (Interop.UnlockPt(master) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            return Failure("unlockpt", error);
        }

        var buffer = stackalloc byte[SlaveNameBuffer];

        if (Interop.PtsNameR(master, buffer, SlaveNameBuffer) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            return Failure("ptsname_r", error);
        }

        return Result<UnixPseudoTerminal>.Success(
            new UnixPseudoTerminal(handle, Marshal.PtrToStringUTF8((IntPtr)buffer)!));
    }

    /// <summary>Reads the window size of the terminal.</summary>
    /// <param name="size">The window size when the call succeeds.</param>
    /// <param name="error">The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when the size was read.</returns>
    public unsafe bool TryGetSize(out UnixTerminalSize size, out LinuxErrno error)
    {
        size = default;
        error = LinuxErrno.None;

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        Span<ushort> values = stackalloc ushort[WindowSizeValues];

        if (!_master.TryAddRef())
        {
            error = LinuxErrno.BadFileDescriptor;
            return false;
        }

        try
        {
            fixed (ushort* pointer = values)
            {
                if (Interop.Ioctl((int)_master.DangerousGetHandle(), Interop.TerminalGetWindowSize, pointer) != 0)
                {
                    error = (LinuxErrno)Marshal.GetLastPInvokeError();
                    return false;
                }
            }
        }
        finally
        {
            _master.DangerousRelease();
        }

        size = new UnixTerminalSize(values[0], values[1], values[2], values[3]);
        return true;
    }

    /// <summary>Sets the window size of the terminal.</summary>
    /// <param name="size">The window size to apply to the pair.</param>
    /// <param name="error">The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when the size was applied.</returns>
    public unsafe bool TrySetSize(UnixTerminalSize size, out LinuxErrno error)
    {
        error = LinuxErrno.None;

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        if (_master.IsClosed)
        {
            error = LinuxErrno.BadFileDescriptor;
            return false;
        }

        Span<ushort> values =
        [
            size.Rows,
            size.Columns,
            size.XPixels,
            size.YPixels,
        ];

        if (!_master.TryAddRef())
        {
            error = LinuxErrno.BadFileDescriptor;
            return false;
        }

        try
        {
            fixed (ushort* pointer = values)
            {
                if (Interop.Ioctl((int)_master.DangerousGetHandle(), Interop.TerminalSetWindowSize, pointer) != 0)
                {
                    error = (LinuxErrno)Marshal.GetLastPInvokeError();
                    return false;
                }
            }
        }
        finally
        {
            _master.DangerousRelease();
        }

        return true;
    }

    /// <summary>Closes the master descriptor.</summary>
    public void Dispose()
    {
        _master.Dispose();
    }

    private int Descriptor()
    {
        ObjectDisposedException.ThrowIf(_master.IsClosed, this);
        return (int)_master.DangerousGetHandle();
    }

    private static Result<UnixPseudoTerminal> Failure(string call, int errno)
    {
        return Result<UnixPseudoTerminal>.Failure(
            new CoreError(CoreErrorCodes.UnixTerminalFailed, $"{call} failed: {(LinuxErrno)errno}."));
    }
}
