// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers.Binary;
using System.Text;
using System.Text.Unicode;


namespace Core.Platform.Linux;

/// <summary>
/// Parses the records of a raw <c>getdents64</c> buffer.
/// </summary>
/// <remarks>
/// A <c>linux_dirent64</c> record is <c>d_ino</c> (8), <c>d_off</c> (8),
/// <c>d_reclen</c> (2) and <c>d_type</c> (1), then the NUL-terminated name, all
/// padded to <c>d_reclen</c>. The length is read as unsigned so a corrupt value
/// above 32767 cannot sign-extend into a negative name length. A malformed
/// record is refused rather than trusted, because reading past it would hash
/// the wrong path.
/// </remarks>
internal static class DirectoryRecordParser
{
    /// <summary>The size of a <c>linux_dirent64</c> header: <c>d_ino</c>, <c>d_off</c>, <c>d_reclen</c> and <c>d_type</c>.</summary>
    internal const int HeaderSize = 19;

    private const int LengthOffset = 16;
    private const int TypeOffset = 18;
    private const int NameOffset = 19;

    /// <summary>
    /// Reads the next non-dot entry from a <c>getdents64</c> buffer.
    /// </summary>
    /// <param name="block">The buffer returned by <c>getdents64</c>.</param>
    /// <param name="offset">The current offset; advanced past the record read.</param>
    /// <param name="entry">The entry when one was read.</param>
    /// <returns><see langword="false"/> when the buffer is exhausted.</returns>
    /// <exception cref="IOException">
    /// A record header is truncated, its length is below the header size, or it
    /// claims more bytes than the block holds.
    /// </exception>
    internal static bool TryReadRecord(ReadOnlySpan<byte> block, ref int offset, out LinuxDirectoryEntry entry)
    {
        while (offset < block.Length)
        {
            var remaining = block.Length - offset;

            if (remaining < HeaderSize)
            {
                throw new IOException("Malformed directory record: the header is truncated.");
            }

            var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(block[(offset + LengthOffset)..]);

            if (recordLength < HeaderSize)
            {
                throw new IOException("Malformed directory record: the length is below the header size.");
            }

            if (recordLength > remaining)
            {
                throw new IOException("Malformed directory record: the length exceeds the block.");
            }

            var type = (LinuxFileType)block[offset + TypeOffset];
            var inode = BinaryPrimitives.ReadUInt64LittleEndian(block[offset..]);
            var name = block.Slice(offset + NameOffset, recordLength - HeaderSize);
            offset += recordLength;

            var terminator = name.IndexOf((byte)0);

            if (terminator >= 0)
            {
                name = name[..terminator];
            }

            if (name.Length == 0 || name.SequenceEqual("."u8) || name.SequenceEqual(".."u8))
            {
                continue;
            }

            entry = new LinuxDirectoryEntry(Encoding.UTF8.GetString(name), type, Utf8.IsValid(name), inode);
            return true;
        }

        entry = default;
        return false;
    }
}
