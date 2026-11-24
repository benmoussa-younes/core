using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Core.Platform.Linux;

/// <summary>
/// A child started by <see cref="UnixProcessSpawn"/>, with no shell in between.
/// </summary>
/// <remarks>
/// The child is a session leader because the spawn sets
/// <c>POSIX_SPAWN_SETSID</c>, so <see cref="TrySignal"/> with
/// <c>wholeGroup</c> set signals its process group. The caller reaps the
/// child with <see cref="TryWait"/>; a live child becomes a zombie when the
/// caller stops without waiting, and <see cref="Dispose"/> neither kills nor
/// reaps it. A reaper that takes the child first makes the wait report
/// <see cref="LinuxErrno.NoChild"/>; the object is then detached and
/// <see cref="TrySignal"/> refuses to touch a descriptor number it no longer
/// owns. The object is not thread-safe: one caller waits and signals it.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class UnixChildProcess : IDisposable
{
    private const int WaitNoHang = 1;

    private readonly int _processId;
    private UnixExitStatus _status;
    private bool _reaped;
    private bool _detached;

    internal UnixChildProcess(int processId)
    {
        _processId = processId;
    }

    /// <summary>Gets the child's process identifier.</summary>
    public int ProcessId => _processId;

    /// <summary>Tries to reap the child without blocking.</summary>
    /// <param name="status">The exit status when the child was reaped; the default value otherwise.</param>
    /// <param name="error">
    /// The <c>errno</c> when the wait failed; <see cref="LinuxErrno.None"/> when the
    /// child is still running or was reaped, and <see cref="LinuxErrno.NoChild"/>
    /// when another reaper already took the child.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the child was reaped; <see langword="false"/> when
    /// it is still running or the wait failed.
    /// </returns>
    public bool TryWait(out UnixExitStatus status, out LinuxErrno error)
    {
        if (_reaped)
        {
            status = _status;
            error = LinuxErrno.None;
            return true;
        }

        status = default;
        error = LinuxErrno.None;

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        int result;
        int raw;

        do
        {
            result = Interop.WaitPid(_processId, out raw, WaitNoHang);
        }
        while (result < 0 && Marshal.GetLastPInvokeError() == (int)LinuxErrno.Interrupted);

        if (result == 0)
        {
            return false;
        }

        if (result < 0)
        {
            error = (LinuxErrno)Marshal.GetLastPInvokeError();
            _detached = error == LinuxErrno.NoChild;
            return false;
        }

        _status = Decode(raw);
        _reaped = true;
        status = _status;
        return true;
    }

    /// <summary>Sends a signal to the child or to its process group.</summary>
    /// <param name="signal">The signal number, from 1 to 64.</param>
    /// <param name="wholeGroup">Whether to signal the child's process group instead of the child alone.</param>
    /// <param name="error">The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.</param>
    /// <returns>
    /// <see langword="true"/> when the signal was sent; a call after the child was
    /// reaped or detached returns <see langword="false"/> with
    /// <see cref="LinuxErrno.NoSuchProcess"/>.
    /// </returns>
    public bool TrySignal(int signal, bool wholeGroup, out LinuxErrno error)
    {
        error = LinuxErrno.None;

        if (signal is < 1 or > 64)
        {
            error = LinuxErrno.InvalidArgument;
            return false;
        }

        if (_reaped || _detached)
        {
            error = LinuxErrno.NoSuchProcess;
            return false;
        }

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        var target = wholeGroup ? -_processId : _processId;

        if (Interop.Kill(target, signal) != 0)
        {
            error = (LinuxErrno)Marshal.GetLastPInvokeError();
            return false;
        }

        return true;
    }

    /// <summary>Releases the handle without killing or reaping the child.</summary>
    public void Dispose()
    {
    }

    private static UnixExitStatus Decode(int raw)
    {
        var signal = raw & 0x7f;
        return signal == 0
            ? new UnixExitStatus(true, (raw >> 8) & 0xff, 0)
            : new UnixExitStatus(false, 128 + signal, signal);
    }
}
