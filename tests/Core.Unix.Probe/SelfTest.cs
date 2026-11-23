using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Core.Platform.Linux;

namespace Core.Unix.Probe;

internal static partial class SelfTest
{
    public static int Run()
    {
        var failures = 0;
        failures += Check("peer credentials of a connected Unix socket", PeerCredentialsOfAConnectedUnixSocket());
        failures += Check("no peer on a listening socket", NoPeerOnAListeningSocket());
        failures += Check("locked memory round trip", LockedMemoryRoundTrip());
        failures += Check("dumpable round trip", DumpableRoundTrip());
        failures += Check("thread name round trip", ThreadNameRoundTrip());
        failures += Check("resource limit read", ResourceLimitRead());
        failures += Check("mask scope", MaskScope());
        failures += Check("terminal echo round trip", TerminalEchoRoundTrip());
        failures += Check("no new privileges", NoNewPrivileges());
        Console.WriteLine(failures == 0 ? "SELF-TEST PASS" : $"SELF-TEST FAIL ({failures})");
        return failures == 0 ? 0 : 1;
    }

    private static int Check(string claim, bool passed)
    {
        if (passed)
        {
            return 0;
        }

        Console.Error.WriteLine($"FAIL {claim}");
        return 1;
    }

    private static bool PeerCredentialsOfAConnectedUnixSocket()
    {
        var directory = Directory.CreateTempSubdirectory("core-unix-probe-");
        var socketPath = Path.Combine(directory.FullName, "probe.sock");

        try
        {
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            listener.Listen(1);

            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(socketPath));
            using var server = listener.Accept();

            var (userId, groupId) = ReadEffectiveIds();

            return UnixSocket.TryGetPeerCredentials(server, out var credentials, out var error) &&
                error == LinuxErrno.None &&
                credentials.ProcessId == Environment.ProcessId &&
                credentials.UserId == userId &&
                credentials.GroupId == groupId;
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static bool LockedMemoryRoundTrip()
    {
        var buffer = new byte[64];
        var locked = UnixLockedMemory.Lock(buffer);
        if (!locked.IsSuccess)
        {
            return false;
        }

        using (var memory = locked.Value)
        {
            memory.Span[0] = 42;
            if (buffer[0] != 42)
            {
                return false;
            }
        }

        return buffer.All(value => value == 0);
    }

    private static bool NoPeerOnAListeningSocket()
    {
        var directory = Directory.CreateTempSubdirectory("core-unix-probe-");
        var socketPath = Path.Combine(directory.FullName, "listener.sock");

        try
        {
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            listener.Listen(1);

            return !UnixSocket.TryGetPeerCredentials(listener, out _, out var error) &&
                error == LinuxErrno.NotConnected;
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static bool DumpableRoundTrip()
    {
        if (!UnixProcessControl.TryGetDumpable(out var original, out _))
        {
            return false;
        }

        if (!UnixProcessControl.TrySetDumpable(!original, out _))
        {
            return false;
        }

        var changed = UnixProcessControl.TryGetDumpable(out var value, out _) && value == !original;
        _ = UnixProcessControl.TrySetDumpable(original, out _);
        return changed;
    }

    private static bool ThreadNameRoundTrip()
    {
        if (!UnixProcessControl.TryGetName(out var original, out _))
        {
            return false;
        }

        var ok = UnixProcessControl.TrySetName("core-probe", out _) &&
            UnixProcessControl.TryGetName(out var name, out _) &&
            name == "core-probe";

        _ = UnixProcessControl.TrySetName(original, out _);
        return ok;
    }

    private static bool ResourceLimitRead()
    {
        return UnixProcessLimits.TryGet(UnixResource.NoFile, out var limit, out _) &&
            limit.Hard >= limit.Soft;
    }

    private static bool MaskScope()
    {
        if (!UnixProcessMask.TryRead(out var original))
        {
            return false;
        }

        var mask = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

        var applied = false;

        using (UnixMaskScope.Enter(mask))
        {
            applied = UnixProcessMask.TryRead(out var current) && current == mask;
        }

        return applied && UnixProcessMask.TryRead(out var restored) && restored == original;
    }

    private static bool NoNewPrivileges()
    {
        return UnixProcessControl.TrySetNoNewPrivileges(out _) &&
            UnixProcessControl.TryGetNoNewPrivileges(out var enabled, out _) &&
            enabled;
    }

    private static bool TerminalEchoRoundTrip()
    {
        var master = PosixOpenPt(2 | 0x100);

        if (master < 0)
        {
            return false;
        }

        try
        {
            if (GrantPt(master) != 0 || UnlockPt(master) != 0)
            {
                return false;
            }

            var name = new byte[256];

            unsafe
            {
                fixed (byte* pointer = name)
                {
                    if (PtsNameR(master, pointer, (nuint)name.Length) != 0)
                    {
                        return false;
                    }
                }
            }

            var path = System.Text.Encoding.UTF8.GetString(name, 0, Array.IndexOf(name, (byte)0));
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite);
            var descriptor = (int)handle.DangerousGetHandle();

            if (!UnixTerminalEcho.TryGetEcho(descriptor, out var before) || !before)
            {
                return false;
            }

            var disabled = UnixTerminalEcho.Disable(descriptor);

            if (!disabled.IsSuccess)
            {
                return false;
            }

            using (disabled.Value)
            {
                if (!UnixTerminalEcho.TryGetEcho(descriptor, out var off) || off)
                {
                    return false;
                }
            }

            return UnixTerminalEcho.TryGetEcho(descriptor, out var restored) && restored;
        }
        finally
        {
            _ = Close(master);
        }
    }

    [LibraryImport("libc", EntryPoint = "posix_openpt", SetLastError = true)]
    private static partial int PosixOpenPt(int flags);

    [LibraryImport("libc", EntryPoint = "grantpt", SetLastError = true)]
    private static partial int GrantPt(int descriptor);

    [LibraryImport("libc", EntryPoint = "unlockpt", SetLastError = true)]
    private static partial int UnlockPt(int descriptor);

    [LibraryImport("libc", EntryPoint = "ptsname_r", SetLastError = true)]
    private static unsafe partial int PtsNameR(int descriptor, byte* buffer, nuint length);

    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int Close(int descriptor);

    private static (uint UserId, uint GroupId) ReadEffectiveIds()
    {
        uint userId = uint.MaxValue;
        uint groupId = uint.MaxValue;

        foreach (var line in File.ReadLines("/proc/self/status"))
        {
            var fields = line.Split('\t');
            if (fields.Length < 3)
            {
                continue;
            }

            if (fields[0] == "Uid:")
            {
                userId = uint.Parse(fields[2], CultureInfo.InvariantCulture);
            }
            else if (fields[0] == "Gid:")
            {
                groupId = uint.Parse(fields[2], CultureInfo.InvariantCulture);
            }
        }

        return (userId, groupId);
    }
}
