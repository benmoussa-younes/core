using System.Diagnostics;
using System.Globalization;
using Core.Results;
using Microsoft.Win32.SafeHandles;

namespace Core.Platform.Linux.Tests;

[Collection("process-limits")]
public sealed class UnixPipeTests
{
    private const int FileDescriptorFlagsCommand = 1;
    private const int CloseOnExecFlag = 1;

    [Fact]
    public void BothEndsAreCloseOnExec()
    {
        var created = UnixPipe.Create();

        Assert.True(created.IsSuccess);

        using var pipe = created.Value;
        var readFlags = Interop.Fcntl(pipe.ReadDescriptor, FileDescriptorFlagsCommand, 0);

        Assert.True(readFlags >= 0);
        Assert.NotEqual(0, readFlags & CloseOnExecFlag);

        var writeFlags = Interop.Fcntl(pipe.WriteDescriptor, FileDescriptorFlagsCommand, 0);

        Assert.True(writeFlags >= 0);
        Assert.NotEqual(0, writeFlags & CloseOnExecFlag);
    }

    [Fact]
    public async Task TheWriteEndDeliversTheBytesAndTheReaderSeesEndOfFile()
    {
        var created = UnixPipe.Create();

        Assert.True(created.IsSuccess);

        using var pipe = created.Value;
        Assert.True(pipe.TryWriteAll("secret"u8, out var written, out var error));
        Assert.Equal(6, written);
        Assert.Equal(LinuxErrno.None, error);

        pipe.CloseWrite();
        Assert.Throws<ObjectDisposedException>(() => pipe.WriteDescriptor);

        using var handle = new SafeFileHandle((IntPtr)pipe.ReadDescriptor, ownsHandle: false);
        using var reader = new FileStream(handle, FileAccess.Read, bufferSize: 1);
        var buffer = new byte[16];
        var count = await reader.ReadAsync(
            buffer.AsMemory(),
            TestContext.Current.CancellationToken).AsTask().WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Equal("secret"u8.ToArray(), buffer[..count].ToArray());

        var eof = await reader.ReadAsync(
            buffer.AsMemory(),
            TestContext.Current.CancellationToken).AsTask().WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, eof);
    }

    [Fact]
    public void AWriteToAClosedReadEndFailsWithBrokenPipe()
    {
        var created = UnixPipe.Create();

        Assert.True(created.IsSuccess);

        using var pipe = created.Value;
        pipe.CloseRead();
        Assert.False(pipe.TryWriteAll("x"u8, out var written, out var error));
        Assert.Equal(0, written);
        Assert.Equal(LinuxErrno.BrokenPipe, error);
    }

    [Fact]
    public void DisposingClosesBothDescriptors()
    {
        var created = UnixPipe.Create();

        Assert.True(created.IsSuccess);

        using var pipe = created.Value;
        var target = DescriptorTarget(pipe.ReadDescriptor);

        Assert.NotNull(target);

        pipe.Dispose();

        Assert.DoesNotContain(target!, DescriptorTargets());
        Assert.Throws<ObjectDisposedException>(() => pipe.ReadDescriptor);
        Assert.Throws<ObjectDisposedException>(() => pipe.WriteDescriptor);
    }

    [Fact]
    public void AnEmptyWriteSucceeds()
    {
        var created = UnixPipe.Create();

        Assert.True(created.IsSuccess);

        using var pipe = created.Value;

        Assert.True(pipe.TryWriteAll([], out var written, out var error));
        Assert.Equal(0, written);
        Assert.Equal(LinuxErrno.None, error);
    }

    [Fact]
    public void WritingAfterCloseWriteThrows()
    {
        var created = UnixPipe.Create();

        Assert.True(created.IsSuccess);

        using var pipe = created.Value;
        pipe.CloseWrite();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = pipe.TryWriteAll("x"u8, out _, out _);
        });
    }

    [Fact]
    public void ClosingTwiceIsIdempotent()
    {
        var created = UnixPipe.Create();

        Assert.True(created.IsSuccess);

        using var pipe = created.Value;
        pipe.CloseRead();
        pipe.CloseRead();
        pipe.CloseWrite();
        pipe.CloseWrite();
        pipe.Dispose();
        pipe.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pipe.ReadDescriptor);
        Assert.Throws<ObjectDisposedException>(() => pipe.WriteDescriptor);
    }

    [Fact]
    public async Task ALargeWriteIsDeliveredCompletely()
    {
        var created = UnixPipe.Create();

        Assert.True(created.IsSuccess);

        using var pipe = created.Value;
        var payload = new byte[4 * 1024 * 1024];

        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)(index % 251);
        }

        var read = Task.Run(() =>
        {
            using var handle = new SafeFileHandle((IntPtr)pipe.ReadDescriptor, ownsHandle: false);
            using var stream = new FileStream(handle, FileAccess.Read, bufferSize: 64 * 1024);
            using var received = new MemoryStream(payload.Length);
            var buffer = new byte[64 * 1024];
            int count;

            while ((count = stream.Read(buffer)) > 0)
            {
                received.Write(buffer, 0, count);
            }

            return received.ToArray();
        });

        var write = Task.Run(() =>
        {
            var ok = pipe.TryWriteAll(payload, out var written, out var error);
            return (ok, written, error);
        });

        var result = await write.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        Assert.True(result.ok);
        Assert.Equal(payload.Length, result.written);
        Assert.Equal(LinuxErrno.None, result.error);

        pipe.CloseWrite();

        var received = await read.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        Assert.Equal(payload, received);
    }

    [Fact]
    public async Task AReaderClosingMidWriteFailsWithThePrefix()
    {
        var created = UnixPipe.Create();

        Assert.True(created.IsSuccess);

        using var pipe = created.Value;
        var payload = new byte[4 * 1024 * 1024];

        var write = Task.Run(() =>
        {
            var ok = pipe.TryWriteAll(payload, out var written, out var error);
            return (ok, written, error);
        });

        using (var handle = new SafeFileHandle((IntPtr)pipe.ReadDescriptor, ownsHandle: false))
        using (var stream = new FileStream(handle, FileAccess.Read, bufferSize: 64 * 1024))
        {
            var buffer = new byte[64 * 1024];
            var count = await stream.ReadAsync(
                buffer.AsMemory(),
                TestContext.Current.CancellationToken).AsTask().WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Assert.True(count > 0);
        }

        pipe.CloseRead();

        var result = await write.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        Assert.False(result.ok);
        Assert.True(result.written > 0);
        Assert.True(result.written < payload.Length);
        Assert.Equal(LinuxErrno.BrokenPipe, result.error);
    }

    [Fact]
    public void APipeEndOnDescriptorZeroIsClosed()
    {
        var probe = Path.Combine(AppContext.BaseDirectory, "unix-probe", "Core.Unix.Probe.dll");

        Assert.True(File.Exists(probe), $"the probe is not copied to the test output: {probe}");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(probe);
        startInfo.ArgumentList.Add("--fd0");

        using var process = Process.Start(startInfo)!;

        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        Assert.True(process.ExitCode == 0, $"exit={process.ExitCode} out={output} err={error}");
    }

    [Fact]
    public void APipeThatCannotBeCreatedReportsUnixPipe()
    {
        Assert.True(UnixProcessLimits.TryGet(UnixResource.NoFile, out var original, out _));

        try
        {
            var lowered = new UnixResourceLimit(3, original.Hard);
            Assert.True(UnixProcessLimits.TrySet(UnixResource.NoFile, lowered, out _));

            var created = UnixPipe.Create();

            Assert.True(created.IsFailure);
            Assert.Equal(CoreErrorCodes.UnixPipeFailed, created.Error.Code);
        }
        finally
        {
            Assert.True(UnixProcessLimits.TrySet(UnixResource.NoFile, original, out _));
        }
    }

    private static string? DescriptorTarget(int descriptor)
    {
        return new FileInfo(Path.Combine(
            "/proc/self/fd",
            descriptor.ToString(CultureInfo.InvariantCulture))).LinkTarget;
    }

    private static IEnumerable<string?> DescriptorTargets()
    {
        foreach (var path in Directory.EnumerateFiles("/proc/self/fd"))
        {
            yield return new FileInfo(path).LinkTarget;
        }
    }
}
