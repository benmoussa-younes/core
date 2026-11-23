using System.Net.Sockets;
using Core.Platform.Linux;

namespace Core.Platform.Linux.Tests;

public sealed class UnixSocketTests
{
    [Fact]
    public void PeerCredentialsOfAConnectedUnixSocketAreTheProcessOnes()
    {
        using var temp = new TempDirectory();
        var socketPath = temp.FullPath("instance.sock");

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);

        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        client.Connect(new UnixDomainSocketEndPoint(socketPath));
        using var server = listener.Accept();

        Assert.True(UnixSocket.TryGetPeerCredentials(server, out var credentials, out var error));
        Assert.Equal(LinuxErrno.None, error);
        Assert.Equal(Environment.ProcessId, credentials.ProcessId);
        Assert.Equal(Interop.GetEuid(), credentials.UserId);
        Assert.Equal(Interop.GetEgid(), credentials.GroupId);
    }

    [Fact]
    public void PeerCredentialsAreRefusedOnAListeningSocket()
    {
        using var temp = new TempDirectory();
        var socketPath = temp.FullPath("instance.sock");

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);

        Assert.False(UnixSocket.TryGetPeerCredentials(listener, out var credentials, out var error));
        Assert.Equal(default, credentials);
        Assert.Equal(LinuxErrno.NotConnected, error);
    }

    [Theory]
    [InlineData(uint.MaxValue, false)]
    [InlineData(0u, true)]
    [InlineData(1000u, true)]
    public void AnInvalidUserIdIsNotAPeerIdentity(uint userId, bool expected)
    {
        Assert.Equal(expected, UnixSocket.IsPeerIdentity(userId));
    }
}
