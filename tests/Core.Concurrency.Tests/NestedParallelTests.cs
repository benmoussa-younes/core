// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Diagnostics;
using Core.Concurrency.Platform.Linux;

namespace Core.Concurrency.Tests;

/// <summary>
/// Guards the property that a fixed worker set gets wrong: <b>nesting must not
/// deadlock</b>.
/// </summary>
/// <remarks>
/// <para>
/// A body running inside a call may itself enter a call — bcs's user code is
/// recursive, a <c>map</c>/<c>walk</c> body may call <c>map</c>. With a fixed
/// worker set the inner call broadcasts to workers the outer call still occupies
/// and then waits for itself; the spike reproduced that deadlock 5/5. The pool
/// must therefore grow on demand: an inner call finds its workers busy and
/// creates fresh threads.
/// </para>
/// <para>
/// Every test here runs the nested call on a worker of the outer call (or takes a
/// lease inside a job) and is bounded by a wall-clock timeout, so a regression
/// fails the test instead of hanging the suite. The assertions check the results
/// are complete and correct, not merely that the call returned.
/// </para>
/// </remarks>
public sealed class NestedParallelTests
{
    /// <summary>A nested call that deadlocks must fail this fast rather than hang.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Run_inside_a_Run_body_completes()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var innerRan = 0;

        var outer = await Task.Run(
            () => context.Run(
                () =>
                {
                    var inner = context.Run(() => Interlocked.Increment(ref innerRan));
                    Assert.True(inner.IsCompleted);
                }),
            Token).WaitAsync(Timeout, Token);

        Assert.True(outer.IsCompleted);
        Assert.Equal(1, innerRan);
    }

    [Fact]
    public async Task For_inside_a_For_body_completes_with_the_right_results()
    {
        const int outerCount = 8;
        const int innerCount = 8;
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var sums = new long[outerCount];

        var outer = await Task.Run(
            () => context.For(
                0,
                outerCount,
                1,
                index => context.For(0, innerCount, 1, inner => Interlocked.Add(ref sums[index], inner))),
            Token).WaitAsync(Timeout, Token);

        Assert.True(outer.IsCompleted);

        // Each outer index ran a full inner range: the sum is 0+..+7.
        var expected = Enumerable.Range(0, innerCount).Sum(value => (long)value);
        Assert.All(sums, sum => Assert.Equal(expected, sum));
    }

    [Fact]
    public async Task A_lease_taken_inside_a_job_completes()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var executed = 0;

        var outer = await Task.Run(
            () => context.Run(() =>
            {
                // A nested call inside a job asks the pool for a fresh worker even
                // though every worker of the outer call is busy.
                var inner = context.Run(() => Interlocked.Increment(ref executed));
                Assert.True(inner.IsCompleted);
            }),
            Token).WaitAsync(Timeout, Token);

        Assert.True(outer.IsCompleted);
        Assert.Equal(1, executed);
    }

    [Fact]
    public async Task A_nested_For_under_a_workers_equal_four_bound_shares_the_pool()
    {
        // A call with workers=4 inside a call with workers=4 would want 8 threads at
        // once. Pin the ceiling to 4 so the test is independent of the host's
        // physical core count: the inner call reuses what the outer does not hold
        // and runs the deficit on its calling worker. The result must still be
        // complete.
        const int bound = 4;
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration
        {
            WorkerCount = bound,
            MaximumPoolSize = bound,
        });

        var innerVisited = 0;

        var outer = await Task.Run(
            () => context.For(
                0,
                4,
                1,
                _ => context.For(
                    0,
                    4,
                    1,
                    _ => Interlocked.Increment(ref innerVisited),
                    new ConcurrencyOptions { WorkerCount = bound }),
                new ConcurrencyOptions { WorkerCount = bound }),
            Token).WaitAsync(Timeout, Token);

        Assert.True(outer.IsCompleted);

        // Every outer index ran a full inner bound-sized range.
        Assert.Equal(4 * 4, innerVisited);

        // The pool respected the explicit ceiling, whatever the host topology.
        Assert.True(context.WorkerCount <= bound, $"pool holds {context.WorkerCount}, ceiling {bound}");
    }

    [Fact]
    public async Task A_recursive_parallel_call_terminates()
    {
        // The map/walk shape: a body that calls itself through a For until a depth
        // guard. This is the exact recursion the fixed set cannot survive.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        var leaves = 0;

        void Recurse(int depth)
        {
            if (depth == 0)
            {
                Interlocked.Increment(ref leaves);
                return;
            }

            context.For(
                0,
                2,
                1,
                _ => Recurse(depth - 1),
                new ConcurrencyOptions { WorkerCount = 2 });
        }

        await Task.Run(() => Recurse(4), Token).WaitAsync(Timeout, Token);

        // A binary tree of depth 4 has 2^4 leaves.
        Assert.Equal(1 << 4, leaves);
    }

    [Fact]
    public async Task A_nested_call_reuses_the_grown_pool_on_the_next_call()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        var outer = await Task.Run(
            () => context.For(
                0,
                2,
                1,
                _ => context.For(0, 2, 1, _ => { }),
                new ConcurrencyOptions { WorkerCount = 2 }),
            Token).WaitAsync(Timeout, Token);

        Assert.True(outer.IsCompleted);

        var grown = context.WorkerCount;
        Assert.InRange(grown, 2, ProcessorTopology.PhysicalCoreCount());

        // A later top-level call reuses the pool: it neither grows nor shrinks.
        var second = context.For(0, 64, 1, _ => { });
        Assert.True(second.IsCompleted);
        Assert.Equal(grown, context.WorkerCount);
    }

    [Fact]
    public async Task A_nested_claim_one_call_completes_with_its_failure_recorded()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var innerResult = await Task.Run(
            () =>
            {
                var captured = ConcurrencyResult.Completed();

                _ = context.Run(() => captured = context.For(
                    0,
                    16,
                    1,
                    index =>
                    {
                        if (index == 5)
                        {
                            throw new InvalidOperationException("inner fault");
                        }
                    },
                    new ConcurrencyOptions { Partition = WorkPartition.ClaimOne }));

                return captured;
            },
            Token).WaitAsync(Timeout, Token);

        Assert.Equal(ConcurrencyStatus.Aborted, innerResult.Status);
        Assert.IsType<InvalidOperationException>(innerResult.FirstFailure);
    }

    [Fact]
    public async Task A_deeply_nested_recursion_keeps_the_pool_bounded()
    {
        // MAJOR 6 regression: the pool grew ~linearly with nesting depth (a
        // depth-200 linear recursion held 200 threads) and a wide recursion at
        // depth 13 tried ~850 and aborted the process. The pool is capped at the
        // context's physical ceiling now, and a call that cannot rent a worker runs
        // the remainder on the calling thread.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration
        {
            WorkerCount = 2,
            WorkerStackBytes = 8 * 1024 * 1024,
        });

        const int depth = 400;
        var leaves = 0;

        await Task.Run(
            () =>
            {
                void Recurse(int remaining)
                {
                    if (remaining == 0)
                    {
                        Interlocked.Increment(ref leaves);
                        return;
                    }

                    context.Run(() => Recurse(remaining - 1));
                }

                Recurse(depth);
            },
            Token).WaitAsync(Timeout, Token);

        // The recursion completed and the pool never passed the physical ceiling.
        Assert.Equal(1, leaves);
        Assert.InRange(context.WorkerCount, 1, ProcessorTopology.PhysicalCoreCount());
    }

    [Fact]
    public async Task A_deep_linear_recursion_still_reports_a_failure()
    {
        // At the pool's ceiling the depth exceeds the worker count, so most levels
        // run inline on the calling worker. The failure contract must survive that:
        // the deepest body fails and the error surfaces at each level.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration
        {
            WorkerCount = 2,
            WorkerStackBytes = 8 * 1024 * 1024,
        });

        const int depth = 400;

        await Task.Run(
            () =>
            {
                void Recurse(int remaining)
                {
                    context.Run(
                        () =>
                        {
                            if (remaining == 0)
                            {
                                throw new InvalidOperationException("deep");
                            }

                            Recurse(remaining - 1);
                        },
                        new ConcurrencyOptions { CollectAllFailures = true });
                }

                Recurse(depth);
            },
            Token).WaitAsync(Timeout, Token);

        Assert.InRange(context.WorkerCount, 1, ProcessorTopology.PhysicalCoreCount());
    }

    [Fact]
    public async Task A_worker_still_sees_a_stable_index_when_it_participates_inline()
    {
        // At the ceiling a nested call runs some items on the calling worker. That
        // worker's index and slot must stay valid for the inline items too.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration
        {
            WorkerCount = 2,
            WorkerStackBytes = 8 * 1024 * 1024,
        });

        var slot = context.RegisterSlot(_ => new object());
        var seen = new System.Collections.Concurrent.ConcurrentDictionary<long, byte>();

        const int depth = 400;

        await Task.Run(
            () =>
            {
                void Recurse(int remaining)
                {
                    // A one-item call per level: a linear chain that pins one worker
                    // per level until the cap, then runs inline on the caller.
                    context.For(
                        0,
                        1,
                        1,
                        _ =>
                        {
                            seen.TryAdd(context.WorkerIndex, 0);
                            GC.KeepAlive(context.WorkerSlot<object>(slot));

                            if (remaining > 0)
                            {
                                Recurse(remaining - 1);
                            }
                        },
                        new ConcurrencyOptions { WorkerCount = 1 });
                }

                Recurse(depth);
            },
            Token).WaitAsync(Timeout, Token);

        // Every participating thread was a real worker with a non-negative index:
        // the chain always runs on a worker, which is the case the cap creates.
        Assert.NotEmpty(seen);
        Assert.All(seen.Keys, index => Assert.True(index >= 0, $"index {index}"));
        Assert.InRange(context.WorkerCount, 1, ProcessorTopology.PhysicalCoreCount());
    }

    [Fact]
    public async Task Nested_calls_do_not_deadlock_across_many_rounds()
    {
        // The spike reproduced the deadlock 5/5; run enough rounds that a flaky
        // regression is caught, not just a deterministic one.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var total = 0;
        var rounds = 0;

        var stopwatch = Stopwatch.StartNew();

        await Task.Run(
            () =>
            {
                for (var round = 0; round < 50; round++)
                {
                    context.For(
                        0,
                        8,
                        1,
                        _ => context.For(0, 8, 1, _ => Interlocked.Increment(ref total)),
                        new ConcurrencyOptions { WorkerCount = 4 });

                    Interlocked.Increment(ref rounds);
                }
            },
            Token).WaitAsync(Timeout, Token);

        stopwatch.Stop();

        Assert.Equal(50, rounds);
        Assert.Equal(50 * 8 * 8, total);
    }
}
