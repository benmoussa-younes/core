// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers;
using System.Runtime.InteropServices;


namespace Core.Platform.Linux;

/// <summary>
/// Reads the entries of an open directory with <c>getdents64</c>.
/// </summary>
/// <remarks>
/// The entry type comes from the directory stream's <c>d_type</c>, so listing a
/// regular file costs no metadata syscall. The reader owns one pooled buffer and
/// decodes each name as UTF-8, flagging the ones whose bytes are not valid
/// UTF-8. It does not own the directory handle: the caller keeps that lifetime.
/// </remarks>
public sealed class LinuxDirectoryReader : IDirectoryReader
{
    private const int BufferSize = 1 << 15;

    private readonly LinuxDirectoryHandle _handle;
    private byte[]? _buffer;
    private int _length;
    private int _offset;
    private bool _finished;
    private bool _disposed;

    internal LinuxDirectoryReader(LinuxDirectoryHandle handle)
    {
        _handle = handle;
        _buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
    }

    /// <summary>
    /// Reads the next entry.
    /// </summary>
    /// <param name="entry">The entry when one was read.</param>
    /// <returns><see langword="false"/> at the end of the directory.</returns>
    /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
    /// <exception cref="IOException">The directory stream could not be read or a record is malformed.</exception>
    public unsafe bool TryRead(out LinuxDirectoryEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (true)
        {
            if (_offset < _length)
            {
                fixed (byte* buffer = _buffer)
                {
                    if (DirectoryRecordParser.TryReadRecord(new ReadOnlySpan<byte>(buffer, _length), ref _offset, out entry))
                    {
                        return true;
                    }
                }
            }

            if (_finished)
            {
                entry = default;
                return false;
            }

            if (_handle.IsDisposed)
            {
                throw new IOException("Unable to read the directory stream: the directory handle is disposed.");
            }

            int read;

            fixed (byte* buffer = _buffer)
            {
                while (true)
                {
                    read = Interop.GetDents64(_handle.FileDescriptor, buffer, BufferSize);

                    // Retry a signal-interrupted listing, exactly as the read
                    // and write wrappers do; a real error is reported below.
                    if (read >= 0 || Marshal.GetLastPInvokeError() != (int)LinuxErrno.Interrupted)
                    {
                        break;
                    }
                }
            }

            if (read < 0)
            {
                throw new IOException($"Unable to read the directory stream (errno {(int)LinuxFileSystem.LastError}).");
            }

            if (read == 0)
            {
                _finished = true;
                entry = default;
                return false;
            }

            _length = read;
            _offset = 0;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Reset the cursor before releasing the buffer: a non-zero length over a
        // null buffer would build a non-empty span over address 0.
        _length = 0;
        _offset = 0;

        var buffer = _buffer;

        if (buffer is not null)
        {
            _buffer = null;
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    bool IDirectoryReader.TryRead(out DirectoryEntry entry)
    {
        var read = TryRead(out LinuxDirectoryEntry linux);
        entry = read
            ? new DirectoryEntry(linux.Name, (FileType)(byte)linux.Type, linux.IsNameValidUtf8, linux.Inode)
            : default;
        return read;
    }
}
