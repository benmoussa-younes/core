// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform;

/// <summary>
/// A read-only memory mapping of a file: a zero-copy slice over pages the kernel
/// faults in on demand.
/// </summary>
/// <remarks>
/// The portable fallback is the BCL <c>MemoryMappedFile</c>; the Linux adapter is
/// the raw <c>mmap</c> implementation, measured at 13,3 µs versus 23,1 µs for the
/// BCL open and 40 B versus 680 B allocated. The mapping is read-only and shared;
/// a writer replacing the file does not change the pages already mapped.
/// </remarks>
public interface IMemoryMapping : IDisposable
{
    /// <summary>Gets the length of the mapping in bytes.</summary>
    long Length { get; }

    /// <summary>
    /// Gets a zero-copy view of <paramref name="length"/> bytes at
    /// <paramref name="offset"/>.
    /// </summary>
    /// <param name="offset">The byte offset into the mapping.</param>
    /// <param name="length">The number of bytes.</param>
    /// <returns>The bytes, valid while the mapping is alive.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The slice is not fully inside the mapping.</exception>
    /// <exception cref="ObjectDisposedException">The mapping has been disposed.</exception>
    ReadOnlySpan<byte> Slice(long offset, int length);
}
