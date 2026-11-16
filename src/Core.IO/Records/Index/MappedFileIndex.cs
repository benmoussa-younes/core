// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.IO.Mapped;
using Core.IO.Pipeline;
using Core.IO.Records.Scan;

namespace Core.IO.Records.Index;

/// <summary>
/// Builds an ordered offset index of a delimiter byte across a
/// <see cref="MappedFile"/>, in parallel and with bounded <em>working</em> memory,
/// so a record can be reached by index instead of rescanning the file.
/// </summary>
/// <remarks>
/// The build makes two passes over the mapped pages. The first counts the
/// delimiters per window, which gives each window a base offset by prefix sum;
/// the second writes each window's absolute offsets into its own disjoint slice
/// of the result array, so the workers never contend. Windows partition the
/// file, so an offset is produced exactly once, in order. Both passes use the
/// shared .NET thread pool: the measured index is CPU/bandwidth bound, and the
/// per-call task/channel pipeline was the overhead. The async entry point runs
/// the CPU-bound build on the calling thread; awaiting completes after the build.
/// <para>
/// The result is one <c>long</c> per delimiter: the index is O(records), up to
/// <see cref="Array.MaxLength"/> offsets, not constant memory. Only the per-window
/// working set is bounded.
/// </para>
/// </remarks>
public static class MappedFileIndex
{
    /// <summary>
    /// The default maximum number of offsets an index may hold (512 MiB of
    /// <c>long</c>s). The index is O(records), so a larger file must be indexed in
    /// bounded parts instead of one result array.
    /// </summary>
    public const long MaxOffsets = 1L << 26;

    /// <summary>
    /// Builds the ordered offsets of <paramref name="delimiter"/> in
    /// <paramref name="file"/>.
    /// </summary>
    /// <param name="file">The mapping to index.</param>
    /// <param name="delimiter">The delimiter byte, for example a newline.</param>
    /// <param name="threads">The number of worker threads.</param>
    /// <param name="windowSize">The window size in bytes.</param>
    /// <param name="cancellationToken">The token used to cancel the build.</param>
    /// <returns>The delimiter offsets in ascending order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="windowSize"/> is not positive, or <paramref name="threads"/> is out of range.</exception>
    /// <exception cref="InvalidOperationException">The index would exceed <see cref="MaxOffsets"/>.</exception>
    /// <exception cref="OperationCanceledException">The build was cancelled.</exception>
    public static ValueTask<long[]> BuildAsync(
        MappedFile file,
        byte delimiter,
        int threads,
        int windowSize = MappedFileScan.DefaultWindowSize,
        CancellationToken cancellationToken = default) =>
        BuildAsync(file, delimiter, threads, MaxOffsets, windowSize, cancellationToken);

    internal static async ValueTask<long[]> BuildAsync(
        MappedFile file,
        byte delimiter,
        int threads,
        long maxOffsets,
        int windowSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowSize);
        ValidateThreads(threads);

        // CPU-bound: the two Parallel.For passes already use the shared thread
        // pool, so a Task.Run here only adds a pool hop.
        await Task.CompletedTask.ConfigureAwait(false);

        return Build(file, delimiter, threads, maxOffsets, windowSize, cancellationToken);
    }

    private static long[] Build(
        MappedFile file,
        byte delimiter,
        int threads,
        long maxOffsets,
        int windowSize,
        CancellationToken cancellationToken)
    {
        var windows = CountWindows(file.Length, windowSize);
        var counts = new long[windows];

        if (windows > 0)
        {
            Parallel.For(
                0,
                windows,
                CreateOptions(threads, cancellationToken),
                index =>
                {
                    var window = WindowAt(file.Length, windowSize, index);
                    counts[index] = CountInWindow(file, delimiter, window);
                });
        }

        var bases = new long[windows];
        var total = 0L;

        for (var index = 0; index < windows; index++)
        {
            bases[index] = total;
            total += counts[index];
        }

        var offsets = total <= maxOffsets
            ? new long[total]
            : throw new InvalidOperationException($"An index of {total} offsets exceeds the maximum of {maxOffsets}.");

        if (windows > 0)
        {
            Parallel.For(
                0,
                windows,
                CreateOptions(threads, cancellationToken),
                index =>
                {
                    var window = WindowAt(file.Length, windowSize, index);
                    FillWindow(file, delimiter, window, bases[index], offsets);
                });
        }

        return offsets;
    }

    private static ParallelOptions CreateOptions(int threads, CancellationToken cancellationToken) =>
        new()
        {
            MaxDegreeOfParallelism = threads,
            CancellationToken = cancellationToken,
        };

    private static int CountWindows(long length, int windowSize)
    {
        return length == 0 ? 0 : (int)(((length - 1) / windowSize) + 1);
    }

    private static Window WindowAt(long length, int windowSize, int index)
    {
        var offset = (long)index * windowSize;
        return new Window(index, offset, (int)Math.Min(windowSize, length - offset));
    }

    private static long CountInWindow(MappedFile file, byte delimiter, Window window)
    {
        return file.Slice(window.Offset, window.Length).Count(delimiter);
    }

    private static int FillWindow(
        MappedFile file,
        byte delimiter,
        Window window,
        long baseOffset,
        long[] offsets)
    {
        var span = file.Slice(window.Offset, window.Length);
        var index = (int)baseOffset;
        var filled = 0;
        var position = 0;

        while (position < span.Length)
        {
            var found = span[position..].IndexOf(delimiter);

            if (found < 0)
            {
                break;
            }

            position += found;
            offsets[index++] = window.Offset + position;
            filled++;
            position++;
        }

        return filled;
    }

    private static void ValidateThreads(int threads)
    {
        if (threads is < 1 or > BlockPipelineOptions.MaximumThreads)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threads),
                threads,
                $"The thread count must be between 1 and {BlockPipelineOptions.MaximumThreads}.");
        }
    }

    private readonly record struct Window(int Index, long Offset, int Length);
}
