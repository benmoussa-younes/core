using System.Runtime.Versioning;
using System.Text;

namespace Core.Platform.Linux;

/// <summary>
/// The process-wide hardening switches a secret-bearing process sets at startup:
/// the dumpable and no-new-privileges flags, and the thread name.
/// </summary>
/// <remarks>
/// A refusal is <see langword="false"/> with the <c>errno</c> in an out
/// parameter, and a getter returns the kernel's value; a name that does not fit
/// is an <see cref="System.ArgumentException"/> (a caller fault). Setting
/// <c>PR_SET_NO_NEW_PRIVILEGES</c> is irreversible for the process, so a caller
/// sets it once, after its privileged setup.
/// </remarks>
[SupportedOSPlatform("linux")]
public static class UnixProcessControl
{
    private const int GetDumpable = 3;
    private const int SetDumpable = 4;
    private const int SetName = 15;
    private const int GetName = 16;
    private const int SetNoNewPrivileges = 38;
    private const int GetNoNewPrivileges = 39;
    private const int NameCapacity = 16;
    private const ulong One = 1;

    /// <summary>Sets whether another process may trace or dump this one.</summary>
    /// <param name="dumpable">Whether the process stays dumpable and traceable.</param>
    /// <param name="error">The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when the kernel accepted the flag.</returns>
    public static bool TrySetDumpable(bool dumpable, out LinuxErrno error)
    {
        error = LinuxErrno.None;

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        if (Interop.Prctl(SetDumpable, dumpable ? One : 0, 0, 0, 0) != 0)
        {
            error = LastError();
            return false;
        }

        return true;
    }

    /// <summary>Reads whether another process may trace or dump this one.</summary>
    /// <param name="dumpable">Whether the process is dumpable, including the root-only mode.</param>
    /// <param name="error">The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when the kernel reported the flag.</returns>
    public static bool TryGetDumpable(out bool dumpable, out LinuxErrno error)
    {
        dumpable = false;
        error = LinuxErrno.None;

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        var value = Interop.Prctl(GetDumpable, 0, 0, 0, 0);

        if (value < 0)
        {
            error = LastError();
            return false;
        }

        dumpable = value != 0;
        return true;
    }

    /// <summary>Sets the irreversible no-new-privileges flag.</summary>
    /// <param name="error">The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when the kernel accepted the flag.</returns>
    public static bool TrySetNoNewPrivileges(out LinuxErrno error)
    {
        error = LinuxErrno.None;

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        if (Interop.Prctl(SetNoNewPrivileges, One, 0, 0, 0) != 0)
        {
            error = LastError();
            return false;
        }

        return true;
    }

    /// <summary>Reads the no-new-privileges flag.</summary>
    /// <param name="enabled">Whether the flag is set.</param>
    /// <param name="error">The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when the kernel reported the flag.</returns>
    public static bool TryGetNoNewPrivileges(out bool enabled, out LinuxErrno error)
    {
        enabled = false;
        error = LinuxErrno.None;

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        var value = Interop.Prctl(GetNoNewPrivileges, 0, 0, 0, 0);

        if (value < 0)
        {
            error = LastError();
            return false;
        }

        enabled = value != 0;
        return true;
    }

    /// <summary>Sets the current thread's name, at most 15 UTF-8 bytes.</summary>
    /// <param name="name">The new name.</param>
    /// <param name="error">The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when the kernel accepted the name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> does not fit in 15 UTF-8 bytes.</exception>
    public static unsafe bool TrySetName(string name, out LinuxErrno error)
    {
        ArgumentNullException.ThrowIfNull(name);

        var bytes = Encoding.UTF8.GetBytes(name);

        if (bytes.Length >= NameCapacity)
        {
            throw new ArgumentException(
                $"A thread name must be shorter than {NameCapacity} UTF-8 bytes.",
                nameof(name));
        }

        error = LinuxErrno.None;

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        byte* buffer = stackalloc byte[NameCapacity];
        new Span<byte>(buffer, NameCapacity).Clear();
        bytes.AsSpan().CopyTo(new Span<byte>(buffer, bytes.Length));

        if (Interop.Prctl(SetName, (ulong)buffer, 0, 0, 0) != 0)
        {
            error = LastError();
            return false;
        }

        return true;
    }

    /// <summary>Reads the current thread's name.</summary>
    /// <param name="name">The thread name when the call succeeds; an empty string otherwise.</param>
    /// <param name="error">The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when the kernel reported the name.</returns>
    public static unsafe bool TryGetName(out string name, out LinuxErrno error)
    {
        name = string.Empty;
        error = LinuxErrno.None;

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        byte* buffer = stackalloc byte[NameCapacity];
        new Span<byte>(buffer, NameCapacity).Clear();

        if (Interop.Prctl(GetName, (ulong)buffer, 0, 0, 0) != 0)
        {
            error = LastError();
            return false;
        }

        var length = 0;

        while (length < NameCapacity && buffer[length] != 0)
        {
            length++;
        }

        name = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(buffer, length));
        return true;
    }

    private static LinuxErrno LastError() => (LinuxErrno)System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
}
