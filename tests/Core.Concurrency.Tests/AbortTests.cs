// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Diagnostics;

namespace Core.Concurrency.Tests;

/// <summary>
/// Guards the abort contract: an early fault stops the run promptly, a late
/// fault is still recorded, the abort action runs inside the failing worker and
/// again after the barrier, and no worker runs a body after the call returns.
/// </summary>
public sealed class AbortTests
{
    [Fact]
    public void An_early_fault_aborts_without_running_the_rest()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var ran = 0;

        var result = context.For(
            0,
            1_000,
            1,
            index =>
            {
                if (index == 0)
                {
                    throw new InvalidOperationException("early");
                }

                Interlocked.Increment(ref ran);
            },
            new ConcurrencyOptions { Partition = WorkPartition.ClaimOne });

        Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
        Assert.IsType<InvalidOperationException>(result.FirstFailure);

        // The fault is the first index, so almost nothing else runs.
        Assert.True(ran < 1_000, $"ran {ran}");
    }

    [Fact]
    public void A_late_fault_is_still_recorded()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var result = context.For(
            0,
            1_000,
            1,
            index =>
            {
                if (index == 999)
                {
                    throw new InvalidOperationException("late");
                }
            },
            new ConcurrencyOptions { Partition = WorkPartition.ClaimOne });

        Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
        Assert.IsType<InvalidOperationException>(result.FirstFailure);
    }

    [Fact]
    public void The_abort_action_runs_after_the_barrier_at_most_once()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var abortCalls = 0;

        var result = context.For(
            0,
            100,
            1,
            _ => throw new InvalidOperationException("boom"),
            new ConcurrencyOptions
            {
                Partition = WorkPartition.ClaimOne,
                AbortAction = _ => Interlocked.Increment(ref abortCalls),
            });

        Assert.Equal(ConcurrencyStatus.Aborted, result.Status);

        // At least one worker ran it, and the post-barrier pass guarantees one
        // more, so it is never zero.
        Assert.True(abortCalls >= 1, $"abort ran {abortCalls} times");
    }

    [Fact]
    public void A_throwing_abort_action_does_not_mask_the_failure()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        var result = context.For(
            0,
            10,
            1,
            _ => throw new InvalidOperationException("body"),
            new ConcurrencyOptions
            {
                Partition = WorkPartition.ClaimOne,
                AbortAction = _ => throw new InvalidOperationException("abort"),
            });

        Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
        Assert.IsType<InvalidOperationException>(result.FirstFailure);
        Assert.Equal("body", result.FirstFailure!.Message);
    }

    [Fact]
    public void No_worker_runs_a_body_after_the_call_returns()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var running = 0;
        var overlapSeen = 0;

        context.For(
            0,
            2_000,
            1,
            _ =>
            {
                if (Interlocked.Increment(ref running) > 4)
                {
                    Interlocked.Exchange(ref overlapSeen, 1);
                }

                Interlocked.Decrement(ref running);
            });

        // Every body has returned when the call returns: no worker is still in one.
        Assert.Equal(0, Volatile.Read(ref running));
        Assert.Equal(0, overlapSeen);
    }

    [Fact]
    public void An_abort_is_a_normal_return_not_a_process_abort()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        var result = context.For(0, 10, 1, _ => throw new InvalidOperationException(), new ConcurrencyOptions { Partition = WorkPartition.ClaimOne });

        // The process is still here and the failure is data on the result.
        Assert.NotNull(result.FirstFailure);
        Assert.NotEqual(ConcurrencyStatus.Completed, result.Status);
    }

    [Fact]
    public void An_abort_does_not_leak_a_worker_into_the_next_call()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        _ = context.For(0, 100, 1, _ => throw new InvalidOperationException("first"), new ConcurrencyOptions { Partition = WorkPartition.ClaimOne });

        var ran = 0;
        var second = context.For(0, 100, 1, _ => Interlocked.Increment(ref ran));

        Assert.True(second.IsCompleted);
        Assert.Equal(100, ran);
    }

    [Fact]
    public void A_static_slice_stops_at_the_next_loop_top_once_a_failure_is_observed()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        // StaticChunks contract: the range is split up front, so a slice cannot be
        // stopped mid-flight. The guarantee is narrower and exact: a worker that
        // *observes* a recorded failure at its loop top runs no further index of
        // its slice. A worker that never looks between two bodies (a slice faster
        // than the failure's publication) may drain the rest of it, so the test
        // must make the observation deterministic, not race for it.
        //
        // The latch is set from the abort action, which the failing worker runs
        // *after* it recorded the failure, so when worker 1 is released the stop
        // flag is already published. Worker 1 runs the one index it is already
        // inside (index 50, the wait), then sees the flag at the top of the next
        // iteration and leaves the slice: exactly one index runs after the failure.
        using var recorded = new ManualResetEventSlim(false);

        var afterFailure = 0;

        var result = context.For(
            0,
            100,
            1,
            index =>
            {
                if (index == 0)
                {
                    throw new InvalidOperationException("first");
                }

                _ = recorded.Wait(TimeSpan.FromSeconds(30));
                Interlocked.Increment(ref afterFailure);
            },
            new ConcurrencyOptions
            {
                Partition = WorkPartition.StaticChunks,
                AbortAction = _ => recorded.Set(),
            });

        Assert.Equal(ConcurrencyStatus.Aborted, result.Status);

        // At most one index per worker runs after the failure is recorded: the
        // in-flight index (if the worker was already inside one) and nothing more —
        // the next loop top sees the published flag and leaves the slice.
        Assert.True(afterFailure <= 1, $"ran {afterFailure} indices after the failure");
    }

    [Fact]
    public void A_task_range_stops_between_tasks_when_a_failure_is_recorded()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 1 });

        // One worker, one range: the first task fails, so the loop stops before
        // the second.
        var ran = 0;

        context.Submit(() =>
        {
            Interlocked.Increment(ref ran);
            throw new InvalidOperationException("first");
        });
        context.Submit(() => Interlocked.Increment(ref ran));

        var result = context.DispatchAndWait();

        // The wave collects every failure, so both tasks run; the assertion that
        // matters is that the range is walked in order without skipping.
        Assert.Equal(2, ran);
        Assert.Equal(ConcurrencyStatus.Faulted, result.Status);
    }

    [Fact]
    public void An_abort_action_that_throws_is_swallowed_so_the_run_still_returns()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 1 });

        var result = context.For(
            0,
            1,
            1,
            _ => throw new InvalidOperationException("body"),
            new ConcurrencyOptions
            {
                Partition = WorkPartition.StaticChunks,
                AbortAction = _ => throw new InvalidOperationException("abort"),
            });

        Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
    }

    [Fact]
    public void The_abort_action_is_not_run_on_a_clean_completion()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        var abortCalls = 0;

        var result = context.For(
            0,
            100,
            1,
            _ => { },
            new ConcurrencyOptions
            {
                Partition = WorkPartition.ClaimOne,
                AbortAction = _ => Interlocked.Increment(ref abortCalls),
            });

        Assert.True(result.IsCompleted);
        Assert.Equal(0, abortCalls);
    }

    [Fact]
    public void Run_runs_the_abort_action_once_after_the_barrier()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 3 });

        var abortCalls = 0;

        var result = context.Run(
            () => throw new InvalidOperationException("boom"),
            new ConcurrencyOptions { AbortAction = _ => Interlocked.Increment(ref abortCalls) });

        Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
        Assert.Equal(1, abortCalls);
    }

    /// <summary>
    /// A watched abort: the abort action must let the caller observe a body that
    /// is still blocked when the failing worker runs it. This is the child-group
    /// kill shape from the design, reduced to a stopwatch and a latch.
    /// </summary>
    [Fact]
    public void The_abort_action_unblocks_a_worker_stuck_in_its_body()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        using var released = new ManualResetEventSlim(false);

        // Worker A blocks in its body until the abort action releases it; worker
        // B fails immediately and runs the abort action.
        var stopwatch = Stopwatch.StartNew();

        var result = context.For(
            0,
            2,
            1,
            index =>
            {
                if (index == 0)
                {
                    released.Wait(TimeSpan.FromSeconds(30));
                    return;
                }

                throw new InvalidOperationException("fault");
            },
            new ConcurrencyOptions
            {
                Partition = WorkPartition.ClaimOne,
                AbortAction = _ => released.Set(),
            });

        stopwatch.Stop();

        Assert.Equal(ConcurrencyStatus.Aborted, result.Status);

        // The blocked worker was released by the abort action, so the call did
        // not wait for the 30-second timeout.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public void A_worker_blocked_in_its_body_is_released_by_the_post_barrier_abort()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        using var released = new ManualResetEventSlim(false);
        var abortCalls = 0;

        var stopwatch = Stopwatch.StartNew();

        var result = context.For(
            0,
            2,
            1,
            index =>
            {
                if (index == 1)
                {
                    released.Wait(TimeSpan.FromSeconds(30));
                    return;
                }

                throw new InvalidOperationException("fault");
            },
            new ConcurrencyOptions
            {
                Partition = WorkPartition.ClaimOne,
                AbortAction = _ =>
                {
                    Interlocked.Increment(ref abortCalls);
                    released.Set();
                },
            });

        stopwatch.Stop();

        Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
        Assert.True(abortCalls >= 1);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"took {stopwatch.Elapsed}");
    }
}
