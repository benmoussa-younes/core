using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Core.Results;

namespace Core.Platform.Linux;

/// <summary>
/// Suppresses terminal echo for the lifetime of the object, so a passphrase read
/// from a terminal is not displayed, and restores the previous attributes on
/// dispose.
/// </summary>
/// <remarks>
/// The descriptor must be a terminal: a regular file is a failure with
/// <see cref="LinuxErrno.NotATty"/> and a closed descriptor with
/// <see cref="LinuxErrno.BadFileDescriptor"/>. The <c>termios</c> layout stays
/// internal; only the echo flag is touched, and the restore uses
/// <c>TCSAFLUSH</c>. The class holds no reference to the descriptor and never
/// closes it. The library does not own the signal disposition: if the process
/// is signalled while echo is off, the terminal keeps the disabled attributes.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class UnixTerminalEcho : IDisposable
{
    private const int TermiosSize = 60;
    private const int LocalFlagsOffset = 12;
    private const uint EchoFlag = 0x8;
    private const int FlushOnSet = 2;

    private readonly int _descriptor;
    private readonly byte[] _original;
    private bool _disposed;

    private UnixTerminalEcho(int descriptor, byte[] original)
    {
        _descriptor = descriptor;
        _original = original;
    }

    /// <summary>Disables echo on a terminal descriptor.</summary>
    /// <param name="descriptor">The terminal descriptor to change.</param>
    /// <returns>The scope that restores the previous attributes, or a failure naming the <c>errno</c>.</returns>
    public static unsafe Result<UnixTerminalEcho> Disable(int descriptor)
    {
        if (!OperatingSystem.IsLinux())
        {
            return Result<UnixTerminalEcho>.Failure(
                new CoreError("unix.termios", "termios is only available on Linux."));
        }

        var original = new byte[TermiosSize];

        fixed (byte* pointer = original)
        {
            if (Interop.TcGetAttr(descriptor, pointer) != 0)
            {
                return Failure("tcgetattr", Marshal.GetLastPInvokeError());
            }
        }

        var modified = (byte[])original.Clone();
        var flags = BitConverter.ToUInt32(modified, LocalFlagsOffset);
        BitConverter.TryWriteBytes(modified.AsSpan(LocalFlagsOffset, 4), flags & ~EchoFlag);

        fixed (byte* pointer = modified)
        {
            if (Interop.TcSetAttr(descriptor, FlushOnSet, pointer) != 0)
            {
                return Failure("tcsetattr", Marshal.GetLastPInvokeError());
            }
        }

        return Result<UnixTerminalEcho>.Success(new UnixTerminalEcho(descriptor, original));
    }

    /// <summary>Reads whether a terminal descriptor echoes its input.</summary>
    /// <param name="descriptor">The terminal descriptor to inspect.</param>
    /// <param name="enabled">Whether echo is on when the call succeeds.</param>
    /// <param name="error">The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when the descriptor is a terminal whose attributes were read.</returns>
    public static unsafe bool TryGetEcho(int descriptor, out bool enabled, out LinuxErrno error)
    {
        enabled = false;
        error = LinuxErrno.None;

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        var attributes = new byte[TermiosSize];

        fixed (byte* pointer = attributes)
        {
            if (Interop.TcGetAttr(descriptor, pointer) != 0)
            {
                error = (LinuxErrno)Marshal.GetLastPInvokeError();
                return false;
            }
        }

        enabled = (BitConverter.ToUInt32(attributes, LocalFlagsOffset) & EchoFlag) != 0;
        return true;
    }

    /// <summary>Restores the terminal attributes saved when echo was disabled.</summary>
    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        fixed (byte* pointer = _original)
        {
            _ = Interop.TcSetAttr(_descriptor, FlushOnSet, pointer);
        }
    }

    private static Result<UnixTerminalEcho> Failure(string call, int errno)
    {
        return Result<UnixTerminalEcho>.Failure(
            new CoreError("unix.termios", $"{call} failed: {(LinuxErrno)errno}."));
    }
}
