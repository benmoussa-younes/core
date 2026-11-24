using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Core.Results;

namespace Core.Platform.Linux;

/// <summary>
/// Owns the two ends of a Linux pipe created with <c>pipe2(O_CLOEXEC)</c>.
/// </summary>
/// <remarks>
/// The write end carries a secret to a child on an inherited descriptor: the
/// caller writes the bytes with <see cref="TryWriteAll"/>, closes the write end
/// with <see cref="CloseWrite"/>, and the child reads the descriptor to
/// end-of-file. Both ends are close-on-exec, so a child that execs without being
/// given the read end cannot inherit it; a child that is only forked still does.
/// One writer at a time: concurrent writes are not serialised, so their bytes
/// interleave. <see cref="TryWriteAll"/> blocks while the pipe buffer is full;
/// the caller keeps the write below the pipe capacity or provides a reader that
/// drains it. A write to a closed reader returns
/// <see cref="LinuxErrno.BrokenPipe"/> when <c>SIGPIPE</c> is ignored or blocked;
/// the .NET runtime ignores it, and a blocked <c>SIGPIPE</c> stays pending and can
/// terminate the process when it is unblocked. The pipe does not change the
/// process's signal disposition.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class UnixPipe : IDisposable
{
    private LinuxFileDescriptor? _read;
    private LinuxFileDescriptor? _write;

    private UnixPipe(int read, int write)
    {
        _read = new LinuxFileDescriptor(read);

        try
        {
            _write = new LinuxFileDescriptor(write);
        }
        catch
        {
            _read.Dispose();
            _read = null;
            _ = Interop.Close(write);
            throw;
        }
    }

    /// <summary>Gets the descriptor of the read end.</summary>
    /// <remarks>
    /// Valid until the read end is closed; the pipe owns it, so the caller must
    /// not close it or use it after <see cref="CloseRead"/> or <see cref="Dispose"/>.
    /// Closing the number lets the next <c>open</c> reuse it, and a later
    /// <see cref="Dispose"/> would close that descriptor.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The read end is already closed.</exception>
    public int ReadDescriptor => Descriptor(_read);

    /// <summary>Gets the descriptor of the write end.</summary>
    /// <remarks>
    /// Valid until the write end is closed; the pipe owns it, so the caller must
    /// not close it or use it after <see cref="CloseWrite"/> or <see cref="Dispose"/>.
    /// Closing the number lets the next <c>open</c> reuse it, and a later
    /// <see cref="Dispose"/> would close that descriptor.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The write end is already closed.</exception>
    public int WriteDescriptor => Descriptor(_write);

    /// <summary>Creates a pipe whose two ends are close-on-exec.</summary>
    /// <returns>The pipe, or a failure with code <see cref="CoreErrorCodes.UnixPipeFailed"/>.</returns>
    public static unsafe Result<UnixPipe> Create()
    {
        if (!OperatingSystem.IsLinux())
        {
            return Result<UnixPipe>.Failure(
                new CoreError(CoreErrorCodes.UnixPipeFailed, "pipe2 is only available on Linux."));
        }

        Span<int> descriptors = stackalloc int[2];

        fixed (int* pointer = descriptors)
        {
            if (Interop.Pipe2(pointer, Interop.OpenCloseOnExec) != 0)
            {
                var error = (LinuxErrno)Marshal.GetLastPInvokeError();
                return Result<UnixPipe>.Failure(
                    new CoreError(CoreErrorCodes.UnixPipeFailed, $"pipe2 failed: {error}."));
            }
        }

        return Result<UnixPipe>.Success(new UnixPipe(descriptors[0], descriptors[1]));
    }

    /// <summary>Writes every byte to the write end.</summary>
    /// <param name="data">The bytes to write; an empty span writes nothing while the write end is open.</param>
    /// <param name="written">The bytes written: every byte on success, the prefix on failure, zero when nothing was written.</param>
    /// <param name="error">
    /// The <c>errno</c> when the call fails; <see cref="LinuxErrno.None"/> otherwise.
    /// A zero-byte write for a non-empty buffer is reported as
    /// <see cref="LinuxErrno.InvalidArgument"/> although the kernel sets no
    /// <c>errno</c>; a blocking pipe does not produce it.
    /// </param>
    /// <returns><see langword="true"/> when every byte was written.</returns>
    /// <remarks>
    /// The call blocks while the pipe buffer is full; the caller provides a
    /// reader that drains it or keeps the write below the pipe capacity. A
    /// failure after a partial write reports the prefix in
    /// <paramref name="written"/> and the pipe must be treated as failed. A
    /// concurrent <see cref="CloseWrite"/> or <see cref="Dispose"/> does not
    /// close the descriptor under the write: the handle stays referenced for the
    /// duration of the call.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The write end is already closed.</exception>
    public unsafe bool TryWriteAll(ReadOnlySpan<byte> data, out int written, out LinuxErrno error)
    {
        written = 0;
        error = LinuxErrno.None;
        var write = _write ?? throw new ObjectDisposedException(nameof(UnixPipe));
        var added = false;
        write.DangerousAddRef(ref added);

        try
        {
            var descriptor = (int)write.DangerousGetHandle();
            var offset = 0;

            fixed (byte* pointer = data)
            {
                while (offset < data.Length)
                {
                    var result = Interop.Write(descriptor, pointer + offset, (nuint)(data.Length - offset));

                    if (result < 0)
                    {
                        var errno = (LinuxErrno)Marshal.GetLastPInvokeError();

                        if (errno == LinuxErrno.Interrupted)
                        {
                            continue;
                        }

                        error = errno;
                        written = offset;
                        return false;
                    }

                    if (result == 0)
                    {
                        // WHY: zero bytes for a non-empty buffer is not progress; looping would spin.
                        error = LinuxErrno.InvalidArgument;
                        written = offset;
                        return false;
                    }

                    offset += (int)result;
                }
            }

            written = offset;
            return true;
        }
        finally
        {
            write.DangerousRelease();
        }
    }

    /// <summary>Closes the read end, so the next write fails with <see cref="LinuxErrno.BrokenPipe"/>.</summary>
    public void CloseRead()
    {
        var read = _read;
        _read = null;
        read?.Dispose();
    }

    /// <summary>Closes the write end, so a reader reaches end-of-file after draining the buffer.</summary>
    public void CloseWrite()
    {
        var write = _write;
        _write = null;
        write?.Dispose();
    }

    /// <summary>Closes both ends.</summary>
    public void Dispose()
    {
        CloseRead();
        CloseWrite();
    }

    private static int Descriptor(LinuxFileDescriptor? descriptor)
    {
        return descriptor is null
            ? throw new ObjectDisposedException(nameof(UnixPipe))
            : (int)descriptor.DangerousGetHandle();
    }
}
