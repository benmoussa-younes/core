// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.IO.Mapped;
using Core.IO.Pipeline;

namespace Core.IO.Records.Scan;

/// <summary>
/// Scans a <see cref="MappedFile"/> in fixed windows, sequentially or with
/// <see cref="Parallel.For(int, int, ParallelOptions, Action{int})"/>, so a file
/// too large to hold is searched with bounded memory.
/// </summary>
/// <remarks>
/// Windows partition the file exactly, so a single-byte search never double
/// counts or misses a boundary: each window owns the half-open byte range
/// <c>[offset, offset + length)</c>. The parallel arm uses the shared .NET thread
/// pool, not a per-call task/channel pipeline: the measured scan is CPU/bandwidth
/// bound, so the pipeline's task, channel and semaphore overhead was the cost.
/// The async entry points run the CPU-bound scan on the calling thread; awaiting
/// the returned value completes after the scan. The caller must keep the
/// <see cref="MappedFile"/> alive until the scan returns; disposal is not
/// synchronized with a parallel scan.
/// </remarks>
public static class MappedFileScan
{
    /// <summary>The default window size in bytes (1 MiB).</summary>
    public const int DefaultWindowSize = 1 << 20;

    /// <summary>
    /// Counts the occurrences of a byte, one window at a time on the calling
    /// thread.
    /// </summary>
    /// <param name="file">The mapping to scan.</param>
    /// <param name="value">The byte to count.</param>
    /// <param name="windowSize">The window size in bytes.</param>
    /// <returns>The number of occurrences.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="windowSize"/> is not positive.</exception>
    public static long CountSequential(MappedFile file, byte value, int windowSize = DefaultWindowSize)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowSize);
        using var lease = file.Lease();

        var count = 0L;

        foreach (var window in Windows(file.Length, windowSize))
        {
            count += CountInWindow(file, value, window);
        }

        return count;
    }

    /// <summary>
    /// Counts the occurrences of a byte, one window per thread-pool work item.
    /// </summary>
    /// <param name="file">The mapping to scan.</param>
    /// <param name="value">The byte to count.</param>
    /// <param name="threads">The number of worker threads.</param>
    /// <param name="windowSize">The window size in bytes.</param>
    /// <param name="cancellationToken">The token used to cancel the scan.</param>
    /// <returns>The number of occurrences.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="windowSize"/> is not positive, or <paramref name="threads"/> is out of range.</exception>
    /// <exception cref="OperationCanceledException">The scan was cancelled.</exception>
    public static async ValueTask<long> CountAsync(
        MappedFile file,
        byte value,
        int threads,
        int windowSize = DefaultWindowSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowSize);
        ValidateThreads(threads);

        // CPU-bound: Parallel.For already uses the shared thread pool, so a
        // Task.Run here only adds a pool hop (measured ~0.45 ms per 16 MiB).
        // The await keeps the async exception contract; it completes at once.
        await Task.CompletedTask.ConfigureAwait(false);

        // ReaderWriterLockSlim is thread-affine; take the lease only after the
        // await so it is acquired and released on the same continuation.
        using var lease = file.Lease();
        return CountParallel(file, value, threads, windowSize, cancellationToken);
    }

    private static long CountParallel(
        MappedFile file,
        byte value,
        int threads,
        int windowSize,
        CancellationToken cancellationToken)
    {
        var windows = CountWindows(file.Length, windowSize);

        if (windows == 0)
        {
            return 0;
        }

        var counts = new long[windows];

        Parallel.For(
            0,
            windows,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = threads,
                CancellationToken = cancellationToken,
            },
            index =>
            {
                var offset = (long)index * windowSize;
                var length = (int)Math.Min(windowSize, file.Length - offset);
                counts[index] = file.Slice(offset, length).Count(value);
            });

        var total = 0L;

        for (var index = 0; index < counts.Length; index++)
        {
            total += counts[index];
        }

        return total;
    }

    private static long CountInWindow(MappedFile file, byte value, Window window)
    {
        return file.Slice(window.Offset, window.Length).Count(value);
    }

    private static int CountWindows(long length, int windowSize) =>
        length == 0 ? 0 : (int)(((length - 1) / windowSize) + 1);

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

    private static IEnumerable<Window> Windows(long length, int windowSize)
    {
        for (long offset = 0; offset < length; offset += windowSize)
        {
            yield return new Window(offset, (int)Math.Min(windowSize, length - offset));
        }
    }

    private readonly record struct Window(long Offset, int Length);
}
