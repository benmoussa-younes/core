using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Core.Results;

namespace Core.Platform.Linux.Tests;

[Collection("process-limits")]
public sealed partial class UnixProcessSpawnTests
{
    private const int DuplicateDescriptorCommand = 0;

    private static readonly string ProbePath = Path.Combine(
        AppContext.BaseDirectory,
        "unix-probe",
        "Core.Unix.Probe.dll");

    private static readonly string Dotnet = ResolveDotnet();

    [Fact]
    public async Task TheChildReadsDescriptorThree()
    {
        using var temp = new TempDirectory();
        var facts = temp.FullPath("facts.txt");
        var parent = OpenParentDescriptor("/etc/hostname", 64);
        Environment.SetEnvironmentVariable("U2_PARENT_SENTINEL", "leak-me");
        var previousMask = BlockSignalTerm();

        try
        {
            var created = UnixPipe.Create();

            Assert.True(created.IsSuccess);

            using var secret = created.Value;
            var source = secret.ReadDescriptor;
            var sourceTarget = DescriptorTarget(source);
            var parentTarget = Math.Max(parent, source) + 1;
            var started = StartProbe(
                facts,
                "3",
                temp.Path,
                new Dictionary<string, string> { ["U2_CHILD_VALUE"] = "explicit" },
                [new UnixDescriptorMapping(source, 3), new UnixDescriptorMapping(parent, parentTarget)]);

            Assert.True(
                started.IsSuccess,
                started.TryGetError(out var startError) ? startError!.Message : string.Empty);

            using var child = started.Value;

            Assert.True(secret.TryWriteAll("spawn-secret"u8, out var written, out var writeError));
            Assert.Equal(12, written);
            Assert.Equal(LinuxErrno.None, writeError);
            secret.CloseWrite();

            var status = await WaitForExitAsync(child, TimeSpan.FromSeconds(30));

            Assert.True(status.Exited);
            Assert.Equal(0, status.ExitCode);

            var lines = File.ReadAllLines(facts);

            Assert.Contains("FD3=spawn-secret", lines);
            Assert.Equal([parentTarget], DescriptorsPointingTo(lines, "/etc/hostname"));
            Assert.Equal([3], DescriptorsPointingTo(lines, sourceTarget));
            Assert.Contains("SIGTERM_BLOCKED=0", lines);
            Assert.Contains($"CWD={temp.Path}", lines);

            var environment = Fact(lines, "ENV");

            Assert.Contains("U2_CHILD_VALUE=explicit", environment, StringComparison.Ordinal);
            Assert.DoesNotContain("U2_PARENT_SENTINEL", environment, StringComparison.Ordinal);
        }
        finally
        {
            RestoreSignalMask(previousMask);
            Environment.SetEnvironmentVariable("U2_PARENT_SENTINEL", null);
            _ = Interop.Close(parent);
        }
    }

    [Fact]
    public async Task AHighDescriptorIsClosed()
    {
        using var temp = new TempDirectory();
        var facts = temp.FullPath("facts.txt");
        var created = UnixPipe.Create();

        Assert.True(created.IsSuccess);

        using var secret = created.Value;
        var source = secret.ReadDescriptor;
        var below = OpenParentDescriptor("/etc/hostname", 4);
        var target = Math.Max(source, below) + 1;
        var above = OpenParentDescriptor("/etc/hostname", target + 64);

        try
        {
            var started = StartProbe(
                facts,
                target.ToString(CultureInfo.InvariantCulture),
                temp.Path,
                new Dictionary<string, string>(),
                [new UnixDescriptorMapping(source, target)]);

            Assert.True(started.IsSuccess);

            using var child = started.Value;

            Assert.True(secret.TryWriteAll("spawn-secret"u8, out _, out _));
            secret.CloseWrite();

            var status = await WaitForExitAsync(child, TimeSpan.FromSeconds(30));

            Assert.True(status.Exited);
            Assert.Equal(0, status.ExitCode);

            var lines = File.ReadAllLines(facts);

            Assert.Contains($"FD{target}=spawn-secret", lines);
            Assert.Empty(DescriptorsPointingTo(lines, "/etc/hostname"));
        }
        finally
        {
            _ = Interop.Close(below);
            _ = Interop.Close(above);
        }
    }

    [Fact]
    public async Task ASourceThatIsAnotherMappingsTargetIsDuplicated()
    {
        using var temp = new TempDirectory();
        var facts = temp.FullPath("facts.txt");
        var parent = OpenParentDescriptor("/etc/hostname", 64);

        try
        {
            var first = UnixPipe.Create();
            Assert.True(first.IsSuccess);
            using var firstSecret = first.Value;
            var second = UnixPipe.Create();
            Assert.True(second.IsSuccess);
            using var secondSecret = second.Value;
            var target = secondSecret.ReadDescriptor;
            var secondTarget = target + 1;
            var started = StartProbe(
                facts,
                $"{target},{secondTarget}",
                temp.Path,
                new Dictionary<string, string>(),
                [
                    new UnixDescriptorMapping(firstSecret.ReadDescriptor, target),
                    new UnixDescriptorMapping(secondSecret.ReadDescriptor, secondTarget),
                ]);

            Assert.True(started.IsSuccess);

            using var child = started.Value;

            Assert.True(firstSecret.TryWriteAll("first-secret"u8, out _, out _));
            firstSecret.CloseWrite();
            Assert.True(secondSecret.TryWriteAll("second-secret"u8, out _, out _));
            secondSecret.CloseWrite();

            var status = await WaitForExitAsync(child, TimeSpan.FromSeconds(30));

            Assert.True(status.Exited);
            Assert.Equal(0, status.ExitCode);

            var lines = File.ReadAllLines(facts);

            Assert.Contains($"FD{target}=first-secret", lines);
            Assert.Contains($"FD{secondTarget}=second-secret", lines);
            Assert.Empty(DescriptorsPointingTo(lines, "/etc/hostname"));
        }
        finally
        {
            _ = Interop.Close(parent);
        }
    }

    [Fact]
    public async Task TheExitCodeIsDecoded()
    {
        using var temp = new TempDirectory();
        var started = StartProbe(["--exit", "3"], temp.Path);

        Assert.True(started.IsSuccess);

        using var child = started.Value;
        var status = await WaitForExitAsync(child, TimeSpan.FromSeconds(30));

        Assert.True(status.Exited);
        Assert.Equal(3, status.ExitCode);
        Assert.Equal(0, status.Signal);
    }

    [Fact]
    public async Task TheSignalIsDecoded()
    {
        using var temp = new TempDirectory();
        var started = StartProbe(["--sleep"], temp.Path);

        Assert.True(started.IsSuccess);

        using var child = started.Value;

        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.False(child.TryWait(out _, out var runningError));
        Assert.Equal(LinuxErrno.None, runningError);
        Assert.True(child.TrySignal(9, false, out var signalError));
        Assert.Equal(LinuxErrno.None, signalError);

        var status = await WaitForExitAsync(child, TimeSpan.FromSeconds(30));

        Assert.False(status.Exited);
        Assert.Equal(9, status.Signal);
        Assert.Equal(137, status.ExitCode);
    }

    [Fact]
    public async Task TheGrandchildInTheGroupIsSignaled()
    {
        using var temp = new TempDirectory();
        var pidPath = temp.FullPath("grandchild.pid");
        var started = StartProbe(["--sleep-grandchild", pidPath], temp.Path);

        Assert.True(started.IsSuccess);

        using var child = started.Value;
        int grandchild;

        try
        {
            grandchild = await WaitForPidAsync(pidPath);
        }
        catch
        {
            child.TrySignal(9, true, out _);
            child.TryWait(out _, out _);
            throw;
        }

        Assert.True(child.TrySignal(15, true, out var signalError));
        Assert.Equal(LinuxErrno.None, signalError);

        var status = await WaitForExitAsync(child, TimeSpan.FromSeconds(30));

        Assert.False(status.Exited);
        Assert.Equal(15, status.Signal);
        await WaitForDeathAsync(grandchild);
    }

    [Fact]
    public async Task TheChildSignalPipeIsResetToTheDefault()
    {
        using var temp = new TempDirectory();
        var previous = IgnoreSignalPipe();

        try
        {
            var started = UnixProcessSpawn.Start(new UnixSpawnRequest(
                "/bin/sh",
                ["-c", "kill -PIPE $$"],
                temp.Path,
                new Dictionary<string, string>()));

            Assert.True(started.IsSuccess);

            using var child = started.Value;
            var status = await WaitForExitAsync(child, TimeSpan.FromSeconds(30));

            Assert.False(status.Exited);
            Assert.Equal(13, status.Signal);
        }
        finally
        {
            RestoreSignalPipe(previous);
        }
    }

    [Fact]
    public async Task TheReapedChildKeepsItsStatus()
    {
        using var temp = new TempDirectory();
        var started = StartProbe(["--exit", "3"], temp.Path);

        Assert.True(started.IsSuccess);

        using var child = started.Value;
        var status = await WaitForExitAsync(child, TimeSpan.FromSeconds(30));

        Assert.True(child.TryWait(out var again, out var waitError));
        Assert.Equal(status, again);
        Assert.Equal(LinuxErrno.None, waitError);
        Assert.False(child.TrySignal(15, false, out var signalError));
        Assert.Equal(LinuxErrno.NoSuchProcess, signalError);
    }

    [Fact]
    public void AChildReapedElsewhereIsNotSignaled()
    {
        using var temp = new TempDirectory();
        var started = StartProbe(["--sleep"], temp.Path);

        Assert.True(started.IsSuccess);

        using var child = started.Value;

        Assert.True(child.TrySignal(9, false, out _));
        Assert.Equal(child.ProcessId, Interop.WaitPid(child.ProcessId, out _, 0));

        Assert.False(child.TryWait(out _, out var waitError));
        Assert.Equal(LinuxErrno.NoChild, waitError);
        Assert.False(child.TrySignal(15, false, out var signalError));
        Assert.Equal(LinuxErrno.NoSuchProcess, signalError);
    }

    [Fact]
    public void AFailedSpawnClosesTheDuplicatedDescriptor()
    {
        using var temp = new TempDirectory();
        var created = UnixPipe.Create();

        Assert.True(created.IsSuccess);

        using var source = created.Value;
        var warmup = UnixProcessSpawn.Start(new UnixSpawnRequest(
            "/nonexistent/program",
            [],
            temp.Path,
            new Dictionary<string, string>(),
            [new UnixDescriptorMapping(source.ReadDescriptor, source.ReadDescriptor)]));

        Assert.True(warmup.IsFailure);

        var before = CountDescriptors();

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var started = UnixProcessSpawn.Start(new UnixSpawnRequest(
                "/nonexistent/program",
                [],
                temp.Path,
                new Dictionary<string, string>(),
                [new UnixDescriptorMapping(source.ReadDescriptor, source.ReadDescriptor)]));

            Assert.True(started.IsFailure);
        }

        Assert.Equal(before, CountDescriptors());
    }

    [Fact]
    public void TheProbeSpawnSelfTestPasses()
    {
        Assert.True(File.Exists(ProbePath), $"the probe is not copied to the test output: {ProbePath}");

        var startInfo = new ProcessStartInfo(Dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(ProbePath);
        startInfo.ArgumentList.Add("--self-test");

        using var process = Process.Start(startInfo)!;

        if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        Assert.True(process.ExitCode == 0, $"exit={process.ExitCode} out={output} err={error}");
    }

    [Fact]
    public void AnInvalidProgramIsRefused()
    {
        using var temp = new TempDirectory();
        var started = UnixProcessSpawn.Start(new UnixSpawnRequest(
            "/nonexistent/program",
            [],
            temp.Path,
            new Dictionary<string, string>()));

        Assert.True(started.IsFailure);
        Assert.True(started.TryGetError(out var error));
        Assert.Equal(CoreErrorCodes.UnixSpawnFailed, error!.Code);
        Assert.Contains("NoEntry", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvalidSourceDescriptorIsRefused()
    {
        using var temp = new TempDirectory();
        var started = UnixProcessSpawn.Start(new UnixSpawnRequest(
            "/bin/true",
            [],
            temp.Path,
            new Dictionary<string, string>(),
            [new UnixDescriptorMapping(999, 3)]));

        Assert.True(started.IsFailure);
        Assert.True(started.TryGetError(out var error));
        Assert.Equal(CoreErrorCodes.UnixSpawnFailed, error!.Code);
        Assert.Contains("source descriptor", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void AnInvalidRequestIsRefused(UnixSpawnRequest request)
    {
        var started = UnixProcessSpawn.Start(request);

        Assert.True(started.IsFailure);
        Assert.Equal(CoreErrorCodes.UnixSpawnFailed, started.Error.Code);
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return [new UnixSpawnRequest("/bin/true", [], "/nonexistent", new Dictionary<string, string>())];
        yield return
        [
            new UnixSpawnRequest(
                "/bin/true",
                [],
                "/tmp",
                new Dictionary<string, string>(),
                [new UnixDescriptorMapping(0, 1_048_577)]),
        ];
        yield return
        [
            new UnixSpawnRequest(
                "/bin/true",
                [],
                "/tmp",
                new Dictionary<string, string>(),
                [new UnixDescriptorMapping(0, 3), new UnixDescriptorMapping(1, 3)]),
        ];
        yield return [new UnixSpawnRequest("/bin/true", [], "/tmp", new Dictionary<string, string> { ["A=B"] = "x" })];
        yield return [new UnixSpawnRequest("bin/true", [], "/tmp", new Dictionary<string, string>())];
        yield return [new UnixSpawnRequest("bin\0true", [], "/tmp", new Dictionary<string, string>())];
    }

    private static Result<UnixChildProcess> StartProbe(
        string facts,
        string readDescriptors,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        UnixDescriptorMapping[] mappings)
    {
        Assert.True(File.Exists(ProbePath), $"the probe is not copied to the test output: {ProbePath}");

        var arguments = new List<string>
        {
            ProbePath,
            "--spawn-child",
            facts,
            readDescriptors,
        };

        return UnixProcessSpawn.Start(new UnixSpawnRequest(
            Dotnet,
            arguments,
            workingDirectory,
            environment,
            mappings));
    }

    private static Result<UnixChildProcess> StartProbe(string[] childArguments, string workingDirectory)
    {
        Assert.True(File.Exists(ProbePath), $"the probe is not copied to the test output: {ProbePath}");

        var arguments = new List<string> { ProbePath };
        arguments.AddRange(childArguments);

        return UnixProcessSpawn.Start(new UnixSpawnRequest(
            Dotnet,
            arguments,
            workingDirectory,
            new Dictionary<string, string>(),
            null));
    }

    private static async Task<UnixExitStatus> WaitForExitAsync(UnixChildProcess child, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (child.TryWait(out var status, out var error))
            {
                return status;
            }

            if (error != LinuxErrno.None)
            {
                throw new InvalidOperationException($"the wait failed: {error}.");
            }

            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        child.TrySignal(9, true, out _);
        child.TryWait(out var killed, out _);
        throw new TimeoutException($"the child did not exit: {killed}.");
    }

    private static async Task<int> WaitForPidAsync(string path)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path);

                if (text.Length > 0)
                {
                    return int.Parse(text, CultureInfo.InvariantCulture);
                }
            }

            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("the grandchild identifier was not written.");
    }

    private static async Task WaitForDeathAsync(int processId)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (Interop.Kill(processId, 0) != 0)
            {
                return;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"the grandchild {processId} survived the group signal.");
    }

    private static int CountDescriptors()
    {
        return Directory.GetFiles("/proc/self/fd").Length;
    }

    private static string Fact(string[] facts, string key)
    {
        return facts.Single(line => line.StartsWith(key + "=", StringComparison.Ordinal))[(key.Length + 1)..];
    }

    private static string DescriptorTarget(int descriptor)
    {
        return new FileInfo($"/proc/self/fd/{descriptor}").LinkTarget ?? "closed";
    }

    private static int[] DescriptorsPointingTo(string[] facts, string target)
    {
        const string Prefix = "TARGET_";
        var suffix = "=" + target;

        return facts
            .Where(line => line.StartsWith(Prefix, StringComparison.Ordinal) &&
                line.EndsWith(suffix, StringComparison.Ordinal))
            .Select(line => int.Parse(
                line[Prefix.Length..line.IndexOf('=', Prefix.Length)],
                CultureInfo.InvariantCulture))
            .Order()
            .ToArray();
    }

    private static unsafe int OpenFile(string path)
    {
        var bytes = Encoding.UTF8.GetBytes(path + "\0");

        fixed (byte* pointer = bytes)
        {
            return Interop.OpenAt(Interop.AtCurrentWorkingDirectory, pointer, Interop.OpenReadOnly, 0);
        }
    }

    private static int OpenParentDescriptor(string path, int minimum)
    {
        var source = OpenFile(path);

        Assert.True(source >= 0, $"the parent descriptor could not be opened: {source}.");

        var duplicate = Interop.Fcntl(source, DuplicateDescriptorCommand, minimum);

        Assert.Equal(0, Interop.Close(source));
        Assert.True(duplicate >= minimum, $"the parent descriptor could not be duplicated: {duplicate}.");
        return duplicate;
    }

    private static unsafe byte[] IgnoreSignalPipe()
    {
        var previous = new byte[152];
        var action = new byte[152];
        action[0] = 1;

        fixed (byte* current = action, old = previous)
        {
            Assert.Equal(0, SigAction(13, current, old));
        }

        return previous;
    }

    private static unsafe void RestoreSignalPipe(byte[] previous)
    {
        fixed (byte* old = previous)
        {
            Assert.Equal(0, SigAction(13, old, null));
        }
    }

    private static unsafe byte[] BlockSignalTerm()
    {
        var previous = new byte[128];
        var set = new byte[128];

        fixed (byte* currentSet = set, old = previous)
        {
            Assert.Equal(0, SigEmptySet(currentSet));
            Assert.Equal(0, SigAddSet(currentSet, 15));
            Assert.Equal(0, SigProcMask(0, currentSet, old));
        }

        return previous;
    }

    private static unsafe void RestoreSignalMask(byte[] previous)
    {
        fixed (byte* old = previous)
        {
            Assert.Equal(0, SigProcMask(2, old, null));
        }
    }

    private static string ResolveDotnet()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.GetFullPath(Path.Combine(directory, "dotnet"));

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("dotnet was not found on PATH.");
    }

    [LibraryImport("libc", EntryPoint = "sigaction", SetLastError = true)]
    private static unsafe partial int SigAction(int signal, byte* action, byte* oldAction);

    [LibraryImport("libc", EntryPoint = "sigprocmask", SetLastError = true)]
    private static unsafe partial int SigProcMask(int how, byte* set, byte* oldSet);

    [LibraryImport("libc", EntryPoint = "sigemptyset", SetLastError = true)]
    private static unsafe partial int SigEmptySet(byte* set);

    [LibraryImport("libc", EntryPoint = "sigaddset", SetLastError = true)]
    private static unsafe partial int SigAddSet(byte* set, int signal);
}
