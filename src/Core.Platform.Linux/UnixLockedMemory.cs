using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Core.Results;

namespace Core.Platform.Linux;

/// <summary>
/// A caller buffer kept out of swap and pageable kernel memory (<c>mlock</c>),
/// zeroed before it is unlocked and released.
/// </summary>
/// <remarks>
/// The buffer stays pinned for the lock's lifetime, so the garbage collector
/// never moves the locked pages. A copy of the bytes elsewhere is not locked:
/// the caller must not duplicate the buffer.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class UnixLockedMemory : IDisposable
{
    private readonly byte[] _buffer;
    private GCHandle _handle;
    private bool _disposed;

    private UnixLockedMemory(byte[] buffer, GCHandle handle)
    {
        _buffer = buffer;
        _handle = handle;
    }

    /// <summary>Gets the locked bytes.</summary>
    /// <exception cref="ObjectDisposedException">The lock has been disposed.</exception>
    public Span<byte> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _buffer;
        }
    }

    /// <summary>Gets the number of locked bytes.</summary>
    public int Length => _buffer.Length;

    /// <summary>Pins a buffer and locks its pages in memory.</summary>
    /// <param name="buffer">The bytes to lock.</param>
    /// <returns>The lock, or a failure naming the <c>errno</c> when the kernel refused it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="buffer"/> is empty.</exception>
    public static unsafe Result<UnixLockedMemory> Lock(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (buffer.Length == 0)
        {
            throw new ArgumentException("A locked buffer must not be empty.", nameof(buffer));
        }

        if (!OperatingSystem.IsLinux())
        {
            return Result<UnixLockedMemory>.Failure(
                new CoreError("unix.mlock", "mlock is only available on Linux."));
        }

        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        if (Interop.MLock(handle.AddrOfPinnedObject().ToPointer(), (nuint)buffer.Length) != 0)
        {
            var errno = (LinuxErrno)Marshal.GetLastPInvokeError();
            handle.Free();
            return Result<UnixLockedMemory>.Failure(
                new CoreError("unix.mlock", $"mlock failed: {errno}."));
        }

        return Result<UnixLockedMemory>.Success(new UnixLockedMemory(buffer, handle));
    }

    /// <summary>Zeroes the buffer, unlocks its pages and releases the pin.</summary>
    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CryptographicOperations.ZeroMemory(_buffer);
        _ = Interop.MUnlock(_handle.AddrOfPinnedObject().ToPointer(), (nuint)_buffer.Length);
        _handle.Free();
    }
}
