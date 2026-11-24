using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Runtime.InteropServices;
using Core.Platform.Linux;
using Core.Results;
using Microsoft.Win32.SafeHandles;

namespace Core.Unix.Probe;

internal static partial class SelfTest
{
    private const int SetDescriptorFlags = 2;
    private const int DuplicateDescriptorFlags = 0;
    private const int TermiosSize = 60;
    private const short PollReadable = 1;
    private const int ReadTimeoutMilliseconds = 2000;
    private const int LocalFlagsOffset = 12;
    private const uint LocalMask = 0x804B;
    private const int DuplicateCloseOnExecFlags = 1030;

    public static int Run()
    {
        var failures = 0;
        failures += Check("peer credentials of a connected Unix socket", PeerCredentialsOfAConnectedUnixSocket());
        failures += Check("no peer on a listening socket", NoPeerOnAListeningSocket());
        failures += Check("locked memory round trip", LockedMemoryRoundTrip());
        failures += Check("dumpable round trip", DumpableRoundTrip());
        failures += Check("thread name round trip", ThreadNameRoundTrip());
        failures += Check("resource limit read", ResourceLimitRead());
        failures += Check("resource limit write", ResourceLimitWrite());
        failures += Check("mask scope", MaskScope());
        failures += Check("terminal echo round trip", TerminalEchoRoundTrip());
        failures += Check("descriptor round trip", DescriptorRoundTrip());
        failures += Check("pipe round trip", PipeRoundTrip());
        failures += Check("per-message credentials", PerMessageCredentials());
        failures += Check("no new privileges", NoNewPrivileges());
        failures += Check("spawn round trip", SpawnRoundTrip());
        failures += Check("spawn refuses a descriptor above a lowered limit", RefusesADescriptorAboveALoweredLimit());
        failures += Check("spawn with a target at the lowered limit", SpawnsWithATargetAtTheLoweredLimit());
        failures += Check("pseudo-terminal round trip", PseudoTerminalRoundTrip());
        failures += Check("raw terminal mode round trip", RawTerminalModeRoundTrip());
        failures += Check("descriptor zero", Program.DescriptorZero());
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

    private static bool ResourceLimitWrite()
    {
        if (!UnixProcessLimits.TryGet(UnixResource.NoFile, out var original, out _) || original.Soft == 0)
        {
            return false;
        }

        var changed = original with { Soft = original.Soft - 1 };
        var applied = UnixProcessLimits.TrySet(UnixResource.NoFile, changed, out _) &&
            UnixProcessLimits.TryGet(UnixResource.NoFile, out var read, out _) &&
            read == changed;

        _ = UnixProcessLimits.TrySet(UnixResource.NoFile, original, out _);
        return applied;
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

    private static bool DescriptorRoundTrip()
    {
        var (sender, receiver, directory) = CreateConnectedPair();

        try
        {
            using var handle = File.OpenHandle("/dev/null", FileMode.Open, FileAccess.Read);
            var source = (int)handle.DangerousGetHandle();

            if (!UnixSocket.TrySend(sender, "x"u8, [source], out _, out _))
            {
                return false;
            }

            var payload = new byte[16];
            Span<int> received = stackalloc int[1];

            if (!UnixSocket.TryReceive(receiver, payload, received, out var message, out _) ||
                message.Length != 1 ||
                message.DescriptorCount != 1)
            {
                return false;
            }

            _ = Close(received[0]);
            return true;
        }
        finally
        {
            sender.Dispose();
            receiver.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static bool PipeRoundTrip()
    {
        var created = UnixPipe.Create();

        if (!created.IsSuccess)
        {
            return false;
        }

        using var pipe = created.Value;

        if (!pipe.TryWriteAll("probe"u8, out var written, out _) || written != 5)
        {
            return false;
        }

        pipe.CloseWrite();

        using var handle = new SafeFileHandle((IntPtr)pipe.ReadDescriptor, ownsHandle: false);
        using var reader = new FileStream(handle, FileAccess.Read, bufferSize: 1);
        var buffer = new byte[8];
        var read = reader.ReadAsync(buffer, CancellationToken.None).AsTask();

        if (!read.Wait(TimeSpan.FromSeconds(5)))
        {
            return false;
        }

        var count = read.Result;
        return count == 5 && buffer.AsSpan(0, count).SequenceEqual("probe"u8);
    }

    private static bool PerMessageCredentials()
    {
        var (sender, receiver, directory) = CreateConnectedPair();

        try
        {
            if (!UnixSocket.TrySetPassCredentials(receiver, true, out _))
            {
                return false;
            }

            if (!UnixSocket.TrySend(sender, "x"u8, [], out _, out _))
            {
                return false;
            }

            var payload = new byte[16];
            Span<int> none = [];

            if (!UnixSocket.TryReceive(receiver, payload, none, out _, out var credentials, out _) ||
                credentials is null)
            {
                return false;
            }

            var (userId, groupId) = ReadEffectiveIds();
            return credentials.Value.ProcessId == Environment.ProcessId &&
                credentials.Value.UserId == userId &&
                credentials.Value.GroupId == groupId;
        }
        finally
        {
            sender.Dispose();
            receiver.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static (Socket Sender, Socket Receiver, string Directory) CreateConnectedPair()
    {
        var directory = Directory.CreateTempSubdirectory("core-unix-probe-").FullName;
        var path = Path.Combine(directory, "pair.sock");

        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(1);

        var sender = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        sender.Connect(new UnixDomainSocketEndPoint(path));
        var receiver = listener.Accept();
        listener.Dispose();
        return (sender, receiver, directory);
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

            if (!UnixTerminalEcho.TryGetEcho(descriptor, out var before, out _) || !before)
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
                if (!UnixTerminalEcho.TryGetEcho(descriptor, out var off, out _) || off)
                {
                    return false;
                }
            }

            return UnixTerminalEcho.TryGetEcho(descriptor, out var restored, out _) && restored;
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

    private struct PollDescriptor
    {
        internal int Descriptor;
        internal short Events;
        internal short Ready;
    }

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static unsafe partial int Poll(void* descriptors, nuint count, int timeoutMilliseconds);

    [LibraryImport("libc", EntryPoint = "tcgetattr", SetLastError = true)]
    private static unsafe partial int TcGetAttr(int descriptor, byte* attributes);

    private static bool SpawnRoundTrip()
    {
        var directory = Directory.CreateTempSubdirectory("core-spawn-probe-");
        var above = -1;

        try
        {
            var facts = Path.Combine(directory.FullName, "facts.txt");
            using var parentFile = File.OpenHandle("/etc/hostname", FileMode.Open, FileAccess.Read);
            var parent = (int)parentFile.DangerousGetHandle();

            if (Program.Fcntl(parent, SetDescriptorFlags, 0) != 0)
            {
                return false;
            }

            above = Program.Fcntl(parent, DuplicateDescriptorFlags, 64);

            if (above < 0)
            {
                return false;
            }

            var created = UnixPipe.Create();

            if (!created.IsSuccess)
            {
                return false;
            }

            using var secret = created.Value;
            var (program, spawnArguments) = SpawnCommand(["--spawn-child", facts, "40"]);
            var request = new UnixSpawnRequest(
                program,
                spawnArguments,
                directory.FullName,
                new Dictionary<string, string> { ["PROBE_VALUE"] = "spawn" },
                [new UnixDescriptorMapping(secret.ReadDescriptor, 40)]);
            var started = UnixProcessSpawn.Start(request);

            if (!started.IsSuccess)
            {
                return false;
            }

            using var child = started.Value;

            if (!secret.TryWriteAll("spawn-secret"u8, out var written, out _) || written != 12)
            {
                return false;
            }

            secret.CloseWrite();

            if (!WaitForSpawn(child, out var status) || !status.Exited || status.ExitCode != 0)
            {
                return false;
            }

            var lines = File.ReadAllLines(facts);
            return lines.Contains("FD40=spawn-secret") &&
                !lines.Any(line => line.StartsWith("TARGET_", StringComparison.Ordinal) &&
                    line.EndsWith("=/etc/hostname", StringComparison.Ordinal)) &&
                lines.Any(line => line.Contains("PROBE_VALUE=spawn", StringComparison.Ordinal));
        }
        finally
        {
            if (above >= 0)
            {
                _ = Program.Close(above);
            }

            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    private static bool RefusesADescriptorAboveALoweredLimit()
    {
        var directory = Directory.CreateTempSubdirectory("core-spawn-limit-");
        var above = -1;
        UnixResourceLimit? previous = null;

        try
        {
            var facts = Path.Combine(directory.FullName, "facts.txt");
            using var parentFile = File.OpenHandle("/etc/hostname", FileMode.Open, FileAccess.Read);
            var parent = (int)parentFile.DangerousGetHandle();
            var loweredLimit = HighestDescriptor() + 32;

            if (Program.Fcntl(parent, SetDescriptorFlags, 0) != 0)
            {
                return false;
            }

            above = Program.Fcntl(parent, DuplicateDescriptorFlags, loweredLimit + 1);

            if (above <= loweredLimit || !LowerFileLimit(loweredLimit, out previous))
            {
                return false;
            }

            var created = UnixPipe.Create();

            if (!created.IsSuccess)
            {
                return false;
            }

            using var secret = created.Value;
            var started = StartSpawn(directory.FullName, facts, secret, loweredLimit - 1);

            return started.IsFailure &&
                started.TryGetError(out var error) &&
                error!.Code == CoreErrorCodes.UnixSpawnFailed &&
                error.Message.Contains("soft file descriptor limit", StringComparison.Ordinal);
        }
        finally
        {
            RestoreFileLimit(previous);

            if (above >= 0)
            {
                _ = Program.Close(above);
            }

            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    private static bool SpawnsWithATargetAtTheLoweredLimit()
    {
        var directory = Directory.CreateTempSubdirectory("core-spawn-target-");
        var above = -1;
        UnixResourceLimit? previous = null;

        try
        {
            var facts = Path.Combine(directory.FullName, "facts.txt");
            using var parentFile = File.OpenHandle("/etc/hostname", FileMode.Open, FileAccess.Read);
            var parent = (int)parentFile.DangerousGetHandle();
            var loweredLimit = HighestDescriptor() + 32;
            above = Program.Fcntl(parent, DuplicateCloseOnExecFlags, loweredLimit + 1);

            if (above <= loweredLimit || !LowerFileLimit(loweredLimit, out previous))
            {
                return false;
            }

            var created = UnixPipe.Create();

            if (!created.IsSuccess)
            {
                return false;
            }

            using var secret = created.Value;
            var target = loweredLimit - 1;
            var started = StartSpawn(directory.FullName, facts, secret, target);

            if (!started.IsSuccess)
            {
                return false;
            }

            using var child = started.Value;

            if (!secret.TryWriteAll("spawn-secret"u8, out _, out _))
            {
                return false;
            }

            secret.CloseWrite();

            if (!WaitForSpawn(child, out var status) || !status.Exited || status.ExitCode != 0)
            {
                return false;
            }

            var lines = File.ReadAllLines(facts);
            return lines.Contains($"FD{target}=spawn-secret") &&
                !lines.Any(line => line.StartsWith("TARGET_", StringComparison.Ordinal) &&
                    line.EndsWith("=/etc/hostname", StringComparison.Ordinal));
        }
        finally
        {
            RestoreFileLimit(previous);

            if (above >= 0)
            {
                _ = Program.Close(above);
            }

            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    private static int HighestDescriptor()
    {
        return Directory.GetFiles("/proc/self/fd")
            .Select(path => int.Parse(Path.GetFileName(path), CultureInfo.InvariantCulture))
            .Max();
    }

    private static bool LowerFileLimit(int limit, out UnixResourceLimit? previous)
    {
        previous = null;

        if (!UnixProcessLimits.TryGet(UnixResource.NoFile, out var current, out _) ||
            !UnixProcessLimits.TrySet(UnixResource.NoFile, new UnixResourceLimit((ulong)limit, current.Hard), out _))
        {
            return false;
        }

        previous = current;
        return true;
    }

    private static void RestoreFileLimit(UnixResourceLimit? previous)
    {
        if (previous is not null)
        {
            _ = UnixProcessLimits.TrySet(UnixResource.NoFile, previous.Value, out _);
        }
    }

    private static Result<UnixChildProcess> StartSpawn(
        string workingDirectory,
        string facts,
        UnixPipe secret,
        int target)
    {
        var (program, spawnArguments) = SpawnCommand(
            ["--spawn-child", facts, target.ToString(CultureInfo.InvariantCulture)]);

        return UnixProcessSpawn.Start(new UnixSpawnRequest(
            program,
            spawnArguments,
            workingDirectory,
            new Dictionary<string, string>(),
            [new UnixDescriptorMapping(secret.ReadDescriptor, target)]));
    }

    private static unsafe bool PseudoTerminalRoundTrip()
    {
        var directory = Directory.CreateTempSubdirectory("core-pty-probe-");

        try
        {
            var facts = Path.Combine(directory.FullName, "facts.txt");
            var created = UnixPseudoTerminal.Create();

            if (!created.IsSuccess)
            {
                return false;
            }

            using var terminal = created.Value;

            if (!terminal.TrySetSize(new UnixTerminalSize(37, 111), out _))
            {
                return false;
            }

            var pipe = UnixPipe.Create();

            if (!pipe.IsSuccess)
            {
                return false;
            }

            using var secret = pipe.Value;
            var (program, spawnArguments) = SpawnCommand(["--spawn-child", facts, "3"]);
            var started = UnixProcessSpawn.Start(new UnixSpawnRequest(
                program,
                spawnArguments,
                directory.FullName,
                new Dictionary<string, string>(),
                [new UnixDescriptorMapping(secret.ReadDescriptor, 3)],
                terminal));

            if (!started.IsSuccess)
            {
                return false;
            }

            using var child = started.Value;
            using var master = new FileStream(
                new SafeFileHandle((IntPtr)terminal.MasterDescriptor, ownsHandle: false),
                FileAccess.ReadWrite,
                bufferSize: 1);

            if (!secret.TryWriteAll("pty-secret"u8, out _, out _))
            {
                return false;
            }

            secret.CloseWrite();
            master.Write("ping\n"u8);

            if (!WaitForSpawn(child, out var status) || !status.Exited || status.ExitCode != 0)
            {
                return false;
            }

            var lines = File.ReadAllLines(facts);
            var buffer = new byte[512];
            var poll = new PollDescriptor { Descriptor = terminal.MasterDescriptor, Events = PollReadable };
            var count = Poll(&poll, 1, ReadTimeoutMilliseconds) > 0 ? master.Read(buffer) : 0;
            var output = count <= 0 ? string.Empty : Encoding.UTF8.GetString(buffer, 0, count);

            return lines.Contains("ISATTY0=1") &&
                lines.Contains("WINSIZE=37,111") &&
                lines.Contains("STDIN=ping") &&
                lines.Contains("FD3=pty-secret") &&
                output.Contains("pty-child", StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    private static bool RawTerminalModeRoundTrip()
    {
        var created = UnixPseudoTerminal.Create();

        if (!created.IsSuccess)
        {
            return false;
        }

        using var terminal = created.Value;
        using var slave = File.OpenHandle(terminal.SlavePath, FileMode.Open, FileAccess.ReadWrite);
        var descriptor = (int)slave.DangerousGetHandle();
        var original = new byte[TermiosSize];

        if (ReadAttributes(descriptor, original) != 0)
        {
            return false;
        }

        var enabled = UnixTerminalRawMode.Enable(descriptor);

        if (!enabled.IsSuccess)
        {
            return false;
        }

        var raw = new byte[TermiosSize];

        using (enabled.Value)
        {
            if (ReadAttributes(descriptor, raw) != 0)
            {
                return false;
            }
        }

        var restored = new byte[TermiosSize];

        return ReadAttributes(descriptor, restored) == 0 &&
            (BitConverter.ToUInt32(raw, LocalFlagsOffset) & LocalMask) == 0 &&
            restored.AsSpan().SequenceEqual(original);
    }

    private static unsafe int ReadAttributes(int descriptor, byte[] attributes)
    {
        fixed (byte* pointer = attributes)
        {
            return TcGetAttr(descriptor, pointer);
        }
    }

    private static (string Program, string[] Arguments) SpawnCommand(string[] childArguments)
    {
        var process = Environment.ProcessPath!;

        if (Path.GetFileName(process) == "dotnet")
        {
            var assembly = Path.Combine(AppContext.BaseDirectory, "Core.Unix.Probe.dll");
            return (process, [assembly, .. childArguments]);
        }

        return (process, childArguments);
    }

    private static bool WaitForSpawn(UnixChildProcess child, out UnixExitStatus status)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (child.TryWait(out status, out var error))
            {
                return true;
            }

            if (error != LinuxErrno.None)
            {
                return false;
            }

            Thread.Sleep(10);
        }

        child.TrySignal(9, true, out _);
        status = default;
        return false;
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
