// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Diagnostics;

namespace Core.Concurrency.Tests;

/// <summary>
/// Guards <see cref="AbortMode.Front"/>: it returns as soon as the first failure
/// is recorded — well under one item's duration — while still reporting the
/// lowest-index failure, and it stays stable under repeated calls (the drain
/// accounting must not starve the next call).
/// </summary>
/// <remarks>
/// The spike measured a front-abort at item 0 falling from 5.08 ms to 0.04 ms
/// with this mode, and a repeated-call starvation (318 ms) removed by the
/// <c>BeginDrain</c>/<c>EndDrain</c> accounting. These are the guards.
/// </remarks>
public sealed class FrontAbortTests
{
    [Fact]
    public void A_front_failure_returns_in_well_under_one_item_duration()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 8 });

        var stopwatch = Stopwatch.StartNew();

        var result = context.For(
            0,
            64,
            1,
            index =>
            {
                if (index == 0)
                {
                    throw new InvalidOperationException("front");
                }

                Thread.Sleep(200);
            },
            new ConcurrencyOptions
            {
                Partition = WorkPartition.ClaimOne,
                AbortMode = AbortMode.Front,
                AbortAction = _ => { },
            });

        stopwatch.Stop();

        Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
        Assert.Equal("front", result.FirstFailure!.Message);

        // The floor for a draining pool is one in-flight item (200 ms here); Front
        // must beat that decisively. The spike measured < 100 ms on a 200 ms item.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(150),
            $"front abort took {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
    }

    [Fact]
    public void A_front_failure_reports_the_lowest_index()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 8 });

        var result = context.For(
            0,
            64,
            1,
            index =>
            {
                if (index is 0 or 5 or 20)
                {
                    throw new InvalidOperationException($"i{index}");
                }

                Thread.Sleep(5);
            },
            new ConcurrencyOptions
            {
                Partition = WorkPartition.ClaimOne,
                AbortMode = AbortMode.Front,
                AbortAction = _ => { },
            });

        Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
        Assert.Equal("i0", result.FirstFailure!.Message);
    }

    [Fact]
    public void A_front_failure_under_a_chunked_partition_still_reports_the_lowest_index()
    {
        // The mode runs the claim-one arm even when a chunked partition was
        // requested, so the lowest-index guarantee holds and the abort stays prompt.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 8 });

        var stopwatch = Stopwatch.StartNew();

        var result = context.For(
            0,
            64,
            1,
            index =>
            {
                if (index == 0)
                {
                    throw new InvalidOperationException("front");
                }

                Thread.Sleep(200);
            },
            new ConcurrencyOptions
            {
                // Ask for the chunked partition; Front must not pair it with a
                // 64-item chunk (the spike measured that at 318 ms).
                Partition = WorkPartition.ClaimChunk,
                ClaimChunkSize = 64,
                AbortMode = AbortMode.Front,
                AbortAction = _ => { },
            });

        stopwatch.Stop();

        Assert.Equal("front", result.FirstFailure!.Message);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(150),
            $"front abort with a chunked partition took {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
    }

    [Fact]
    public void Repeated_front_aborts_do_not_starve_the_next_call()
    {
        // The drain accounting regression: without BeginDrain/EndDrain the workers
        // of a front-aborted call were kept out of the idle set for the whole drain,
        // so repeated calls starved (318 ms). Each repeated call must return
        // promptly.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 8 });

        var total = TimeSpan.Zero;

        for (var round = 0; round < 10; round++)
        {
            var stopwatch = Stopwatch.StartNew();

            var result = context.For(
                0,
                64,
                1,
                index =>
                {
                    if (index == 0)
                    {
                        throw new InvalidOperationException("front");
                    }

                    Thread.Sleep(50);
                },
                new ConcurrencyOptions
                {
                    Partition = WorkPartition.ClaimOne,
                    AbortMode = AbortMode.Front,
                    AbortAction = _ => { },
                });

            stopwatch.Stop();
            total += stopwatch.Elapsed;

            Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromMilliseconds(150),
                $"round {round} took {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
        }

        // The whole ten-call campaign is bounded: no single call, and not the run,
        // pays the 50 ms drain of the previous call.
        Assert.True(total < TimeSpan.FromMilliseconds(1_000), $"ten front aborts took {total.TotalMilliseconds:F1} ms");
    }

    [Fact]
    public void A_front_abort_is_a_normal_return_and_the_pool_keeps_working()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        _ = context.For(
            0,
            32,
            1,
            index =>
            {
                if (index == 0)
                {
                    throw new InvalidOperationException("front");
                }

                Thread.Sleep(10);
            },
            new ConcurrencyOptions
            {
                Partition = WorkPartition.ClaimOne,
                AbortMode = AbortMode.Front,
                AbortAction = _ => { },
            });

        // The very next call completes normally, with every index, despite the
        // in-flight bodies the front call left draining in the background.
        var ran = 0;
        var second = context.For(0, 1_000, 1, _ => Interlocked.Increment(ref ran));

        Assert.True(second.IsCompleted);
        Assert.Equal(1_000, ran);
    }

    [Fact]
    public void A_front_abort_that_completes_cleanly_still_waits_for_every_item()
    {
        // Front only shortens the *failing* path: a clean run must still run every
        // item and return a completed result.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var ran = 0;

        var result = context.For(
            0,
            1_000,
            1,
            _ => Interlocked.Increment(ref ran),
            new ConcurrencyOptions
            {
                Partition = WorkPartition.ClaimOne,
                AbortMode = AbortMode.Front,
            });

        Assert.True(result.IsCompleted);
        Assert.Equal(1_000, ran);
    }

    [Fact]
    public void A_front_abort_runs_the_abort_action()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var abortCalls = 0;

        var result = context.For(
            0,
            32,
            1,
            index =>
            {
                if (index == 0)
                {
                    throw new InvalidOperationException("front");
                }

                Thread.Sleep(5);
            },
            new ConcurrencyOptions
            {
                Partition = WorkPartition.ClaimOne,
                AbortMode = AbortMode.Front,
                AbortAction = _ => Interlocked.Increment(ref abortCalls),
            });

        Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
        Assert.True(abortCalls >= 1, $"abort ran {abortCalls} times");
    }

    [Fact]
    public void Collect_all_overrides_front_and_runs_to_completion()
    {
        // CollectAllFailures and Front are contradictory; the collect-all contract
        // wins, so every index runs and every failure is gathered in order.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var ran = 0;

        var result = context.For(
            0,
            64,
            1,
            index =>
            {
                Interlocked.Increment(ref ran);
                throw new InvalidOperationException($"i{index}");
            },
            new ConcurrencyOptions
            {
                Partition = WorkPartition.ClaimOne,
                AbortMode = AbortMode.Front,
                CollectAllFailures = true,
            });

        Assert.Equal(ConcurrencyStatus.Faulted, result.Status);
        Assert.Equal(64, ran);
        Assert.Equal(64, result.Failures.Count);
    }

    [Fact]
    public void The_drain_accounting_never_lets_the_pool_exceed_its_ceiling()
    {
        // A front-aborted call's workers are draining in the background; the pool may
        // replace them up to the ceiling but never past it.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2, WorkerStackBytes = 1024 * 1024 });
        var ceiling = Platform.Linux.ProcessorTopology.PhysicalCoreCount();

        for (var round = 0; round < 20; round++)
        {
            _ = context.For(
                0,
                32,
                1,
                index =>
                {
                    if (index == 0)
                    {
                        throw new InvalidOperationException("front");
                    }

                    Thread.Sleep(1);
                },
                new ConcurrencyOptions
                {
                    Partition = WorkPartition.ClaimOne,
                    AbortMode = AbortMode.Front,
                    AbortAction = _ => { },
                });
        }

        Assert.True(context.WorkerCount <= ceiling, $"pool holds {context.WorkerCount}, ceiling {ceiling}");
    }
}
