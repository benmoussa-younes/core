// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Core.Platform.Linux;

/// <summary>
/// The Unix domain socket surface: the peer identity (<c>SO_PEERCRED</c>), the
/// per-message credentials (<c>SO_PASSCRED</c>) and ancillary file descriptors
/// (<c>SCM_RIGHTS</c>) sent and received with <c>sendmsg</c>/<c>recvmsg</c>.
/// </summary>
/// <remarks>
/// The peer-identity call reads the option through the BCL and adds no direct
/// libc import; the descriptor path imports <c>sendmsg</c> and <c>recvmsg</c>.
/// <see cref="TryGetPeerCredentials"/> requires a connected stream socket: a
/// listening socket has no peer and a datagram socket reports the invalid
/// credentials (<c>uid</c> = <see cref="uint.MaxValue"/>); an unconnected socket
/// is refused earlier with <see cref="LinuxErrno.NotConnected"/>.
/// <c>TrySend</c> and <c>TryReceive</c> accept a connected socket;
/// a stream socket attaches the descriptors to the payload and rejects an empty
/// payload with descriptors, while a connected datagram socket carries a
/// descriptor without payload. A received descriptor is either handed to the
/// caller or closed by the call: it is never leaked.
/// </remarks>
[SupportedOSPlatform("linux")]
public static class UnixSocket
{
    private const int SocketLevel = 1;
    private const int PassCredentialsOption = 16;
    private const int PeerCredentialsOption = 17;
    private const int CredentialsSize = 12;
    private const int MessageHeaderSize = 56;
    private const int IoVectorSize = 16;
    private const int ControlHeaderSize = 16;
    private const int RightsControl = 1;
    private const int CredentialsControl = 2;
    private const int MessageNoSignal = 0x4000;
    private const int MessageControlCloseOnExec = 0x40000000;
    private const int MessageTruncated = 0x20;
    private const int MessageControlTruncated = 0x8;
    private const int CredentialsControlSpace = 32;
    // WHY: SCM_MAX_FD is the kernel's per-message descriptor limit.
    private const int MaximumDescriptors = 253;

    /// <summary>
    /// Reads the credentials of the peer connected to a Unix domain socket.
    /// </summary>
    /// <param name="socket">The connected stream socket.</param>
    /// <param name="credentials">The peer's identifiers when the call succeeds; the default value otherwise.</param>
    /// <param name="error">
    /// The <c>errno</c> when the call fails, or <see cref="LinuxErrno.InvalidArgument"/>
    /// when the kernel returned an unexpected option length;
    /// <see cref="LinuxErrno.None"/> otherwise.
    /// </param>
    /// <returns><see langword="true"/> when the kernel reported the peer's credentials.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="socket"/> is <see langword="null"/>.</exception>
    public static bool TryGetPeerCredentials(
        Socket socket,
        out UnixPeerCredentials credentials,
        out LinuxErrno error)
    {
        ArgumentNullException.ThrowIfNull(socket);

        credentials = default;
        error = LinuxErrno.None;

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        if (!socket.Connected)
        {
            error = LinuxErrno.NotConnected;
            return false;
        }

        Span<byte> buffer = stackalloc byte[CredentialsSize];

        try
        {
            if (socket.GetRawSocketOption(SocketLevel, PeerCredentialsOption, buffer) < CredentialsSize)
            {
                error = LinuxErrno.InvalidArgument;
                return false;
            }
        }
        catch (SocketException exception)
        {
            error = (LinuxErrno)exception.NativeErrorCode;
            return false;
        }

        if (!TryReadPeerIdentity(buffer, out credentials))
        {
            error = LinuxErrno.ProtocolNotAvailable;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Enables or disables the delivery of the sender's credentials with every
    /// received message (<c>SO_PASSCRED</c>).
    /// </summary>
    /// <param name="socket">The receiving socket.</param>
    /// <param name="enabled">Whether each message carries its sender's credentials.</param>
    /// <param name="error">The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when the kernel accepted the option.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="socket"/> is <see langword="null"/>.</exception>
    public static bool TrySetPassCredentials(Socket socket, bool enabled, out LinuxErrno error)
    {
        ArgumentNullException.ThrowIfNull(socket);

        error = LinuxErrno.None;

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        Span<byte> value = stackalloc byte[4];
        BitConverter.TryWriteBytes(value, enabled ? 1 : 0);

        try
        {
            socket.SetRawSocketOption(SocketLevel, PassCredentialsOption, value);
        }
        catch (SocketException exception)
        {
            error = (LinuxErrno)exception.NativeErrorCode;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Sends a payload with file descriptors attached (<c>SCM_RIGHTS</c>).
    /// </summary>
    /// <param name="socket">The connected socket, stream or datagram.</param>
    /// <param name="payload">The bytes to send; it must not be empty when descriptors are attached on a stream socket.</param>
    /// <param name="descriptors">The descriptors the receiver obtains a copy of.</param>
    /// <param name="sent">The number of payload bytes the kernel accepted.</param>
    /// <param name="error">The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when the message was sent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="socket"/> is <see langword="null"/>.</exception>
    public static unsafe bool TrySend(
        Socket socket,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<int> descriptors,
        out int sent,
        out LinuxErrno error)
    {
        ArgumentNullException.ThrowIfNull(socket);

        sent = 0;
        error = LinuxErrno.None;

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        if (!socket.Connected)
        {
            error = LinuxErrno.NotConnected;
            return false;
        }

        if (descriptors.Length > MaximumDescriptors)
        {
            error = LinuxErrno.InvalidArgument;
            return false;
        }

        // WHY: a stream socket attaches the control data to the first payload byte and drops it without one.
        if (payload.IsEmpty && !descriptors.IsEmpty && socket.SocketType == SocketType.Stream)
        {
            error = LinuxErrno.InvalidArgument;
            return false;
        }

        var controlSize = unchecked(ControlHeaderSize + (descriptors.Length * 4));
        ulong* controlWords = stackalloc ulong[descriptors.IsEmpty ? 1 : (controlSize + 7) / 8];
        byte* control = (byte*)controlWords;

        if (!descriptors.IsEmpty)
        {
            *(nuint*)control = (nuint)controlSize;
            *(int*)(control + 8) = SocketLevel;
            *(int*)(control + 12) = RightsControl;

            for (var index = 0; index < descriptors.Length; index++)
            {
                *(int*)(control + ControlHeaderSize + (index * 4)) = descriptors[index];
            }
        }

        ulong* headerWords = stackalloc ulong[MessageHeaderSize / 8];
        byte* header = (byte*)headerWords;
        new Span<byte>(header, MessageHeaderSize).Clear();

        ulong* vectorWords = stackalloc ulong[IoVectorSize / 8];
        byte* vector = (byte*)vectorWords;

        fixed (byte* payloadPointer = payload)
        {
            *(void**)vector = payloadPointer;
            *(nuint*)(vector + 8) = (nuint)payload.Length;
            *(void**)(header + 16) = vector;
            *(nuint*)(header + 24) = 1;
            *(void**)(header + 32) = descriptors.IsEmpty ? null : control;
            *(nuint*)(header + 40) = descriptors.IsEmpty ? 0 : (nuint)controlSize;

            var result = Interop.SendMsg((int)socket.Handle, header, MessageNoSignal);

            if (result < 0)
            {
                error = (LinuxErrno)Marshal.GetLastPInvokeError();
                return false;
            }

            sent = (int)result;
            return true;
        }
    }

    /// <summary>
    /// Receives a payload and its attached file descriptors.
    /// </summary>
    /// <param name="socket">The connected socket, stream or datagram.</param>
    /// <param name="payload">The buffer the payload is copied into.</param>
    /// <param name="descriptors">The buffer that receives the attached descriptors, one per slot.</param>
    /// <param name="message">The received length, descriptor count and truncation flag when the call succeeds.</param>
    /// <param name="error">The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when a message was received.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="socket"/> is <see langword="null"/>.</exception>
    public static bool TryReceive(
        Socket socket,
        Span<byte> payload,
        Span<int> descriptors,
        out UnixMessage message,
        out LinuxErrno error)
    {
        return TryReceive(socket, payload, descriptors, out message, out _, out error);
    }

    /// <summary>
    /// Receives a payload, its attached file descriptors and its sender's
    /// credentials when <c>SO_PASSCRED</c> is enabled.
    /// </summary>
    /// <param name="socket">The connected socket, stream or datagram.</param>
    /// <param name="payload">The buffer the payload is copied into.</param>
    /// <param name="descriptors">The buffer that receives the attached descriptors, one per slot.</param>
    /// <param name="message">The received length, descriptor count and truncation flag when the call succeeds.</param>
    /// <param name="credentials">The sender's credentials, or <see langword="null"/> when the message carries none.</param>
    /// <param name="error">The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when a message was received.</returns>
    /// <remarks>
    /// A stream peer that has closed is reported as a successful message whose
    /// <see cref="UnixMessage.Length"/> and <see cref="UnixMessage.DescriptorCount"/>
    /// are zero; a caller loops until it sees that message.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="socket"/> is <see langword="null"/>.</exception>
    public static unsafe bool TryReceive(
        Socket socket,
        Span<byte> payload,
        Span<int> descriptors,
        out UnixMessage message,
        out UnixPeerCredentials? credentials,
        out LinuxErrno error)
    {
        ArgumentNullException.ThrowIfNull(socket);

        message = default;
        credentials = null;
        error = LinuxErrno.None;

        if (!OperatingSystem.IsLinux())
        {
            error = LinuxErrno.FunctionNotImplemented;
            return false;
        }

        if (!socket.Connected)
        {
            error = LinuxErrno.NotConnected;
            return false;
        }

        if (descriptors.Length > MaximumDescriptors)
        {
            error = LinuxErrno.InvalidArgument;
            return false;
        }

        var rightsSize = descriptors.IsEmpty
            ? 0
            : ControlHeaderSize + (((descriptors.Length * 4) + 7) / 8 * 8);
        var controlSize = rightsSize + CredentialsControlSpace;
        ulong* controlWords = stackalloc ulong[(controlSize + 7) / 8];
        byte* control = (byte*)controlWords;
        new Span<byte>(control, controlSize).Clear();

        ulong* headerWords = stackalloc ulong[MessageHeaderSize / 8];
        byte* header = (byte*)headerWords;
        new Span<byte>(header, MessageHeaderSize).Clear();

        ulong* vectorWords = stackalloc ulong[IoVectorSize / 8];
        byte* vector = (byte*)vectorWords;

        var receivedFds = 0;

        fixed (byte* payloadPointer = payload)
        {
            *(void**)vector = payloadPointer;
            *(nuint*)(vector + 8) = (nuint)payload.Length;
            *(void**)(header + 16) = vector;
            *(nuint*)(header + 24) = 1;
            *(void**)(header + 32) = control;
            *(nuint*)(header + 40) = (nuint)controlSize;

            var result = Interop.RecvMsg((int)socket.Handle, header, MessageControlCloseOnExec);

            if (result < 0)
            {
                error = (LinuxErrno)Marshal.GetLastPInvokeError();
                return false;
            }

            var flags = *(int*)(header + 48);
            var controlLength = (int)*(nuint*)(header + 40);
            var truncated = (flags & MessageControlTruncated) != 0;
            var overflow = false;
            var parseLength = truncated ? controlSize : controlLength;
            var offset = 0;

            // WHY: a truncated control buffer drops installed fds unless the wrapper closes them.
            while (offset + ControlHeaderSize <= parseLength)
            {
                var rawLength = (int)*(nuint*)(control + offset);

                if (rawLength < ControlHeaderSize)
                {
                    break;
                }

                var remaining = parseLength - offset;
                var usableLength = rawLength < remaining ? rawLength : remaining;
                var level = *(int*)(control + offset + 8);
                var type = *(int*)(control + offset + 12);

                if (level == SocketLevel && type == RightsControl)
                {
                    var count = (usableLength - ControlHeaderSize) / 4;

                    if (truncated || count > descriptors.Length - receivedFds)
                    {
                        for (var index = 0; index < count; index++)
                        {
                            _ = Interop.Close(*(int*)(control + offset + ControlHeaderSize + (index * 4)));
                        }

                        overflow = !truncated;
                    }
                    else
                    {
                        for (var index = 0; index < count; index++)
                        {
                            descriptors[receivedFds] = *(int*)(control + offset + ControlHeaderSize + (index * 4));
                            receivedFds++;
                        }
                    }
                }
                else if (!truncated && level == SocketLevel && type == CredentialsControl &&
                    usableLength >= ControlHeaderSize + CredentialsSize)
                {
                    credentials = new UnixPeerCredentials(
                        *(int*)(control + offset + ControlHeaderSize),
                        *(uint*)(control + offset + ControlHeaderSize + 4),
                        *(uint*)(control + offset + ControlHeaderSize + 8));
                }

                if (rawLength > remaining)
                {
                    break;
                }

                offset += (rawLength + 7) & ~7;
            }

            if (truncated || overflow)
            {
                for (var index = 0; index < receivedFds; index++)
                {
                    _ = Interop.Close(descriptors[index]);
                    descriptors[index] = 0;
                }

                error = LinuxErrno.MessageTooLong;
                return false;
            }

            message = new UnixMessage((int)result, receivedFds, (flags & MessageTruncated) != 0);
            return true;
        }
    }

    internal static bool TryReadPeerIdentity(ReadOnlySpan<byte> buffer, out UnixPeerCredentials credentials)
    {
        credentials = new UnixPeerCredentials(
            MemoryMarshal.Read<int>(buffer),
            MemoryMarshal.Read<uint>(buffer[4..]),
            MemoryMarshal.Read<uint>(buffer[8..]));

        if (credentials.UserId == uint.MaxValue)
        {
            credentials = default;
            return false;
        }

        return true;
    }
}
