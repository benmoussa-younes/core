using System.Runtime.Versioning;

namespace Core.Platform.Linux;

/// <summary>
/// The process-wide file creation mask (<c>umask</c>), read from the kernel and
/// replaced for a scope.
/// </summary>
/// <remarks>
/// The mask is process-wide, not per-thread: a scope must not be entered by two
/// threads at once. <see cref="TryRead"/> reads <c>Umask:</c> in
/// <c>/proc/self/status</c> without changing the mask.
/// </remarks>
[SupportedOSPlatform("linux")]
public static class UnixProcessMask
{
    private const uint PermissionBits = 0x1FF;

    /// <summary>Replaces the process mask and returns the previous one.</summary>
    /// <param name="mask">The bits to remove from every created file.</param>
    /// <returns>The mask in force before the call; the argument unchanged on another platform.</returns>
    public static UnixFileMode Replace(UnixFileMode mask)
    {
        if (!OperatingSystem.IsLinux())
        {
            return mask;
        }

        return (UnixFileMode)(Interop.Umask((uint)mask) & PermissionBits);
    }

    /// <summary>Reads the process mask without changing it.</summary>
    /// <param name="mask">The mask when the call succeeds.</param>
    /// <returns><see langword="true"/> when <c>/proc/self/status</c> reported a mask.</returns>
    public static bool TryRead(out UnixFileMode mask)
    {
        mask = default;

        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            foreach (var line in File.ReadLines("/proc/self/status"))
            {
                if (!line.StartsWith("Umask:", StringComparison.Ordinal))
                {
                    continue;
                }

                var text = line["Umask:".Length..].Trim();

                try
                {
                    mask = (UnixFileMode)(Convert.ToUInt32(text, 8) & PermissionBits);
                    return true;
                }
                catch (FormatException)
                {
                    return false;
                }
                catch (OverflowException)
                {
                    return false;
                }
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }
}
