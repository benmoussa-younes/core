using System.Text;
using Core.Platform.Linux;
using Microsoft.Win32.SafeHandles;

namespace Core.Platform.Linux.Tests;

[Collection("process-limits")]
public sealed partial class UnixPseudoTerminalTests
{
    private const int WindowSizeRequest = 0x5413;

    [Fact]
    public void CreateReturnsAMasterAndASlavePath()
    {
        var created = UnixPseudoTerminal.Create();

        Assert.True(created.IsSuccess, created.TryGetError(out var error) ? error!.Message : string.Empty);

        using var terminal = created.Value;

        Assert.True(terminal.MasterDescriptor >= 0);
        Assert.StartsWith("/dev/pts/", terminal.SlavePath, StringComparison.Ordinal);
        Assert.True(File.Exists(terminal.SlavePath), "the slave device does not exist.");
        Assert.Equal(
            Interop.FileDescriptorCloseOnExec,
            Interop.Fcntl(terminal.MasterDescriptor, Interop.FileDescriptorStatusCommand, 0) &
                Interop.FileDescriptorCloseOnExec);
    }

    [Fact]
    public async Task TheSlaveReadsWhatTheMasterWrites()
    {
        using var terminal = Create();
        using var slave = File.OpenHandle(terminal.SlavePath, FileMode.Open, FileAccess.ReadWrite);
        var bytes = "terminal-data\n"u8.ToArray();

        Assert.Equal(bytes.Length, Write(terminal.MasterDescriptor, bytes));

        var buffer = new byte[bytes.Length];
        var read = await Task.Run(() => Read((int)slave.DangerousGetHandle(), buffer))
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(bytes.Length, read);
        Assert.Equal("terminal-data\n", Encoding.UTF8.GetString(buffer));
    }

    [Fact]
    public void TheSizeRoundTripsOnceTheSlaveIsOpen()
    {
        using var terminal = Create();

        Assert.True(terminal.TryGetSize(out var initial, out var initialError));
        Assert.Equal(LinuxErrno.None, initialError);
        Assert.Equal(new UnixTerminalSize(0, 0), initial);

        Assert.True(terminal.TrySetSize(new UnixTerminalSize(37, 111), out var setError));
        Assert.Equal(LinuxErrno.None, setError);

        using var slave = File.OpenHandle(terminal.SlavePath, FileMode.Open, FileAccess.ReadWrite);

        Assert.True(terminal.TryGetSize(out var size, out var getError));
        Assert.Equal(LinuxErrno.None, getError);
        Assert.Equal(new UnixTerminalSize(37, 111), size);

        Span<ushort> values = stackalloc ushort[4];

        unsafe
        {
            fixed (ushort* pointer = values)
            {
                Assert.Equal(
                    0,
                    Interop.Ioctl((int)slave.DangerousGetHandle(), WindowSizeRequest, pointer));
            }
        }

        Assert.Equal(37, values[0]);
        Assert.Equal(111, values[1]);
    }

    [Fact]
    public void DisposeClosesTheMaster()
    {
        var created = UnixPseudoTerminal.Create();

        Assert.True(created.IsSuccess);

        var terminal = created.Value;
        _ = terminal.MasterDescriptor;
        terminal.Dispose();

        Assert.Throws<ObjectDisposedException>(() => terminal.MasterDescriptor);
        Assert.False(terminal.TryGetSize(out _, out var error));
        Assert.Equal(LinuxErrno.BadFileDescriptor, error);
        Assert.False(terminal.TrySetSize(new UnixTerminalSize(24, 80), out var setError));
        Assert.Equal(LinuxErrno.BadFileDescriptor, setError);
    }

    private static UnixPseudoTerminal Create()
    {
        var created = UnixPseudoTerminal.Create();

        Assert.True(created.IsSuccess, created.TryGetError(out var error) ? error!.Message : string.Empty);
        return created.Value;
    }

    private static unsafe int Write(int descriptor, byte[] bytes)
    {
        fixed (byte* pointer = bytes)
        {
            return WriteCore(descriptor, pointer, bytes.Length);
        }
    }

    private static unsafe int Read(int descriptor, byte[] bytes)
    {
        fixed (byte* pointer = bytes)
        {
            return ReadCore(descriptor, pointer, bytes.Length);
        }
    }

    [System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    private static unsafe partial int WriteCore(int descriptor, byte* buffer, nint count);

    [System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    private static unsafe partial int ReadCore(int descriptor, byte* buffer, nint count);
}
