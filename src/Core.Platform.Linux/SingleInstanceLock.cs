// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using Core.Paths;
using Core.Primitives;

namespace Core.Platform.Linux;

/// <summary>
/// Keeps one instance of an application per user: the first instance binds a Unix
/// domain socket, and a later launch connects to it, asks it to surface, and exits
/// instead of starting a second window.
/// </summary>
/// <remarks>
/// The lock is an OS resource, so it is Linux-only and exercised by an integration
/// test over a real socket. The socket is owner-only (0600) and lives under the
/// user's runtime directory.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class SingleInstanceLock : ISingleInstance
{
    /// <summary>The first byte a client sends to request activation.</summary>
    private const byte ActivationByte = 1;

    /// <summary>
    /// The longest socket path the kernel accepts: <c>sun_path</c> is 108 bytes
    /// including its terminator, so 107 bytes of path fit.
    /// </summary>
    private const int MaxSocketPathBytes = 107;

    /// <summary>How long one activation connection may take to deliver its frame.</summary>
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(2);

    /// <summary>The maximum activation payload a later launch may hand over.</summary>
    public const int MaxPayloadBytes = 65_536;

    private readonly string _socketPath;
    private Socket? _listener;
    private FileStream? _claim;
    private CancellationTokenSource? _lifetime;
    private int _disposed;

    private string ClaimPath => string.Concat(_socketPath, ".lock");

    private enum ActivationResult
    {
        Delivered,
        Stale,
        Failed,
    }

    /// <summary>Initializes a new instance of the <see cref="SingleInstanceLock"/> class.</summary>
    /// <param name="socketPath">The Unix domain socket path the lock owns.</param>
    /// <exception cref="ArgumentException"><paramref name="socketPath"/> is empty or whitespace.</exception>
    public SingleInstanceLock(string socketPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);

        if (socketPath.Contains('\0') || Encoding.UTF8.GetByteCount(socketPath) > MaxSocketPathBytes)
        {
            throw new ArgumentException(
                $"A Unix domain socket path must be at most {MaxSocketPathBytes} UTF-8 bytes and contain no NUL.",
                nameof(socketPath));
        }

        _socketPath = socketPath;
    }

    /// <summary>
    /// Raised when another launch asked this instance to surface. It runs on a
    /// background thread; a UI host must marshal it to its own thread.
    /// </summary>
    /// <remarks>
    /// A payload launch raises both this event and <see cref="Activated"/>, this
    /// one last. Subscribe to one of the two, not both, or the host handles the
    /// same launch twice.
    /// </remarks>
    public event Action? ActivationRequested;

    /// <summary>
    /// Raised with the payload the later launch handed over — typically the
    /// document path to open. It runs on a background thread; a UI host must
    /// marshal it to its own thread. The memory is valid for the duration of the
    /// callback; a handler that retains it must copy it.
    /// </summary>
    /// <remarks>
    /// A payload launch raises this event and then <see cref="ActivationRequested"/>.
    /// Subscribe to one of the two, not both, or the host handles the same launch
    /// twice.
    /// </remarks>
    public event Action<ReadOnlyMemory<byte>>? Activated;

    /// <summary>Gets the socket path the lock owns.</summary>
    public string SocketPath => _socketPath;

    /// <summary>Builds the default socket path for an application and the current user.</summary>
    /// <param name="applicationName">The application name, a path component.</param>
    /// <returns>An absolute path under <c>XDG_RUNTIME_DIR</c> (or the temp directory).</returns>
    /// <exception cref="ArgumentException"><paramref name="applicationName"/> is empty or whitespace.</exception>
    public static string DefaultSocketPath(string applicationName)
    {
        ArgumentNullException.ThrowIfNull(applicationName);

        var validated = PathComponent.Validate(applicationName);
        if (validated.TryGetError(out var error))
        {
            throw new ArgumentException(error.Message, nameof(applicationName));
        }

        // The user name is an environment-owned component too; fall back to a
        // fixed word rather than let a separator escape the runtime directory.
        var user = PathComponent.Validate(Environment.UserName).ValueOr("user");
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");

        if (!string.IsNullOrEmpty(runtimeDirectory) && !Path.IsPathRooted(runtimeDirectory))
        {
            throw new ArgumentException("XDG_RUNTIME_DIR must be an absolute path.", nameof(applicationName));
        }

        var directory = string.IsNullOrEmpty(runtimeDirectory)
            ? Path.Combine(Path.GetTempPath(), $"core-{user}")
            : runtimeDirectory;

        // "core" is the default vendor, not a prefix of its own: the host passes
        // its own vendor, and the two must agree on one naming rule.
        var path = Path.Combine(directory, SingleInstanceSocketName.For("core", validated.Value, user));

        if (Encoding.UTF8.GetByteCount(path) > MaxSocketPathBytes)
        {
            throw new ArgumentException(
                $"The socket path for '{validated.Value}' exceeds {MaxSocketPathBytes} UTF-8 bytes; use an explicit path.",
                nameof(applicationName));
        }

        return path;
    }

    /// <summary>
    /// Takes the single-instance claim. Returns <see langword="true"/> when this is
    /// the running instance; returns <see langword="false"/> after asking the running
    /// instance to surface, in which case the caller must exit.
    /// </summary>
    /// <returns>Whether this process became the running instance.</returns>
    /// <exception cref="ObjectDisposedException">The lock has been disposed.</exception>
    public bool TryAcquire() => TryAcquire(ReadOnlySpan<byte>.Empty);

    /// <summary>
    /// Takes the single-instance claim, handing a payload to the running instance
    /// when this process must exit.
    /// </summary>
    /// <param name="payload">The payload to hand over, at most <see cref="MaxPayloadBytes"/> bytes.</param>
    /// <returns>Whether this process became the running instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="payload"/> exceeds <see cref="MaxPayloadBytes"/>.</exception>
    /// <exception cref="ObjectDisposedException">The lock has been disposed.</exception>
    public bool TryAcquire(ReadOnlySpan<byte> payload)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (_listener is not null)
        {
            return true;
        }

        if (payload.Length > MaxPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                payload.Length,
                $"The activation payload must be at most {MaxPayloadBytes} bytes.");
        }

        var activation = TryActivateRunningInstance(payload);

        // Delivered means the owner surfaced; Failed is a transient socket error
        // that must not delete a live owner's socket or create a second instance.
        // Only a refused probe falls through to the stale-path cleanup.
        if (activation != ActivationResult.Stale)
        {
            return false;
        }

        CreateOwnerOnlyDirectory();

        // The lock file is the claim: it is atomic, it survives a crash without
        // being "stale", and the kernel releases it when the owner exits. This
        // closes the bind/listen and dispose/delete windows that could otherwise
        // produce two instances.
        FileStream claim;

        try
        {
            claim = new FileStream(
                ClaimPath,
                new FileStreamOptions
                {
                    Mode = FileMode.OpenOrCreate,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Another owner holds the lock; let it surface if it is listening.
            _ = TryActivateRunningInstance(payload);
            return false;
        }

        // We hold the claim, so any socket file at the path is a leftover from a
        // crashed owner. Remove it before binding.
        if (File.Exists(_socketPath))
        {
            TryDeleteStale();
        }

        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        }
        catch (SocketException)
        {
            listener.Dispose();
            claim.Dispose();
            _ = TryActivateRunningInstance(payload);
            return false;
        }

        StartListener(listener, _socketPath, claim);
        _listener = listener;
        _claim = claim;

        _lifetime = new CancellationTokenSource();
        _ = AcceptLoopAsync(listener, _lifetime.Token);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        // A lock that never acquired the claim owns nothing: disposing it must not
        // delete the socket the owning instance still serves.
        if (_listener is not null)
        {
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _listener.Dispose();

            try
            {
                File.Delete(_socketPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A directory or an entry we cannot remove must not leak the claim.
            }
        }

        // Release the claim only after unlinking our socket, so a challenger cannot
        // bind a new path and have it deleted by this dispose.
        _claim?.Dispose();
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "A failure between bind and listen is an OS fault that cannot be provoked deterministically; the successful listener path is covered.")]
    private static void StartListener(Socket listener, string socketPath, FileStream claim)
    {
        try
        {
            File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            listener.Listen(1);
        }
        catch
        {
            listener.Dispose();
            claim.Dispose();

            try
            {
                File.Delete(socketPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best effort; the original fault is rethrown.
            }

            throw;
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "The best-effort delete failure depends on the parent directory's permissions; the successful stale replacement is covered.")]
    private void TryDeleteStale()
    {
        try
        {
            File.Delete(_socketPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A directory or an entry we cannot remove; the bind below decides.
        }
    }

    private void CreateOwnerOnlyDirectory()
    {
        if (Path.GetDirectoryName(_socketPath) is not { Length: > 0 } directory)
        {
            return;
        }

        if (Directory.Exists(directory))
        {
            // XDG_RUNTIME_DIR is owner-only by contract. A pre-existing
            // group/world-writable directory would let another local user
            // replace the socket or the claim file.
            if (LinuxFileSystem.TryStat(directory, out var status) &&
                status.Type == LinuxFileType.Directory)
            {
                if (status.OwnerUserId != Interop.GetEuid())
                {
                    throw new IOException("The single-instance runtime directory is owned by another user.");
                }

                if ((status.Mode & 0x3F) != 0)
                {
                    throw new IOException("The single-instance runtime directory is not owner-only (0700).");
                }
            }

            return;
        }

        Directory.CreateDirectory(directory);

        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "The transient EMFILE/ENOBUFS arm cannot be provoked deterministically; the refused/stale and delivered paths are covered.")]
    private ActivationResult TryActivateRunningInstance(ReadOnlySpan<byte> payload)
    {
        if (!File.Exists(_socketPath))
        {
            return ActivationResult.Stale;
        }

        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(_socketPath));

            // Frame: the activation byte followed by the payload. An old instance
            // reads the first byte and ignores the rest; a new one reads the
            // payload until EOF. This keeps the one-byte protocol working.
            var frame = new byte[1 + payload.Length];
            frame[0] = ActivationByte;
            payload.CopyTo(frame.AsSpan(1));

            var sent = 0;
            while (sent < frame.Length)
            {
                sent += client.Send(frame.AsSpan(sent), SocketFlags.None);
            }

            return ActivationResult.Delivered;
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionRefused)
        {
            // The path exists but nobody answers: a leftover from a crash, so the
            // bind below may take the claim.
            return ActivationResult.Stale;
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            // A transient socket error (EMFILE, ENOBUFS, a reset) must not become a
            // second instance. The caller exits without deleting the live socket.
            return ActivationResult.Failed;
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "The listener-death versus per-connection-reset distinction depends on kernel timing; the integration tests cover activation and a direct listener close.")]
    private async Task AcceptLoopAsync(Socket listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var activated = false;
            ReadOnlyMemory<byte> payload = ReadOnlyMemory<byte>.Empty;

            try
            {
                using var connection = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);

                // A Unix socket in the 0700 runtime directory is already
                // user-private; SO_PEERCRED makes the same-user rule explicit
                // against a process that inherited the directory.
                if (!HasSameUser(connection))
                {
                    continue;
                }

                using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectionTimeout.CancelAfter(ActivationTimeout);

                var kind = new byte[1];
                var read = await connection
                    .ReceiveAsync(kind, SocketFlags.None, connectionTimeout.Token)
                    .ConfigureAwait(false);

                if (read == 1 && kind[0] == ActivationByte)
                {
                    activated = true;
                    payload = await ReadPayloadAsync(connection, connectionTimeout.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The lock is being disposed: stop serving.
                return;
            }
            catch (OperationCanceledException)
            {
                // A client that connects but never delivers a complete frame is
                // dropped, so one stalled launch cannot wedge the activation loop.
                continue;
            }
            catch (ObjectDisposedException)
            {
                // The listener itself is gone: stop serving.
                return;
            }
            catch (SocketException)
            {
                // A reset on one connection must not kill the only accept loop. Only
                // a dead listener ends it.
                if (cancellationToken.IsCancellationRequested || listener.SafeHandle.IsClosed)
                {
                    return;
                }

                continue;
            }

            if (!activated)
            {
                continue;
            }

            // A product handler must not be able to kill the only accept loop; the
            // event is best effort and the loop keeps serving.
            try
            {
                Activated?.Invoke(payload);
            }
            catch
            {
            }

            try
            {
                ActivationRequested?.Invoke();
            }
            catch
            {
            }
        }
    }

    private static bool HasSameUser(Socket connection)
    {
        return UnixSocket.TryGetPeerCredentials(connection, out var peer, out _) &&
            peer.UserId == Interop.GetEuid();
    }

    private static async Task<ReadOnlyMemory<byte>> ReadPayloadAsync(Socket connection, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var payload = new MemoryStream();

        while (payload.Length < MaxPayloadBytes)
        {
            var remaining = (int)Math.Min(buffer.Length, MaxPayloadBytes - payload.Length);
            var read = await connection
                .ReceiveAsync(buffer.AsMemory(0, remaining), SocketFlags.None, cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            payload.Write(buffer, 0, read);
        }

        return payload.ToArray();
    }
}
