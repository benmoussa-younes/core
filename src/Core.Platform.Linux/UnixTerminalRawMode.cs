using System.Runtime.Versioning;
using Core.Results;

namespace Core.Platform.Linux;

/// <summary>
/// Puts a terminal in raw mode for the lifetime of the object, so bytes pass
/// through unprocessed, and restores the previous attributes on dispose.
/// </summary>
/// <remarks>
/// The attributes are the <c>cfmakeraw</c> set: input bytes are no longer mapped
/// or stripped, output is not post-processed, the line discipline stops
/// buffering canonical lines, signalling and extended input are off, the
/// character size is eight bits, and a read returns at least one byte as soon as
/// it arrives. The layout stays internal; the restore uses <c>TCSAFLUSH</c> and
/// is best effort: a failed restore is not reported, and a <c>tcsetattr</c> that
/// fails when enabling puts the saved attributes back. The class holds no
/// reference to the descriptor and never closes it. The library does not own the
/// signal disposition: if the process is signalled while the mode is raw, the
/// terminal keeps the raw attributes.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class UnixTerminalRawMode : IDisposable
{
    private const int MinimumOffset = 6;
    private const int TimeoutOffset = 5;
    private const uint IgnoreBreak = 0x1;
    private const uint BreakInterrupt = 0x2;
    private const uint MarkParity = 0x8;
    private const uint StripEighthBit = 0x20;
    private const uint MapNewlineToCarriageReturn = 0x40;
    private const uint IgnoreCarriageReturn = 0x80;
    private const uint MapCarriageReturnToNewline = 0x100;
    private const uint EnableOutputFlowControl = 0x400;
    private const uint PostProcessOutput = 0x1;
    private const uint EnableSignals = 0x1;
    private const uint CanonicalMode = 0x2;
    private const uint Echo = 0x8;
    private const uint EchoNewline = 0x40;
    private const uint ExtendedInput = 0x8000;
    private const uint CharacterSizeMask = 0x30;
    private const uint ParityEnable = 0x100;
    private const uint CharacterSizeEight = 0x30;

    private readonly int _descriptor;
    private readonly byte[] _original;
    private bool _disposed;

    private UnixTerminalRawMode(int descriptor, byte[] original)
    {
        _descriptor = descriptor;
        _original = original;
    }

    /// <summary>Puts a terminal descriptor in raw mode.</summary>
    /// <param name="descriptor">The terminal descriptor to change.</param>
    /// <returns>The scope that restores the previous attributes, or a failure naming the <c>errno</c>.</returns>
    public static unsafe Result<UnixTerminalRawMode> Enable(int descriptor)
    {
        if (!OperatingSystem.IsLinux())
        {
            return Failure("termios", (int)LinuxErrno.FunctionNotImplemented);
        }

        var original = new byte[Termios.Size];

        if (!Termios.TryRead(descriptor, original, out var readError))
        {
            return Failure("tcgetattr", (int)readError);
        }

        var modified = (byte[])original.Clone();
        var input = BitConverter.ToUInt32(modified, Termios.InputFlagsOffset) &
            ~(IgnoreBreak | BreakInterrupt | MarkParity | StripEighthBit | MapNewlineToCarriageReturn |
                IgnoreCarriageReturn | MapCarriageReturnToNewline | EnableOutputFlowControl);
        var output = BitConverter.ToUInt32(modified, Termios.OutputFlagsOffset) & ~PostProcessOutput;
        var control = (BitConverter.ToUInt32(modified, Termios.ControlFlagsOffset) &
            ~(CharacterSizeMask | ParityEnable)) | CharacterSizeEight;
        var local = BitConverter.ToUInt32(modified, Termios.LocalFlagsOffset) &
            ~(EnableSignals | CanonicalMode | Echo | EchoNewline | ExtendedInput);

        BitConverter.TryWriteBytes(modified.AsSpan(Termios.InputFlagsOffset, 4), input);
        BitConverter.TryWriteBytes(modified.AsSpan(Termios.OutputFlagsOffset, 4), output);
        BitConverter.TryWriteBytes(modified.AsSpan(Termios.ControlFlagsOffset, 4), control);
        BitConverter.TryWriteBytes(modified.AsSpan(Termios.LocalFlagsOffset, 4), local);
        modified[Termios.ControlCharactersOffset + MinimumOffset] = 1;
        modified[Termios.ControlCharactersOffset + TimeoutOffset] = 0;

        if (!Termios.TryWrite(descriptor, modified, out var writeError))
        {
            _ = Termios.TryWrite(descriptor, original, out _);
            return Failure("tcsetattr", (int)writeError);
        }

        return Result<UnixTerminalRawMode>.Success(new UnixTerminalRawMode(descriptor, original));
    }

    /// <summary>Restores the terminal attributes saved when raw mode was enabled.</summary>
    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _ = Termios.TryWrite(_descriptor, _original, out _);
    }

    private static Result<UnixTerminalRawMode> Failure(string call, int errno)
    {
        return Result<UnixTerminalRawMode>.Failure(
            new CoreError(CoreErrorCodes.UnixTerminalFailed, $"{call} failed: {(LinuxErrno)errno}."));
    }
}
