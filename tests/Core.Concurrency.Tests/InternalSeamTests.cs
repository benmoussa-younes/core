// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Concurrency.Tests;

/// <summary>
/// Guards the internal seams whose branches a public-API test cannot reach: a
/// worker or pool disposed twice, a worker returned after the pool is disposed.
/// The suite has <c>InternalsVisibleTo</c>, so these run against the real types.
/// </summary>
public sealed class InternalSeamTests
{
    [Fact]
    public void Disposing_a_worker_twice_is_idempotent()
    {
        var worker = new Worker(index: 0, stackBytes: 256 * 1024);
        worker.Start();

        worker.Dispose();

        // The second dispose returns at the guard instead of joining twice.
        worker.Dispose();

        Assert.False(worker.Thread.IsAlive);
    }

    [Fact]
    public void Disposing_a_pool_twice_is_idempotent()
    {
        var pool = new WorkerPool(initialCount: 2, stackBytes: 256 * 1024);

        pool.Dispose();
        pool.Dispose();

        Assert.All(pool.WorkerThreads, thread => Assert.False(thread.IsAlive));
    }

    [Fact]
    public void Returning_workers_after_the_pool_is_disposed_is_a_no_op()
    {
        var pool = new WorkerPool(initialCount: 2, stackBytes: 256 * 1024);
        var rented = pool.Rent(1);

        pool.Dispose();

        // A call outliving the pool returns its workers to a closed pool; the
        // return must be dropped, not resurrect the worker or throw.
        pool.Return(rented);

        Assert.Equal(2, pool.Count);
    }

    [Fact]
    public void Growing_a_slot_to_a_length_it_already_has_keeps_the_same_array()
    {
        var slot = new Slot("test", initialCount: 2, create: _ => null, destroy: null);

        var grown = slot.GrowToFit(5);

        Assert.Equal(5, grown.Length);

        // A second request for a length already covered returns the same array: it
        // is not replaced with a smaller one.
        Assert.Same(grown, slot.GrowToFit(3));
        Assert.Same(grown, slot.GrowToFit(5));
    }

    [Fact]
    public void An_empty_slice_runs_no_body()
    {
        // The caller's slice is empty when it has no index of its own (a one-item
        // call split across two participants). Running it must be a no-op, not a
        // stray index or an exception.
        var failures = new FailureRecord(collectAll: false);
        var ran = 0;

        new Wave.Slice(
            start: 0,
            end: 0,
            step: 1,
            body: _ => Interlocked.Increment(ref ran),
            failures: failures,
            abortAction: null).Run();

        Assert.Equal(0, ran);
        Assert.True(failures.ShouldKeepGoing);
    }

    [Fact]
    public void The_pool_never_shrinks_below_its_steady_state()
    {
        var pool = new WorkerPool(initialCount: 3, stackBytes: 256 * 1024, maximumSize: 6);

        var grown = pool.Rent(6);
        Assert.Equal(3, pool.SteadyStateCount);
        Assert.Equal(6, pool.Count);

        pool.Return(grown);

        // The grown workers are returned, not destroyed.
        Assert.Equal(6, pool.Count);

        pool.Dispose();
    }

    [Fact]
    public void The_pool_reports_the_ceiling_it_was_created_with()
    {
        var pool = new WorkerPool(initialCount: 2, stackBytes: 256 * 1024, maximumSize: 4);

        Assert.Equal(4, pool.MaximumSize);
        pool.Dispose();
    }

    [Fact]
    public void Rent_never_creates_past_the_ceiling()
    {
        // The physical ceiling: Rent returns at most the ceiling workers even when
        // the caller asks for more, so the caller runs the deficit itself.
        var pool = new WorkerPool(initialCount: 2, stackBytes: 256 * 1024, maximumSize: 3);

        var rented = pool.Rent(10);

        Assert.Equal(3, rented.Length);
        Assert.Equal(3, pool.Count);

        pool.Dispose();
    }

    [Fact]
    public void A_draining_worker_is_not_replaced_past_the_ceiling()
    {
        // A Front abort returns while its workers drain; those workers are not idle,
        // so the next call cannot rent them — but the pool may NOT create fresh
        // threads past the ceiling to replace them. The deficit runs on the caller.
        var pool = new WorkerPool(initialCount: 2, stackBytes: 256 * 1024, maximumSize: 4);

        var first = pool.Rent(2);
        pool.BeginDrain(first.Length);

        // The two rented workers are draining; the pool holds 2 total, so it may
        // create up to the ceiling (4), not ceiling + draining.
        var second = pool.Rent(4);
        Assert.Equal(2, second.Length);
        Assert.Equal(4, pool.Count);
        Assert.Equal(2, pool.DrainingCount);

        pool.EndDrain(first.Length);
        pool.Return(first);
        pool.Return(second);

        // The draining count is back to zero; a later Rent cannot pass the ceiling.
        Assert.Equal(0, pool.DrainingCount);
        var third = pool.Rent(10);
        Assert.Equal(4, third.Length);

        pool.Dispose();
    }

    [Fact]
    public void The_idle_stack_grows_past_its_initial_capacity()
    {
        // The idle set is a stack with a 16-worker backing array; a pool with a
        // larger ceiling grows it. Rent then Return must preserve every worker.
        var pool = new WorkerPool(initialCount: 2, stackBytes: 256 * 1024, maximumSize: 40);

        var rented = pool.Rent(40);
        Assert.Equal(40, rented.Length);

        pool.Return(rented);

        // The returned workers refill the grown idle stack; a second full rent finds
        // them all idle (no new worker is created).
        var again = pool.Rent(40);
        Assert.Equal(40, again.Length);
        Assert.Equal(40, pool.Count);

        pool.Dispose();
    }

    [Fact]
    public void Disposing_a_failure_record_is_idempotent_enough_for_the_front_path()
    {
        var record = new FailureRecord(collectAll: false);

        record.Record(3, new InvalidOperationException("i3"));

        Assert.False(record.ShouldKeepGoing);
        Assert.Equal("i3", record.FirstFailure!.Message);
        Assert.True(record.Failed.IsSet);

        record.Dispose();
    }

    [Fact]
    public void A_collect_all_record_never_raises_the_front_signal()
    {
        var record = new FailureRecord(collectAll: true);

        record.Record(1, new InvalidOperationException("i1"));

        // Collect-all keeps going and does not set the stop flag or the front signal.
        Assert.True(record.ShouldKeepGoing);
        Assert.False(record.Failed.IsSet);

        record.Dispose();
    }
}
