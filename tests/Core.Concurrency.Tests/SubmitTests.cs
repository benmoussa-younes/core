// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Collections.Concurrent;

namespace Core.Concurrency.Tests;

/// <summary>
/// Guards the arbitrary-task level: <see cref="ConcurrencyContext.Submit"/>,
/// <see cref="ConcurrencyContext.SubmitBatch"/>,
/// <see cref="ConcurrencyContext.DispatchAndWait"/> and
/// <see cref="ConcurrencyContext.Run(Action)"/>, and the per-worker slot model.
/// </summary>
public sealed class SubmitTests
{
    [Fact]
    public void Dispatch_without_staged_tasks_is_a_completed_no_op()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        var result = context.DispatchAndWait();

        Assert.True(result.IsCompleted);
    }

    [Fact]
    public void Submit_stages_tasks_until_the_explicit_barrier()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var ran = 0;

        context.Submit(() => Interlocked.Increment(ref ran));
        context.Submit(() => Interlocked.Increment(ref ran));

        // Staged, not run: the barrier has not been crossed.
        Assert.Equal(0, ran);

        var result = context.DispatchAndWait();

        Assert.True(result.IsCompleted);
        Assert.Equal(2, ran);
    }

    [Fact]
    public void SubmitBatch_runs_every_task_of_the_wave()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var ran = 0;
        Action[] tasks =
        [
            () => Interlocked.Increment(ref ran),
            () => Interlocked.Increment(ref ran),
            () => Interlocked.Increment(ref ran),
        ];

        context.SubmitBatch(tasks);

        Assert.Equal(3, context.DispatchAndWait().Failures.Count == 0 ? ran : -1);
    }

    [Fact]
    public void DispatchAndWait_empties_the_stage()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        var ran = 0;
        context.Submit(() => Interlocked.Increment(ref ran));
        context.DispatchAndWait();

        // A second barrier runs nothing: the stage was emptied by the first.
        context.DispatchAndWait();

        Assert.Equal(1, ran);
    }

    [Fact]
    public void Run_is_submit_dispatch_and_wait_in_one_call()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var ran = 0;

        var result = context.Run(() => Interlocked.Increment(ref ran));

        Assert.True(result.IsCompleted);
        Assert.Equal(1, ran);
    }

    [Fact]
    public void Run_records_a_failure_without_throwing()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        var result = context.Run(() => throw new InvalidOperationException("boom"));

        Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
        Assert.IsType<InvalidOperationException>(result.FirstFailure);

        // ThrowIfFailed rethrows the recorded failure only when asked to.
        Assert.Throws<InvalidOperationException>(result.ThrowIfFailed);
    }

    [Fact]
    public void A_completed_result_does_not_throw()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        var result = context.Run(() => { });

        result.ThrowIfFailed();
        Assert.True(result.IsCompleted);
    }

    [Fact]
    public void A_barrier_with_more_tasks_than_workers_runs_them_all()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        var ran = 0;
        for (var index = 0; index < 100; index++)
        {
            context.Submit(() => Interlocked.Increment(ref ran));
        }

        var result = context.DispatchAndWait();

        Assert.True(result.IsCompleted);
        Assert.Equal(100, ran);
    }

    [Fact]
    public void A_barrier_collects_every_failure()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        context.Submit(() => throw new InvalidOperationException("a"));
        context.Submit(() => throw new InvalidOperationException("b"));

        var result = context.DispatchAndWait();

        Assert.Equal(ConcurrencyStatus.Faulted, result.Status);
        Assert.Equal(2, result.Failures.Count);
    }

    [Fact]
    public void WorkerIndex_is_negative_on_the_calling_thread()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        Assert.Equal(-1, context.WorkerIndex);
    }

    [Fact]
    public void WorkerIndex_is_stable_and_in_range_inside_a_worker()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 3 });

        var indices = new ConcurrentBag<long>();

        context.Run(() =>
        {
            indices.Add(context.WorkerIndex);
            indices.Add(context.WorkerIndex);
        });

        Assert.All(indices, index => Assert.InRange(index, 0, 2));
        Assert.Single(indices.Distinct());
    }

    [Fact]
    public void A_slot_instance_is_per_worker()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var created = 0;
        var slot = context.RegisterSlot(_ => new Holder(Interlocked.Increment(ref created)));

        var seen = new ConcurrentBag<int>();

        context.For(
            0,
            1_000,
            1,
            _ => seen.Add(context.WorkerSlot<Holder>(slot).Id),
            new ConcurrencyOptions { Partition = WorkPartition.StaticChunks });

        // One holder per worker, and each worker only ever saw its own: a static
        // chunk gives every worker a slice, so all four take part.
        Assert.Equal(4, created);
        Assert.Equal(4, seen.Distinct().Count());
    }

    [Fact]
    public void A_slot_is_created_lazily_and_reused()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        var created = 0;
        var slot = context.RegisterSlot(_ => new Holder(Interlocked.Increment(ref created)));

        context.Run(() =>
        {
            var first = context.WorkerSlot<Holder>(slot);
            var second = context.WorkerSlot<Holder>(slot);

            Assert.Same((object)first, (object)second);
        });

        Assert.Equal(1, created);
    }

    [Fact]
    public void Slots_are_destroyed_once_per_worker_on_dispose()
    {
        var created = new ConcurrentBag<int>();
        var destroyed = new ConcurrentBag<int>();
        var nextId = 0;
        var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 3 });

        var slot = context.RegisterSlot(
            _ =>
            {
                var holder = new Holder(Interlocked.Increment(ref nextId));
                created.Add(holder.Id);
                return holder;
            },
            holder => destroyed.Add(holder.Id));

        context.For(
            0,
            30,
            1,
            index => EnsureSlot(context, slot, index),
            new ConcurrencyOptions { Partition = WorkPartition.StaticChunks });

        // Nothing is destroyed while the context lives; a static chunk gives every
        // worker a slice, so every worker's value is created.
        Assert.Equal(3, created.Count);
        Assert.Empty(destroyed);

        context.Dispose();

        // Every created value is destroyed exactly once.
        Assert.Equal(created.Order(), destroyed.Order());
    }

    [Fact]
    public void A_slot_creates_a_value_for_a_worker_the_pool_grows_later()
    {
        // Steady state is 2, but a nested call grows the pool to 4. Pin the ceiling
        // to 4 so a 2-physical-core runner still exercises the growth path. The
        // slot array starts at 2 and must grow so the grown workers each get their
        // own value.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration
        {
            WorkerCount = 2,
            MaximumPoolSize = 4,
        });

        var created = new ConcurrentBag<int>();
        var slot = context.RegisterSlot(
            _ =>
            {
                var holder = new Holder(created.Count + 1);
                created.Add(holder.Id);
                return holder;
            });

        var seen = new ConcurrentDictionary<long, byte>();

        context.For(
            0,
            2,
            1,
            outer => context.For(
                0,
                2,
                1,
                inner =>
                {
                    _ = context.WorkerSlot<Holder>(slot);
                    seen.TryAdd(context.WorkerIndex, 0);
                },
                new ConcurrencyOptions { WorkerCount = 2 }),
            new ConcurrencyOptions { WorkerCount = 2 });

        // The pool grew past two, so at least one worker with an index beyond the
        // steady-state array size read its slot, which forced the array to grow;
        // every worker that ran got its own value.
        Assert.True(context.WorkerCount > 2, $"pool holds {context.WorkerCount}");
        Assert.True(seen.Keys.Any(index => index >= 2), $"indices {string.Join(",", seen.Keys)}");
        Assert.Equal(seen.Count, created.Count);
    }

    [Fact]
    public void A_slot_keeps_one_stable_instance_per_worker_across_concurrent_growth()
    {
        // BLOCKER 1 regression: the pool grows while workers first use the slot,
        // so a worker whose index is beyond the live array grows it. If that grow
        // is not coordinated with the first-use write, the worker's value lands in
        // a discarded array: the same worker gets a different instance on the next
        // call, the value is never destroyed and create runs twice. Hammer the
        // growth path from many workers and assert the three invariants.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        var created = 0;
        var destroyed = 0;
        var slot = context.RegisterSlot(
            _ =>
            {
                Interlocked.Increment(ref created);
                return new Holder(0);
            },
            _ => Interlocked.Increment(ref destroyed));

        var byWorker = new ConcurrentDictionary<long, Holder>();
        var identityViolations = 0;

        for (var round = 0; round < 20; round++)
        {
            // Outer 2 workers, inner up to 64: the inner call grows the pool to a
            // fresh high-water mark each round, racing GrowToFit with first-use.
            context.For(
                0,
                2,
                1,
                _ => context.For(
                    0,
                    64,
                    1,
                    _ =>
                    {
                        var holder = context.WorkerSlot<Holder>(slot);
                        var me = context.WorkerIndex;

                        byWorker.AddOrUpdate(
                            me,
                            holder,
                            (_, existing) =>
                            {
                                if (!ReferenceEquals(existing, holder))
                                {
                                    Interlocked.Increment(ref identityViolations);
                                }

                                return existing;
                            });
                    },
                    new ConcurrencyOptions { WorkerCount = 64 }),
                new ConcurrencyOptions { WorkerCount = 2 });
        }

        // One instance per worker for the whole context lifetime, created exactly
        // once per worker.
        Assert.Equal(0, identityViolations);
        Assert.Equal(byWorker.Count, created);

        context.Dispose();

        // No value is leaked: every created value is destroyed exactly once.
        Assert.Equal(created, destroyed);
    }

    [Fact]
    public void A_slot_registration_is_named_after_its_value_type()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        var slot = context.RegisterSlot(_ => new Holder(1));

        Assert.Equal(nameof(Holder), slot.Name);
    }

    [Fact]
    public void A_slot_read_outside_a_worker_is_a_programmer_error()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        var slot = context.RegisterSlot(_ => new Holder(1));

        Assert.Throws<InvalidOperationException>(() => context.WorkerSlot<Holder>(slot));
    }

    [Fact]
    public void A_null_slot_is_a_programmer_error()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        Assert.Throws<ArgumentNullException>(() => context.WorkerSlot<Holder>(null!));
    }

    [Fact]
    public void Submitting_a_null_task_is_a_programmer_error()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        Assert.Throws<ArgumentNullException>(() => context.Submit(null!));
        Assert.Throws<ArgumentNullException>(() => context.Run(null!));
    }

    private sealed class Holder(int id)
    {
        internal int Id { get; } = id;
    }

    private static void EnsureSlot(ConcurrencyContext context, SlotRegistration slot, long index) =>
        _ = context.WorkerSlot<Holder>(slot).Id + index;
}
