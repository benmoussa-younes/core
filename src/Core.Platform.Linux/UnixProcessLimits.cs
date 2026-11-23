using System.Runtime.Versioning;

namespace Core.Platform.Linux;

/// <summary>
/// The kernel resource limits of the current process (<c>getrlimit</c> and
/// <c>setrlimit</c>).
/// </summary>
/// <remarks>
/// A refusal is <see langword="false"/> with the <c>errno</c> in an out
/// parameter. Raising a soft limit above its hard ceiling, or raising a hard
/// limit without the matching capability, is such a refusal.
/// </remarks>
[SupportedOSPlatform("linux")]
public static class UnixProcessLimits
{
    private const int LimitSize = 16;

    /// <summary>Reads a resource limit.</summary>
    /// <param name="resource">The resource to read.</param>
    /// <param name="limit">The soft and hard limits when the call succeeds.</param>
    /// <param name="error">The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when the kernel reported the limit.</returns>
    public static unsafe bool TryGet(UnixResource resource, out UnixResourceLimit limit, out LinuxErrno error)
    {
        limit = default;
        error = LinuxErrno.None;

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        byte* buffer = stackalloc byte[LimitSize];

        if (Interop.GetRLimit((int)resource, buffer) != 0)
        {
            error = (LinuxErrno)System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            return false;
        }

        limit = new UnixResourceLimit(
            BitConverter.ToUInt64(new ReadOnlySpan<byte>(buffer, 8)),
            BitConverter.ToUInt64(new ReadOnlySpan<byte>(buffer + 8, 8)));
        return true;
    }

    /// <summary>Sets a resource limit.</summary>
    /// <param name="resource">The resource to set.</param>
    /// <param name="limit">The soft and hard limits to apply.</param>
    /// <param name="error">The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when the kernel accepted the limit.</returns>
    public static unsafe bool TrySet(UnixResource resource, UnixResourceLimit limit, out LinuxErrno error)
    {
        error = LinuxErrno.None;

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        byte* buffer = stackalloc byte[LimitSize];
        BitConverter.TryWriteBytes(new Span<byte>(buffer, 8), limit.Soft);
        BitConverter.TryWriteBytes(new Span<byte>(buffer + 8, 8), limit.Hard);

        if (Interop.SetRLimit((int)resource, buffer) != 0)
        {
            error = (LinuxErrno)System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            return false;
        }

        return true;
    }
}
