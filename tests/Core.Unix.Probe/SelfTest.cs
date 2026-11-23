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

            return UnixSocket.TryGetPeerCredentials(server, out var credentials, out var error) &&
                error == LinuxErrno.None &&
                credentials.ProcessId == Environment.ProcessId &&
                credentials.UserId != uint.MaxValue &&
                credentials.GroupId != uint.MaxValue;
        }
        finally
        {
            directory.Delete(recursive: true);
        }
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
}
