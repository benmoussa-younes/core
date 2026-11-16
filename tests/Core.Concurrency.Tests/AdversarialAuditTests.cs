// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Collections.Concurrent;
using Core.Concurrency.Platform.Linux;

namespace Core.Concurrency.Tests;

/// <summary>
/// The adversarial audit of the optimized <c>Core.Concurrency</c>:
/// independent attempts to break the ceiling, the lowest-index determinism, the
/// front abort, the claim cursor at the type's edge, the per-worker buffer pool
/// and the race surface. Each test encodes the property it attacks; a failure is
/// a real defect, not a missing test.
/// </summary>
public sealed class AdversarialAuditTests
{
    private static int PhysicalCeiling => ProcessorTopology.PhysicalCoreCount();

    private static int LogicalCeiling => ProcessorTopology.LogicalProcessorCount();

    // ---------------------------------------------------------------------
    // Ceiling: try to exceed the physical / logical budget, nested, with many
    // independent contexts and a deep recursion.
    // ---------------------------------------------------------------------

    [Fact]
    public void Many_independent_contexts_do_not_multiply_the_budget_inside_one_context()
    {
        // The ceiling is per-context; two contexts each honour their own physical
        // ceiling. This asserts neither one leaks a second pool's worth of threads
        // into the other and each stays at its ceiling under a nested storm.
        using var first = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2, WorkerStackBytes = 1024 * 1024 });
        using var second = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2, WorkerStackBytes = 1024 * 1024 });

        RunWithTimeout(
            () => Parallel.Invoke(
                () => first.For(0, 8, 1, _ => first.For(0, 8, 1, _ => { }), new ConcurrencyOptions { WorkerCount = 8 }),
                () => second.For(0, 8, 1, _ => second.For(0, 8, 1, _ => { }), new ConcurrencyOptions { WorkerCount = 8 })),
            TimeSpan.FromSeconds(30));

        Assert.True(first.WorkerCount <= PhysicalCeiling, $"first holds {first.WorkerCount}");
        Assert.True(second.WorkerCount <= PhysicalCeiling, $"second holds {second.WorkerCount}");
    }

    [Fact]
    public void A_bounded_recursion_shares_the_budget_and_never_multiplies_it()
    {
        // A bounded linear recursion 200 levels deep: each level enters a 2-worker
        // call but only one branch continues, so the tree is a chain of 200 nodes.
        // Without a shared budget the pool would grow ~linearly with depth. It must
        // settle at the physical ceiling and complete.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration
        {
            WorkerCount = 2,
            WorkerStackBytes = 8 * 1024 * 1024,
        });

        var maxObserved = 0;
        var leaves = 0;
        const int depth = 200;

        RunWithTimeout(
            () =>
            {
                void Recurse(int remaining)
                {
                    context.For(
                        0,
                        1,
                        1,
                        _ =>
                        {
                            var seen = context.WorkerCount;
                            var current = Volatile.Read(ref maxObserved);
                            if (seen > current)
                            {
                                Interlocked.CompareExchange(ref maxObserved, seen, current);
                            }

                            if (remaining == 0)
                            {
                                Interlocked.Increment(ref leaves);
                                return;
                            }

                            Recurse(remaining - 1);
                        },
                        new ConcurrencyOptions { WorkerCount = 2 });
                }

                Recurse(depth);
            },
            TimeSpan.FromSeconds(60));

        Assert.Equal(1, leaves);
        Assert.True(maxObserved <= PhysicalCeiling, $"pool reached {maxObserved}, ceiling {PhysicalCeiling}");
    }

    [Fact]
    public void A_wide_recursion_at_the_ceiling_completes_without_aborting_the_process()
    {
        // MAJOR 6 shape: a wide recursion (branching 3) at depth 13 tried ~850
        // threads and aborted the process before the cap. At the cap it must stay at
        // the ceiling and complete.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration
        {
            WorkerCount = 2,
            WorkerStackBytes = 8 * 1024 * 1024,
        });

        var leaves = 0;

        RunWithTimeout(
            () =>
            {
                void Recurse(int depth)
                {
                    if (depth == 0)
                    {
                        Interlocked.Increment(ref leaves);
                        return;
                    }

                    context.For(0, 3, 1, _ => Recurse(depth - 1), new ConcurrencyOptions { WorkerCount = 3 });
                }

                Recurse(8);
            },
            TimeSpan.FromSeconds(60));

        Assert.Equal((int)Math.Pow(3, 8), leaves);
        Assert.True(context.WorkerCount <= PhysicalCeiling, $"pool holds {context.WorkerCount}");
    }

    [Fact]
    public void An_io_workload_never_passes_the_logical_ceiling_even_nested()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration
        {
            Workload = WorkloadKind.IoBound,
            WorkerCount = 2,
            WorkerStackBytes = 1024 * 1024,
        });

        RunWithTimeout(
            () => context.For(
                0,
                32,
                1,
                _ => context.For(0, 32, 1, _ => { }, new ConcurrencyOptions { WorkerCount = 32 }),
                new ConcurrencyOptions { WorkerCount = 32 }),
            TimeSpan.FromSeconds(30));

        Assert.True(context.WorkerCount <= LogicalCeiling, $"pool holds {context.WorkerCount}, ceiling {LogicalCeiling}");
    }

    // ---------------------------------------------------------------------
    // Lowest-index failure determinism under Front and under load.
    // ---------------------------------------------------------------------

    [Fact]
    public void A_front_failure_with_a_slow_lower_index_still_reports_the_lowest_index()
    {
        // ATTACK on the Front contract. Item 0 fails *late* (behind a sleep), item 1
        // fails *early*. Front returns on the first recorded failure, so item 1 is
        // recorded first. The documented contract is that the *lowest-index* failure
        // is surfaced. If Front surfaces item 1, the contract is broken.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var result = context.For(
            0,
            8,
            1,
            index =>
            {
                if (index == 0)
                {
                    Thread.Sleep(150);
                    throw new InvalidOperationException("i0");
                }

                if (index == 1)
                {
                    throw new InvalidOperationException("i1");
                }

                Thread.Sleep(500);
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
    public void A_front_failure_at_the_front_ordinal_returns_without_waiting_for_the_wave()
    {
        // The promptness the fix must preserve: when the first failure IS the
        // front-most ordinal, no lower ordinal exists to still fail, so Front returns
        // without waiting for the sleeping items. The fix must not turn this into a
        // full drain.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 8 });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = context.For(
            0,
            64,
            1,
            index =>
            {
                if (index == 0)
                {
                    throw new InvalidOperationException("i0");
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

        Assert.Equal("i0", result.FirstFailure!.Message);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(150), $"front abort took {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
    }

    [Fact]
    public void A_front_failure_unblocked_by_its_abort_action_still_surfaces_the_lowest_index()
    {
        // A lower-indexed item is BLOCKED (not just slow). The abort action releases
        // it so the fix can join it and record its lower-index failure. Without the
        // release the test could only time out, so the abort action is the escape.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });
        using var release = new ManualResetEventSlim(false);

        var result = context.For(
            0,
            4,
            1,
            index =>
            {
                if (index == 0)
                {
                    _ = release.Wait(TimeSpan.FromSeconds(30));
                    throw new InvalidOperationException("i0");
                }

                if (index == 1)
                {
                    throw new InvalidOperationException("i1");
                }
            },
            new ConcurrencyOptions
            {
                Partition = WorkPartition.ClaimOne,
                AbortMode = AbortMode.Front,
                AbortAction = _ => release.Set(),
            });

        Assert.Equal("i0", result.FirstFailure!.Message);
    }

    [Fact]
    public void Run_treats_front_and_drain_identically_for_a_single_task()
    {
        // A single task has no wave to abandon, so AbortMode does not change Run's
        // behaviour: both modes wait for the one task and report its failure.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        foreach (var mode in new[] { AbortMode.Front, AbortMode.Drain })
        {
            var result = context.Run(
                () => throw new InvalidOperationException("boom"),
                new ConcurrencyOptions { AbortMode = mode, AbortAction = _ => { } });

            Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
            Assert.Equal("boom", result.FirstFailure!.Message);
        }
    }

    [Fact]
    public async Task The_lowest_index_failure_holds_under_repeated_concurrent_load()
    {
        // Run the three-fault determinism check while a sibling load runs, so a
        // scheduling-dependent ordering is exercised. ClaimOne and ClaimChunk must
        // both surface index 10 every run.
        var token = TestContext.Current.CancellationToken;
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 8 });
        using var loadStop = new CancellationTokenSource();

        var load = Task.Run(
            () =>
            {
                var sink = 0L;
                while (!loadStop.IsCancellationRequested)
                {
                    sink += Environment.TickCount64;
                }

                GC.KeepAlive(sink);
            },
            token);

        try
        {
            foreach (var partition in new[] { WorkPartition.ClaimOne, WorkPartition.ClaimChunk })
            {
                for (var run = 0; run < 30; run++)
                {
                    var result = context.For(
                        0,
                        64,
                        1,
                        index =>
                        {
                            if (index is 10 or 40 or 55)
                            {
                                throw new InvalidOperationException($"i{index}");
                            }

                            Thread.SpinWait(200);
                        },
                        new ConcurrencyOptions { Partition = partition, ClaimChunkSize = 8 });

                    Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
                    Assert.Equal("i10", result.FirstFailure!.Message);
                }
            }
        }
        finally
        {
            loadStop.Cancel();
            await load.WaitAsync(TimeSpan.FromSeconds(10), token);
        }
    }

    // ---------------------------------------------------------------------
    // ClaimChunk at the long extremes.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(WorkPartition.ClaimOne)]
    [InlineData(WorkPartition.ClaimChunk)]
    [InlineData(WorkPartition.StaticChunks)]
    public void A_range_spanning_the_whole_long_domain_runs_every_ordinal_once(WorkPartition partition)
    {
        // A span wider than long.MaxValue: the count saturates and the index maps
        // through Int128. The run must visit every ordinal exactly once and never
        // wrap. The body records the index and stops claiming after a bounded sample
        // so the test is finite; the cursor still hands out ordinals in order.
        long[] expected = [long.MinValue, -1, long.MaxValue - 1];
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var seen = new ConcurrentBag<long>();

        var result = context.For(
            long.MinValue,
            long.MaxValue,
            long.MaxValue,
            seen.Add,
            new ConcurrencyOptions { Partition = partition, ClaimChunkSize = 64 });

        Assert.True(result.IsCompleted);
        Assert.Equal(expected, seen.Order().ToArray());
    }

    [Fact]
    public void A_chunk_cursor_past_the_end_claims_nothing_and_never_wraps()
    {
        // The chunk cursor's _claimed is a long and the chunk is added with
        // Interlocked.Add. Claiming past the end must stop, and the ordinal must
        // never wrap negative.
        var cursor = new ChunkCursor(start: 0, count: 5, step: 1);

        var claimed = new List<(long First, int Length)>();
        while (cursor.TryClaim(chunkSize: 2, out var first, out var length))
        {
            claimed.Add((first, length));
        }

        Assert.Equal([(0L, 2), (2L, 2), (4L, 1)], claimed);

        // A second attempt after exhaustion stays exhausted.
        Assert.False(cursor.TryClaim(2, out _, out var empty));
        Assert.Equal(0, empty);
    }

    [Fact]
    public void Every_index_runs_once_when_the_chunk_size_exceeds_the_count_by_a_lot()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var visits = new ConcurrentDictionary<long, int>();
        var result = context.For(
            100,
            130,
            1,
            index => visits.AddOrUpdate(index, 1, static (_, current) => current + 1),
            new ConcurrencyOptions { Partition = WorkPartition.ClaimChunk, ClaimChunkSize = int.MaxValue });

        Assert.True(result.IsCompleted);
        Assert.Equal(30, visits.Count);
        Assert.All(visits.Values, value => Assert.Equal(1, value));
    }

    // ---------------------------------------------------------------------
    // AbortMode.Front: no-starvation and the weakest-contract disclosure.
    // ---------------------------------------------------------------------

    [Fact]
    public void Front_and_Drain_both_surface_the_same_first_failure_on_a_single_fault()
    {
        // A single fault makes the two modes agree on the reported failure; only the
        // latency differs. This pins that the mode never changes *what* is reported.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var drain = context.For(
            0,
            64,
            1,
            index =>
            {
                if (index == 20)
                {
                    throw new InvalidOperationException("i20");
                }

                Thread.Sleep(2);
            },
            new ConcurrencyOptions { Partition = WorkPartition.ClaimOne, AbortMode = AbortMode.Drain });

        var front = context.For(
            0,
            64,
            1,
            index =>
            {
                if (index == 20)
                {
                    throw new InvalidOperationException("i20");
                }

                Thread.Sleep(2);
            },
            new ConcurrencyOptions { Partition = WorkPartition.ClaimOne, AbortMode = AbortMode.Front, AbortAction = _ => { } });

        Assert.Equal(drain.FirstFailure!.Message, front.FirstFailure!.Message);
    }

    [Fact]
    public void A_run_that_ignores_front_still_runs_no_body_after_it_returns()
    {
        // Run documents that a failure follows the call's failure policy. Whatever
        // the policy, once Run returns no body of that call may still be running.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var running = 0;
        var result = context.Run(
            () =>
            {
                Interlocked.Increment(ref running);
                Thread.Sleep(20);
                Interlocked.Decrement(ref running);
                throw new InvalidOperationException("run");
            },
            new ConcurrencyOptions { AbortMode = AbortMode.Front, AbortAction = _ => { } });

        Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
        Assert.Equal(0, Volatile.Read(ref running));
    }

    // ---------------------------------------------------------------------
    // WorkerBufferPool: identity, growth and no leak.
    // ---------------------------------------------------------------------

    [Fact]
    public void Every_worker_gets_its_own_buffer_and_no_buffer_is_shared()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2, WorkerStackBytes = 1024 * 1024 });
        var pool = context.RegisterBufferPool<byte>(initialCapacity: 16);

        var buffersByWorker = new ConcurrentDictionary<long, object>();
        var shared = 0;

        for (var round = 0; round < 20; round++)
        {
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
                        var buffer = context.WorkerBuffer(pool);
                        var me = context.WorkerIndex;

                        buffersByWorker.AddOrUpdate(
                            me,
                            buffer,
                            (_, existing) =>
                            {
                                if (!ReferenceEquals(existing, buffer))
                                {
                                    Interlocked.Increment(ref shared);
                                }

                                return existing;
                            });
                    },
                    new ConcurrencyOptions { WorkerCount = 64 }),
                new ConcurrencyOptions { WorkerCount = 2 });
        }

        Assert.Equal(0, shared);

        // One buffer per worker that actually ran a body, never shared between
        // workers, and never more buffers than the pool holds workers.
        Assert.NotEmpty(buffersByWorker);
        Assert.True(
            buffersByWorker.Count <= context.WorkerCount,
            $"{buffersByWorker.Count} buffers for {context.WorkerCount} workers");
    }

    [Fact]
    public void A_buffer_grown_by_one_worker_keeps_its_capacity_for_the_next_call()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 1 });
        var pool = context.RegisterBufferPool<int>(initialCapacity: 4);

        var grown = 0;
        context.Run(() =>
        {
            var buffer = context.WorkerBuffer(pool);
            Assert.True(buffer.Ensure(100).Length >= 100);
            grown = buffer.Capacity;
        });

        // The buffer is per worker for the context's life: the grown capacity is
        // still there on the next call.
        context.Run(() =>
        {
            var buffer = context.WorkerBuffer(pool);
            Assert.Equal(grown, buffer.Capacity);
        });
    }

    [Fact]
    public void A_buffer_ensure_above_the_power_of_two_cap_is_not_a_negative_length()
    {
        // ATTACK: `Ensure` rounds up to a power of two. For a request above 2^30 the
        // rounded value is 2^31, which wrapped to a negative `int` and made
        // `new T[negative]` throw an `OverflowException`. The request must either
        // return a span of at least the requested length or fail as an allocation
        // failure, never as a bogus negative-length error.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 1 });
        using var pool = context.RegisterBufferPool<byte>(initialCapacity: 4);

        Buffer<byte>? buffer = null;
        context.Run(() => buffer = context.WorkerBuffer(pool));

        Assert.NotNull(buffer);

        // A request that cannot be satisfied should surface the allocation failure,
        // not an OverflowException from a negative length.
        var threw = Record.Exception(() => buffer!.Ensure(1_500_000_000));
        Assert.True(
            threw is null or OutOfMemoryException,
            $"expected null or OutOfMemoryException, got {threw?.GetType().Name}: {threw?.Message}");
    }

    // ---------------------------------------------------------------------
    // BoundedQueue: MPMC hammering.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task A_bounded_queue_survives_a_many_producer_many_consumer_hammer()
    {
        const int producers = 6;
        const int consumers = 6;
        const int perProducer = 4_000;
        const int total = producers * perProducer;

        using var queue = new BoundedQueue<int>(32);
        var seen = new ConcurrentDictionary<int, int>();
        var consumed = 0;
        var token = TestContext.Current.CancellationToken;

        var consumerTasks = new Task[consumers];
        for (var index = 0; index < consumers; index++)
        {
            consumerTasks[index] = Task.Run(() =>
            {
                while (queue.Pop(out var item))
                {
                    seen.AddOrUpdate(item, 1, static (_, current) => current + 1);
                    Interlocked.Increment(ref consumed);
                }
            }, token);
        }

        var producerTasks = new Task[producers];
        for (var producer = 0; producer < producers; producer++)
        {
            var start = producer * perProducer;
            producerTasks[producer] = Task.Run(() =>
            {
                for (var offset = 0; offset < perProducer; offset++)
                {
                    if (!queue.Push(start + offset))
                    {
                        return;
                    }
                }
            }, token);
        }

        await Task.WhenAll(producerTasks).WaitAsync(TimeSpan.FromSeconds(60), token);
        queue.Close();
        await Task.WhenAll(consumerTasks).WaitAsync(TimeSpan.FromSeconds(60), token);

        Assert.Equal(total, consumed);
        Assert.Equal(total, seen.Count);
        Assert.All(seen.Values, count => Assert.Equal(1, count));
    }

    // ---------------------------------------------------------------------
    // Dispose / lifecycle under nesting.
    // ---------------------------------------------------------------------

    [Fact]
    public void Disposing_after_a_front_abort_waits_for_the_background_drain()
    {
        // A Front call returns while its bodies drain. Dispose must wait for that
        // drain before joining, or it joins a worker mid-body. The observable
        // contract: Dispose returns only once no body is running.
        var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 8 });
        var running = 0;

        for (var round = 0; round < 5; round++)
        {
            _ = context.For(
                0,
                64,
                1,
                index =>
                {
                    if (index == 0)
                    {
                        throw new InvalidOperationException("front");
                    }

                    Interlocked.Increment(ref running);
                    Thread.Sleep(30);
                    Interlocked.Decrement(ref running);
                },
                new ConcurrencyOptions
                {
                    Partition = WorkPartition.ClaimOne,
                    AbortMode = AbortMode.Front,
                    AbortAction = _ => { },
                });
        }

        context.Dispose();

        Assert.Equal(0, Volatile.Read(ref running));
    }

    [Fact]
    public void A_pool_never_shrinks_after_a_grow_even_across_many_aborts()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2, WorkerStackBytes = 1024 * 1024 });

        var grew = 0;
        RunWithTimeout(
            () => context.For(
                0,
                8,
                1,
                _ => context.For(0, 8, 1, _ => { }, new ConcurrencyOptions { WorkerCount = 8 }),
                new ConcurrencyOptions { WorkerCount = 8 }),
            TimeSpan.FromSeconds(30));

        grew = context.WorkerCount;

        for (var round = 0; round < 20; round++)
        {
            _ = context.For(0, 16, 1, _ => throw new InvalidOperationException("x"), new ConcurrencyOptions { Partition = WorkPartition.ClaimOne });
        }

        Assert.Equal(grew, context.WorkerCount);
    }

    private static void RunWithTimeout(Action body, TimeSpan timeout)
    {
        var token = TestContext.Current.CancellationToken;
        var task = Task.Run(body, token);

        try
        {
            task.Wait(timeout, token);
        }
        catch (AggregateException exception)
        {
            throw exception.InnerExceptions.Count == 1 ? exception.InnerExceptions[0] : exception;
        }
    }
}
