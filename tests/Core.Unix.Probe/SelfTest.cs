using System.Net.Sockets;
using Core.Platform.Linux;

namespace Core.Unix.Probe;

internal static class SelfTest
{
    public static int Run()
    {
        var failures = 0;
        failures += Check("peer credentials of a Unix socket", PeerCredentialsOfAUnixSocket());
        failures += Check("no peer credentials without a peer", NoPeerCredentials());
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

    private static bool PeerCredentialsOfAUnixSocket()
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

            return UnixSocket.TryGetPeerCredentials(server, out var credentials, out _) &&
                credentials.ProcessId == Environment.ProcessId &&
                credentials.UserId != uint.MaxValue;
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static bool NoPeerCredentials()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        return !UnixSocket.TryGetPeerCredentials(socket, out _, out var error) &&
            error == LinuxErrno.ProtocolNotAvailable;
    }
}
