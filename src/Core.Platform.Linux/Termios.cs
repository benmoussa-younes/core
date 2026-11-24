using System.Runtime.InteropServices;

namespace Core.Platform.Linux;

/// <summary>
/// The Linux <c>termios</c> layout shared by the terminal wrappers.
/// </summary>
/// <remarks>
/// LP64 x86-64: four 32-bit flag words, the line discipline byte, 32 control
/// characters at offset 17, then the two speeds, for 60 bytes. The layout stays
/// internal; the wrappers expose flags, not the struct.
/// </remarks>
internal static class Termios
{
    internal const int Size = 60;
    internal const int InputFlagsOffset = 0;
    internal const int OutputFlagsOffset = 4;
    internal const int ControlFlagsOffset = 8;
    internal const int LocalFlagsOffset = 12;
    internal const int ControlCharactersOffset = 17;
    internal const int FlushOnSet = 2;

    internal static unsafe bool TryRead(int descriptor, Span<byte> attributes, out LinuxErrno error)
    {
        fixed (byte* pointer = attributes)
        {
            if (Interop.TcGetAttr(descriptor, pointer) != 0)
            {
                error = (LinuxErrno)Marshal.GetLastPInvokeError();
                return false;
            }
        }

        error = LinuxErrno.None;
        return true;
    }

    internal static unsafe bool TryWrite(int descriptor, ReadOnlySpan<byte> attributes, out LinuxErrno error)
    {
        fixed (byte* pointer = attributes)
        {
            if (Interop.TcSetAttr(descriptor, FlushOnSet, pointer) != 0)
            {
                error = (LinuxErrno)Marshal.GetLastPInvokeError();
                return false;
            }
        }

        error = LinuxErrno.None;
        return true;
    }
}
