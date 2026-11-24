using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Core.Results;

namespace Core.Platform.Linux;

/// <summary>
/// Starts a child process without a shell, with an explicit environment, an
/// explicit working directory and inherited descriptors.
/// </summary>
/// <remarks>
/// The program is started with <c>posix_spawn</c>: no shell, no <c>PATH</c>
/// lookup and no <c>system</c>. The environment and the working directory are
/// the request's, not the parent's. Descriptors 0, 1 and 2 are inherited unless
/// mapped. The <c>/proc/self/fd</c> scan closes every descriptor it observes
/// except the sources and the targets; each source that is not a target is
/// closed in the child after its duplication. <c>addclosefrom_np</c> closes the
/// close-from base, 3 or the highest target plus one, and above, including a
/// descriptor opened after the scan. A descriptor at or above the soft
/// <c>RLIMIT_NOFILE</c> cannot be closed by a file action, so the spawn is
/// refused with <see cref="LinuxErrno.InvalidArgument"/> when one is observed.
/// The child is a session leader and its <c>SIGPIPE</c> disposition is reset to
/// the default at exec; a .NET child's runtime may ignore it again when it
/// initialises. The spawn flags set <c>SETSIGDEF</c>, <c>SETSIGMASK</c> and
/// <c>SETSID</c>. A descriptor another thread opens after the scan and below the
/// base can be inherited, so the caller keeps its other descriptors
/// close-on-exec or ensures no other thread opens a non-CLOEXEC descriptor while
/// the spawn runs. A source that is also a target is duplicated in the parent,
/// so the mapping is independent of the descriptor numbers. The caller reaps the
/// child with <see cref="UnixChildProcess.TryWait"/>; the close-from action needs
/// glibc 2.34 or later (Debian 12 and Ubuntu 22.04 ship newer).
/// </remarks>
[SupportedOSPlatform("linux")]
public static unsafe class UnixProcessSpawn
{
    private const short SpawnFlags = 0x8c;

    private const int SignalPipe = 13;
    private const int MaximumTargetDescriptor = 1_048_576;
    private const int StandardDescriptorCount = 3;
    private const int BufferWords = 64;
    private const int SignalWords = 16;

    /// <summary>Starts a child process.</summary>
    /// <param name="request">The program, arguments, working directory, environment and descriptor mappings.</param>
    /// <returns>The child handle, or a failure with code <see cref="CoreErrorCodes.UnixSpawnFailed"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/>, its program, its arguments, its working directory
    /// or its environment is <see langword="null"/>.
    /// </exception>
    public static Result<UnixChildProcess> Start(UnixSpawnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Program);
        ArgumentNullException.ThrowIfNull(request.Arguments);
        ArgumentNullException.ThrowIfNull(request.WorkingDirectory);
        ArgumentNullException.ThrowIfNull(request.Environment);

        if (!OperatingSystem.IsLinux())
        {
            return Failure(LinuxErrno.FunctionNotImplemented, "posix_spawn is only available on Linux.");
        }

        if (!UnixProcessLimits.TryGet(UnixResource.NoFile, out var limit, out var limitError))
        {
            return Failure(limitError, "the file descriptor limit could not be read.");
        }

        var invalid = Validate(request, limit);

        if (invalid is not null)
        {
            return Result<UnixChildProcess>.Failure(invalid);
        }

        var mappings = request.Descriptors ?? [];
        var argumentValues = new List<string>(request.Arguments.Count + 1) { request.Program };
        argumentValues.AddRange(request.Arguments);
        var environmentValues = new List<string>(request.Environment.Count);

        foreach (var pair in request.Environment)
        {
            environmentValues.Add($"{pair.Key}={pair.Value}");
        }

        using var arguments = Vector.Create(argumentValues);
        using var environment = Vector.Create(environmentValues);
        var programBytes = Encoding.UTF8.GetBytes(request.Program + "\0");
        var directoryBytes = Encoding.UTF8.GetBytes(request.WorkingDirectory + "\0");
        var duplicates = new List<int>();
        var attributeWords = stackalloc ulong[BufferWords];
        var actionWords = stackalloc ulong[BufferWords];
        var signalWords = stackalloc ulong[SignalWords];
        var maskWords = stackalloc ulong[SignalWords];
        var attributes = (byte*)attributeWords;
        var actions = (byte*)actionWords;
        var defaultSignals = (byte*)signalWords;
        var maskSignals = (byte*)maskWords;
        var actionsInitialized = false;
        var attributesInitialized = false;

        try
        {
            var targets = new HashSet<int>(mappings.Select(static mapping => mapping.TargetDescriptor));
            var sources = new HashSet<int>(mappings.Select(static mapping => mapping.SourceDescriptor));
            var closeDescriptors = new SortedSet<int>();
            var maximumTarget = targets.Count == 0 ? 0 : targets.Max();
            var closeFrom = Math.Max(StandardDescriptorCount, maximumTarget + 1);

            var actionsResult = Interop.PosixSpawnFileActionsInit(actions);

            if (actionsResult != 0)
            {
                return Failure((LinuxErrno)actionsResult, "the file actions could not be initialized.");
            }

            actionsInitialized = true;

            foreach (var mapping in mappings)
            {
                var source = mapping.SourceDescriptor;

                if (targets.Contains(source))
                {
                    var duplicate = Interop.Fcntl(
                        source,
                        Interop.FileDescriptorDuplicateCloseOnExec,
                        closeFrom);

                    if (duplicate < 0)
                    {
                        return Failure(
                            (LinuxErrno)Marshal.GetLastPInvokeError(),
                            "a source descriptor could not be duplicated.");
                    }

                    duplicates.Add(duplicate);
                    sources.Add(duplicate);
                    source = duplicate;
                }

                var added = Interop.PosixSpawnFileActionsAddDup2(actions, source, mapping.TargetDescriptor);

                if (added != 0)
                {
                    return Failure((LinuxErrno)added, "a descriptor could not be mapped.");
                }
            }

            foreach (var source in sources)
            {
                if (source >= StandardDescriptorCount && !targets.Contains(source))
                {
                    if ((ulong)source >= limit.Soft)
                    {
                        return AboveLimit();
                    }

                    closeDescriptors.Add(source);
                }
            }

            try
            {
                foreach (var path in Directory.EnumerateFiles("/proc/self/fd"))
                {
                    if (int.TryParse(
                            Path.GetFileName(path),
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var descriptor) &&
                        descriptor >= StandardDescriptorCount &&
                        !targets.Contains(descriptor) &&
                        !sources.Contains(descriptor))
                    {
                        if ((ulong)descriptor >= limit.Soft)
                        {
                            return AboveLimit();
                        }

                        closeDescriptors.Add(descriptor);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Failure(LinuxErrno.InputOutput, "the open descriptors could not be read.");
            }

            foreach (var descriptor in closeDescriptors)
            {
                var closed = Interop.PosixSpawnFileActionsAddClose(actions, descriptor);

                if (closed != 0)
                {
                    return Failure((LinuxErrno)closed, "a descriptor could not be closed in the child.");
                }
            }

            fixed (byte* directory = directoryBytes)
            {
                var changed = Interop.PosixSpawnFileActionsAddChdir(actions, directory);

                if (changed != 0)
                {
                    return Failure((LinuxErrno)changed, "the working directory could not be set.");
                }
            }

            var fromResult = Interop.PosixSpawnFileActionsAddCloseFrom(actions, closeFrom);

            if (fromResult != 0)
            {
                return Failure((LinuxErrno)fromResult, "the remaining descriptors could not be closed.");
            }

            var attributesResult = Interop.PosixSpawnAttrInit(attributes);

            if (attributesResult != 0)
            {
                return Failure((LinuxErrno)attributesResult, "the spawn attributes could not be initialized.");
            }

            attributesInitialized = true;

            if (Interop.SigEmptySet(defaultSignals) != 0 ||
                Interop.SigAddSet(defaultSignals, SignalPipe) != 0 ||
                Interop.SigEmptySet(maskSignals) != 0)
            {
                return Failure(
                    (LinuxErrno)Marshal.GetLastPInvokeError(),
                    "the child signal sets could not be built.");
            }

            var defaultResult = Interop.PosixSpawnAttrSetSigDefault(attributes, defaultSignals);

            if (defaultResult != 0)
            {
                return Failure((LinuxErrno)defaultResult, "the default signal set could not be applied.");
            }

            var maskResult = Interop.PosixSpawnAttrSetSigMask(attributes, maskSignals);

            if (maskResult != 0)
            {
                return Failure((LinuxErrno)maskResult, "the signal mask could not be applied.");
            }

            var flagsResult = Interop.PosixSpawnAttrSetFlags(attributes, SpawnFlags);

            if (flagsResult != 0)
            {
                return Failure((LinuxErrno)flagsResult, "the spawn flags could not be applied.");
            }

            fixed (byte* program = programBytes)
            {
                var spawned = Interop.PosixSpawn(
                    out var processId,
                    program,
                    actions,
                    attributes,
                    arguments.Pointers,
                    environment.Pointers);

                if (spawned != 0)
                {
                    var error = (LinuxErrno)spawned;
                    return Failure(error, $"the program could not be started: {error}.");
                }

                return Result<UnixChildProcess>.Success(new UnixChildProcess(processId));
            }
        }
        finally
        {
            if (actionsInitialized)
            {
                _ = Interop.PosixSpawnFileActionsDestroy(actions);
            }

            if (attributesInitialized)
            {
                _ = Interop.PosixSpawnAttrDestroy(attributes);
            }

            foreach (var duplicate in duplicates)
            {
                _ = Interop.Close(duplicate);
            }
        }
    }

    private static CoreError? Validate(UnixSpawnRequest request, UnixResourceLimit limit)
    {
        if (string.IsNullOrEmpty(request.Program) ||
            request.Program.Contains('\0') ||
            !Path.IsPathFullyQualified(request.Program))
        {
            return Invalid("the program must be an absolute path without a NUL.");
        }

        if (string.IsNullOrEmpty(request.WorkingDirectory) ||
            request.WorkingDirectory.Contains('\0') ||
            !Path.IsPathFullyQualified(request.WorkingDirectory))
        {
            return Invalid("the working directory must be an absolute path without a NUL.");
        }

        foreach (var argument in request.Arguments)
        {
            if (argument is null || argument.Contains('\0'))
            {
                return Invalid("an argument contains a NUL.");
            }
        }

        foreach (var pair in request.Environment)
        {
            if (string.IsNullOrEmpty(pair.Key) ||
                pair.Key.Contains('=') ||
                pair.Key.Contains('\0') ||
                pair.Value is null ||
                pair.Value.Contains('\0'))
            {
                return Invalid("an environment entry is invalid.");
            }
        }

        var targets = new HashSet<int>();

        foreach (var mapping in request.Descriptors ?? [])
        {
            if (mapping.SourceDescriptor < 0 ||
                mapping.TargetDescriptor < 0 ||
                (ulong)mapping.TargetDescriptor >= limit.Soft ||
                mapping.TargetDescriptor > MaximumTargetDescriptor)
            {
                return Invalid("a descriptor number is invalid.");
            }

            if (!targets.Add(mapping.TargetDescriptor))
            {
                return Invalid("two mappings share a target descriptor.");
            }

            if (Interop.Fcntl(mapping.SourceDescriptor, Interop.FileDescriptorStatusCommand, 0) < 0)
            {
                return Invalid("a source descriptor is not open.");
            }
        }

        return null;
    }

    private static Result<UnixChildProcess> AboveLimit()
    {
        return Failure(
            LinuxErrno.InvalidArgument,
            "a descriptor at or above the soft file descriptor limit cannot be closed in the child; " +
            "close it or raise the limit.");
    }

    private static CoreError Invalid(string message)
    {
        return new CoreError(CoreErrorCodes.UnixSpawnFailed, message);
    }

    private static Result<UnixChildProcess> Failure(LinuxErrno error, string message)
    {
        return Result<UnixChildProcess>.Failure(
            new CoreError(CoreErrorCodes.UnixSpawnFailed, $"{message} ({error})"));
    }

    private sealed unsafe class Vector : IDisposable
    {
        private readonly GCHandle[] _pins;
        private readonly byte** _pointers;

        private Vector(GCHandle[] pins, byte** pointers)
        {
            _pins = pins;
            _pointers = pointers;
        }

        internal byte** Pointers => _pointers;

        internal static Vector Create(List<string> values)
        {
            var pointers = (byte**)NativeMemory.Alloc((nuint)(values.Count + 1), (nuint)sizeof(byte*));
            var pins = new GCHandle[values.Count];

            for (var index = 0; index < values.Count; index++)
            {
                var bytes = Encoding.UTF8.GetBytes(values[index] + "\0");
                pins[index] = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                pointers[index] = (byte*)pins[index].AddrOfPinnedObject();
            }

            pointers[values.Count] = null;
            return new Vector(pins, pointers);
        }

        public void Dispose()
        {
            NativeMemory.Free(_pointers);

            foreach (var pin in _pins)
            {
                pin.Free();
            }
        }
    }
}
