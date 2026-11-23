using System.Net;
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

    [Fact]
    public void PeerCredentialsAreRefusedOnAConnectedInternetSocket()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        client.Connect(listener.LocalEndPoint!);
        using var server = listener.Accept();

        Assert.False(UnixSocket.TryGetPeerCredentials(server, out var credentials, out var error));
        Assert.Equal(default, credentials);
        Assert.Equal(LinuxErrno.ProtocolNotAvailable, error);
    }

    [Fact]
    public void CredentialsAreReadAtTheKernelOffsets()
    {
        var buffer = new byte[12];
        BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), 1234);
        BitConverter.TryWriteBytes(buffer.AsSpan(4, 4), 1000u);
        BitConverter.TryWriteBytes(buffer.AsSpan(8, 4), 2000u);

        var credentials = UnixSocket.ReadCredentials(buffer);

        Assert.Equal(1234, credentials.ProcessId);
        Assert.Equal(1000u, credentials.UserId);
        Assert.Equal(2000u, credentials.GroupId);
    }

    [Theory]
    [InlineData(0, uint.MaxValue, uint.MaxValue, false)]
    [InlineData(1234, 1000, 1000, true)]
    [InlineData(0, 1000, 1000, true)]
    [InlineData(1234, 1000, uint.MaxValue, true)]
    public void ThePeerIdentityIsTheEffectiveUserId(int processId, uint userId, uint groupId, bool expected)
    {
        Assert.Equal(expected, UnixSocket.IsPeerIdentity(new UnixPeerCredentials(processId, userId, groupId)));
    }
}
