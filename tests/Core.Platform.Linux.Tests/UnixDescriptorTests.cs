using System.Net.Sockets;
using Core.Platform.Linux;

namespace Core.Platform.Linux.Tests;

[Collection("process-limits")]
public sealed class UnixDescriptorTests
{
    [Fact]
    public void ADescriptorSurvivesTheRoundTrip()
    {
        var (sender, receiver, directory) = CreatePair();
        using var sending = sender;
        using var receiving = receiver;
        using var scratch = directory;
        var source = OpenNull();

        try
        {
            Assert.True(UnixSocket.TrySend(sender, "x"u8, [source], out var sent, out var error));
            Assert.Equal(1, sent);
            Assert.Equal(LinuxErrno.None, error);

            var payload = new byte[16];
            Span<int> received = stackalloc int[1];

            Assert.True(UnixSocket.TryReceive(receiver, payload, received, out var message, out error));
            Assert.Equal(LinuxErrno.None, error);
            Assert.Equal(1, message.Length);
            Assert.Equal(1, message.DescriptorCount);
            Assert.False(message.Truncated);
            Assert.Equal((byte)'x', payload[0]);

            var flags = Interop.Fcntl(received[0], 1, 0);
            Assert.True(flags >= 0);
            Assert.NotEqual(0, flags & 1);
            _ = Interop.Close(received[0]);
        }
        finally
        {
            _ = Interop.Close(source);
        }
    }

    [Fact]
    public void TruncatedControlDataClosesTheDescriptorsAndFails()
    {
        var (sender, receiver, directory) = CreatePair();
        using var sending = sender;
        using var receiving = receiver;
        using var scratch = directory;
        var descriptors = new int[5];

        for (var index = 0; index < descriptors.Length; index++)
        {
            descriptors[index] = OpenNull();
        }

        try
        {
            var before = CountNullDescriptors();

            Assert.True(UnixSocket.TrySend(sender, "x"u8, descriptors, out _, out _));

            var payload = new byte[16];
            Span<int> none = [];

            Assert.False(UnixSocket.TryReceive(receiver, payload, none, out _, out var error));
            Assert.Equal(LinuxErrno.MessageTooLong, error);
            Assert.Equal(before, CountNullDescriptors());
        }
        finally
        {
            foreach (var descriptor in descriptors)
            {
                _ = Interop.Close(descriptor);
            }
        }
    }

    [Fact]
    public void PerMessageCredentialsAreTheSenderOnes()
    {
        var (sender, receiver, directory) = CreatePair();
        using var sending = sender;
        using var receiving = receiver;
        using var scratch = directory;

        Assert.True(UnixSocket.TrySetPassCredentials(receiver, true, out var error));
        Assert.Equal(LinuxErrno.None, error);
        Assert.True(UnixSocket.TrySend(sender, "x"u8, [], out _, out _));

        var payload = new byte[16];
        Span<int> none = [];

        Assert.True(UnixSocket.TryReceive(receiver, payload, none, out _, out var credentials, out error));
        Assert.Equal(LinuxErrno.None, error);
        Assert.NotNull(credentials);
        Assert.Equal(Environment.ProcessId, credentials!.Value.ProcessId);
        Assert.Equal(Interop.GetEuid(), credentials.Value.UserId);
        Assert.Equal(Interop.GetEgid(), credentials.Value.GroupId);
    }

    [Fact]
    public void AShortPayloadBufferReportsTruncation()
    {
        var (sender, receiver, directory) = CreateDatagramPair();
        using var sending = sender;
        using var receiving = receiver;
        using var scratch = directory;

        Assert.True(UnixSocket.TrySend(sender, new byte[16], [], out _, out _));

        var payload = new byte[4];
        Span<int> none = [];

        Assert.True(UnixSocket.TryReceive(receiver, payload, none, out var message, out var error));
        Assert.Equal(LinuxErrno.None, error);
        Assert.Equal(4, message.Length);
        Assert.True(message.Truncated);
    }

    private static (Socket Sender, Socket Receiver, TempDirectory Directory) CreatePair()
    {
        var directory = new TempDirectory();
        var path = directory.FullPath("pair.sock");

        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(1);

        var sender = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        sender.Connect(new UnixDomainSocketEndPoint(path));
        var receiver = listener.Accept();
        listener.Dispose();
        return (sender, receiver, directory);
    }

    private static (Socket Sender, Socket Receiver, TempDirectory Directory) CreateDatagramPair()
    {
        var directory = new TempDirectory();
        var senderPath = directory.FullPath("sender.sock");
        var receiverPath = directory.FullPath("receiver.sock");

        var sender = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
        sender.Bind(new UnixDomainSocketEndPoint(senderPath));
        var receiver = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
        receiver.Bind(new UnixDomainSocketEndPoint(receiverPath));

        sender.Connect(new UnixDomainSocketEndPoint(receiverPath));
        receiver.Connect(new UnixDomainSocketEndPoint(senderPath));
        return (sender, receiver, directory);
    }

    private static int OpenNull()
    {
        var path = "/dev/null\0"u8;

        unsafe
        {
            fixed (byte* pointer = path)
            {
                return Interop.OpenAt(Interop.AtCurrentWorkingDirectory, pointer, Interop.OpenReadOnly, 0);
            }
        }
    }

    private static int CountNullDescriptors()
    {
        var count = 0;

        foreach (var path in Directory.GetFiles("/proc/self/fd"))
        {
            try
            {
                if (new FileInfo(path).LinkTarget == "/dev/null")
                {
                    count++;
                }
            }
            catch (IOException)
            {
            }
        }

        return count;
    }
}
