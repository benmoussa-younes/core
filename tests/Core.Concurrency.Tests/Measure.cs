// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers;
using System.Diagnostics;

namespace Core.Concurrency.Tests;

/// <summary>
/// The handful of measurement helpers the timing-sensitive tests use. These are
/// <em>qualitative</em> guards: the spike and the AOT probe own the exact numbers,
/// and these assert only that a regression of the expected sign has not appeared.
/// Each runs in <see cref="TimingSensitiveCollection"/> so it is not measured under
/// the rest of the suite's contention.
/// </summary>
internal static class Measure
{
    /// <summary>
    /// Runs <paramref name="body"/> <paramref name="warmup"/> times, then
    /// <paramref name="iterations"/> times, and returns the best transformed sample
    /// (the least-perturbed run, the least noisy statistic on a shared host).
    /// </summary>
    /// <param name="body">The action to time.</param>
    /// <param name="iterations">The number of measured runs.</param>
    /// <param name="warmup">The number of untimed warmup runs.</param>
    /// <param name="action">Maps a run's elapsed milliseconds to the reported value.</param>
    /// <returns>The best transformed sample.</returns>
    internal static double Best(Action body, int iterations, int warmup, Func<double, double> action)
    {
        for (var index = 0; index < warmup; index++)
        {
            body();
        }

        var best = double.MaxValue;

        for (var index = 0; index < iterations; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            body();
            stopwatch.Stop();
            best = Math.Min(best, action(stopwatch.Elapsed.TotalMilliseconds));
        }

        return best;
    }

    /// <summary>
    /// Measures a scratch-buffer body three ways — the per-worker buffer pool, an
    /// <see cref="ArrayPool{T}.Shared"/> rent/return, and a fresh allocation — over
    /// the same items, on one context.
    /// </summary>
    /// <param name="workers">The worker count.</param>
    /// <param name="items">The item count.</param>
    /// <param name="payload">The scratch region's element count.</param>
    /// <returns>The three ns/item figures and the pooled arm's caller allocation.</returns>
    internal static BufferMeasurement BufferedBody(int workers, int items, int payload)
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = workers });
        using var pool = context.RegisterBufferPool<byte>(payload);

        long sink = 0;

        double NsPerItem(Action body) => Best(
            body,
            iterations: 21,
            warmup: 10,
            action: elapsedMs => elapsedMs * 1e6 / items);

        void PooledBody() => context.For(
            0,
            items,
            1,
            index => sink ^= Sum(index, context.WorkerBuffer(pool).Ensure(payload)[..payload]),
            new ConcurrencyOptions { Partition = WorkPartition.ClaimChunk });

        void ArrayPoolBody() => context.For(
            0,
            items,
            1,
            index =>
            {
                var rented = ArrayPool<byte>.Shared.Rent(payload);
                try
                {
                    sink ^= Sum(index, rented.AsSpan(0, payload));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            },
            new ConcurrencyOptions { Partition = WorkPartition.ClaimChunk });

        // Warm both shapes, then measure the array-pool arm first so the pooled arm
        // is measured on the warmer JIT: the measurement must not decide the result.
        for (var warmup = 0; warmup < 10; warmup++)
        {
            PooledBody();
            ArrayPoolBody();
        }

        var arrayPool = NsPerItem(ArrayPoolBody);
        var pooled = NsPerItem(PooledBody);

        // The caller's own allocation for the pooled shape, over 2 000 items. The
        // bodies run on workers, so this is the dispatch's own garbage, not the
        // buffer's: the pool must not rent or allocate per item.
        var before = GC.GetAllocatedBytesForCurrentThread();

        context.For(
            0,
            2_000,
            1,
            index => sink ^= Sum(index, context.WorkerBuffer(pool).Ensure(payload)[..payload]),
            new ConcurrencyOptions { Partition = WorkPartition.ClaimChunk });

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        return new BufferMeasurement(pooled, arrayPool, allocated);
    }

    private static long Sum(long seed, Span<byte> span)
    {
        for (var index = 0; index < span.Length; index++)
        {
            span[index] = (byte)(seed + index);
        }

        long sum = 0;
        foreach (var value in span)
        {
            sum += value;
        }

        return sum;
    }

    /// <summary>The three scratch-buffer figures, in nanoseconds per item.</summary>
    /// <param name="PooledNsPerItem">The per-worker buffer pool figure.</param>
    /// <param name="ArrayPoolNsPerItem">The <see cref="ArrayPool{T}.Shared"/> figure.</param>
    /// <param name="AllocatedBytesForTwoThousandItems">The caller's allocation over 2 000 pooled items.</param>
    internal readonly record struct BufferMeasurement(
        double PooledNsPerItem,
        double ArrayPoolNsPerItem,
        long AllocatedBytesForTwoThousandItems);
}
