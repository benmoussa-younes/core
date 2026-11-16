// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.IO.Mapped;
using Core.IO.Records.Scan;

namespace Core.IO.Records.Index;

/// <summary>
/// A delimiter-delimited view of a <see cref="MappedFile"/>: the record at an
/// index is reached in constant time from the built index, and its bytes are a
/// zero-copy span.
/// </summary>
/// <remarks>
/// A record is the bytes between two delimiters. The trailing bytes after the
/// last delimiter form a final record unless the file ends with the delimiter,
/// so a file that does not end with one has one more record than the index has
/// offsets. A record may be empty (two consecutive delimiters). The set does
/// not own the mapping: the <see cref="MappedFile"/> must outlive it.
/// </remarks>
public sealed class RecordSet
{
    private readonly MappedFile _file;
    private readonly long[] _offsets;

    private RecordSet(MappedFile file, byte delimiter, long[] offsets, long count)
    {
        _file = file;
        _offsets = offsets;
        Delimiter = delimiter;
        Count = count;
    }

    /// <summary>
    /// Gets the delimiter byte.
    /// </summary>
    public byte Delimiter { get; }

    /// <summary>
    /// Gets the number of records, as a <see cref="long"/>: a file ending
    /// without a delimiter has one more record than the index has offsets.
    /// </summary>
    public long Count { get; }

    /// <summary>
    /// Builds the index and returns the record set.
    /// </summary>
    /// <param name="file">The mapping to read.</param>
    /// <param name="delimiter">The delimiter byte, for example a newline.</param>
    /// <param name="threads">The number of worker threads.</param>
    /// <param name="windowSize">The window size in bytes.</param>
    /// <param name="cancellationToken">The token used to cancel the build.</param>
    /// <returns>The record set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="windowSize"/> is not positive, or <paramref name="threads"/> is out of range.</exception>
    /// <exception cref="OperationCanceledException">The build was cancelled.</exception>
    public static async ValueTask<RecordSet> OpenAsync(
        MappedFile file,
        byte delimiter,
        int threads,
        int windowSize = MappedFileScan.DefaultWindowSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var offsets = await MappedFileIndex
            .BuildAsync(file, delimiter, threads, windowSize, cancellationToken)
            .ConfigureAwait(false);

        var count = offsets.Length == 0
            ? (file.Length == 0 ? 0L : 1L)
            : (offsets[^1] == file.Length - 1 ? offsets.Length : offsets.Length + 1L);

        return new RecordSet(file, delimiter, offsets, count);
    }

    /// <summary>
    /// Gets the absolute start offset of a record.
    /// </summary>
    /// <param name="index">The record index.</param>
    /// <returns>The byte offset of the record's first byte.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the records.</exception>
    public long Start(long index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

        return index == 0 ? 0 : _offsets[(int)(index - 1)] + 1;
    }

    /// <summary>
    /// Gets the absolute end offset of a record, exclusive: the delimiter's
    /// offset, or the end of the file for the trailing record.
    /// </summary>
    /// <param name="index">The record index.</param>
    /// <returns>The byte offset just past the record's last byte.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the records.</exception>
    public long End(long index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

        return index < _offsets.Length ? _offsets[(int)index] : _file.Length;
    }

    /// <summary>
    /// Gets the bytes of a record, without its delimiter.
    /// </summary>
    /// <param name="index">The record index.</param>
    /// <returns>The record's bytes, valid while the mapping is alive.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the records.</exception>
    /// <exception cref="InvalidOperationException">The record is larger than <see cref="int.MaxValue"/> bytes.</exception>
    public ReadOnlySpan<byte> Record(long index)
    {
        var start = Start(index);
        var end = End(index);
        var length = end - start;

        return length <= int.MaxValue
            ? _file.Slice(start, (int)length)
            : throw new InvalidOperationException($"Record {index} is larger than {int.MaxValue} bytes.");
    }
}
