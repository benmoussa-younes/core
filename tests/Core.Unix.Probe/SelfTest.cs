using System.Globalization;
using System.Net.Sockets;
using Core.Platform.Linux;

namespace Core.Unix.Probe;

internal static class SelfTest
{
    public static int Run()
    {
        var failures = 0;
        failures += Check("peer credentials of a connected Unix socket", PeerCredentialsOfAConnectedUnixSocket());
        failures += Check("no peer on a listening socket", NoPeerOnAListeningSocket());
        failures += Check("locked memory round trip", LockedMemoryRoundTrip());
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
