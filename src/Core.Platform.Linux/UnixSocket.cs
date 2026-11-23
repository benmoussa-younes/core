using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Core.Platform.Linux;

/// <summary>
/// The peer-identity surface of a Unix domain socket: the credentials the
/// kernel reports for the process at the other end (<c>SO_PEERCRED</c>).
/// </summary>
/// <remarks>
/// The call reads the option through the BCL and adds no direct libc import. The
/// socket must be a connected stream socket: a listening socket has no peer and
/// the kernel would report the listener's own credentials, so the call refuses
/// it with <see cref="LinuxErrno.NotConnected"/>. A connected datagram socket or
/// a connected socket of another family reports the invalid credentials
/// (<c>uid</c> = <see cref="uint.MaxValue"/>) and is refused with
/// <see cref="LinuxErrno.ProtocolNotAvailable"/>.
/// </remarks>
[SupportedOSPlatform("linux")]
public static class UnixSocket
{
    private const int SocketLevel = 1;
    private const int PeerCredentialsOption = 17;
    private const int CredentialsSize = 12;

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
