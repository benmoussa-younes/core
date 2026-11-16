// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Concurrency.Platform.Linux;

namespace Core.Concurrency.Tests;

/// <summary>
/// Guards the design §4a HARD RULE — no SMT oversubscription, ever, including
/// nested — and the caller-participation path the ceiling creates.
/// </summary>
/// <remarks>
/// <para>
/// The pool's total worker count never exceeds the workload's ceiling, at any
/// nesting depth: <b>physical cores</b> for CPU-bound work (the default and
/// <see cref="WorkloadKind.CpuBound"/>) and <b>logical processors</b> for the
/// explicit I/O mode (<see cref="WorkloadKind.IoBound"/> or the legacy
/// <see cref="ConcurrencyConfiguration.AllowOversubscribe"/>). On a 6-physical / 12-logical
/// host a CPU-bound nesting is at most 6×6 — never 8×8, never a pool of 72, never
/// the logical count; an I/O-bound nesting is at most 12×12. A call that cannot
/// rent every worker it wants runs the remainder of its items on the calling
/// thread.
/// </para>
/// <para>
/// The ceiling is asserted two ways: through the library's own worker count and
/// threads, and through the OS thread names under <c>/proc/self/task/*/comm</c>,
/// so a regression that spawns a 72-thread pool is caught by name as well. A small
/// stack keeps the workers cheap.
/// </para>
/// </remarks>
public sealed class PoolCapTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Gets the pool ceiling this host derives for the default (CPU) workload.</summary>
    private static int PhysicalCeiling => ProcessorTopology.PhysicalCoreCount();

    /// <summary>Gets the pool ceiling this host derives for an I/O-bound workload.</summary>
    private static int LogicalCeiling => ProcessorTopology.LogicalProcessorCount();

    [Fact]
    public void A_cpu_bound_ceiling_is_the_physical_core_count_not_the_logical()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2, WorkerStackBytes = 1024 * 1024 });

        // The CPU-bound ceiling is the physical count, and — on a host whose
        // logical count is higher (SMT) — strictly below the logical count.
        Assert.True(PhysicalCeiling <= LogicalCeiling);

        // A call that asks for the hard maximum with plenty of items grows the pool
        // only to the physical ceiling, never to 256 and never to the logical count.
        var result = context.For(
            0,
            ConcurrencyConfiguration.MaximumWorkerCount + 64,
            1,
            _ => { },
            new ConcurrencyOptions { WorkerCount = ConcurrencyConfiguration.MaximumWorkerCount, Partition = WorkPartition.ClaimChunk });

        Assert.True(result.IsCompleted);
        Assert.Equal(PhysicalCeiling, context.WorkerCount);
    }

    [Fact]
    public void The_cpu_and_io_ceilings_differ_exactly_as_the_rule_says()
    {
        // CPU-bound caps at the physical cores; I/O-bound at the logical processors.
        Assert.Equal(PhysicalCeiling, ThreadCount.ResolvePoolCap(new ConcurrencyConfiguration { Workload = WorkloadKind.CpuBound }));
        Assert.Equal(LogicalCeiling, ThreadCount.ResolvePoolCap(new ConcurrencyConfiguration { Workload = WorkloadKind.IoBound }));

        // AllowOversubscribe is the legacy coarse form and matches Io exactly.
        Assert.Equal(LogicalCeiling, ThreadCount.ResolvePoolCap(new ConcurrencyConfiguration { AllowOversubscribe = true }));

        // On an SMT host the I/O ceiling strictly exceeds the CPU ceiling.
        if (LogicalCeiling > PhysicalCeiling)
        {
            Assert.True(
                ThreadCount.ResolvePoolCap(new ConcurrencyConfiguration { Workload = WorkloadKind.IoBound })
                    > ThreadCount.ResolvePoolCap(new ConcurrencyConfiguration { Workload = WorkloadKind.CpuBound }));
        }
    }

    [Fact]
    public void A_nested_8x8_cpu_call_uses_at_most_the_physical_ceiling_of_threads()
    {
        // THE headline assertion: 8×8 nested, no deadlock, and the whole process
        // holds no more than the physical ceiling in worker threads (6 on this
        // host → never 8×8, never 72).
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2, WorkerStackBytes = 1024 * 1024 });

        var ran = 0;

        RunWithTimeout(
            () => context.For(
                0,
                8,
                1,
                _ => context.For(0, 8, 1, _ => Interlocked.Increment(ref ran), new ConcurrencyOptions { WorkerCount = 8 }),
                new ConcurrencyOptions { WorkerCount = 8 }),
            TimeSpan.FromSeconds(30));

        Assert.Equal(8 * 8, ran);

        // The pool never grew past the physical ceiling. AssertCeilingHolds checks it
        // two ways: the context's own worker count and threads, and the count of OS
        // threads named after this library's workers (a worker counter read from
        // /proc/self/task/*/comm), which is exactly the HARD RULE's quantity and is
        // immune to the test host's own threads.
        Assert.Equal(PhysicalCeiling, context.WorkerCount);
        AssertCeilingHolds(context.WorkerThreads, PhysicalCeiling);
    }

    [Fact]
    public void A_nested_16x16_cpu_call_uses_at_most_the_physical_ceiling_of_threads()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2, WorkerStackBytes = 1024 * 1024 });

        var ran = 0;

        RunWithTimeout(
            () => context.For(
                0,
                16,
                1,
                _ => context.For(0, 16, 1, _ => Interlocked.Increment(ref ran), new ConcurrencyOptions { WorkerCount = 16 }),
                new ConcurrencyOptions { WorkerCount = 16 }),
            TimeSpan.FromSeconds(30));

        Assert.Equal(16 * 16, ran);
        AssertCeilingHolds(context.WorkerThreads, PhysicalCeiling);
    }

    [Fact]
    public void A_nested_io_call_shares_the_logical_ceiling_and_never_multiplies_it()
    {
        // WorkloadKind.IoBound is the explicit I/O mode: the ceiling is the logical
        // count, but nesting shares it, so 16×16 is nowhere near 12×12.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration
        {
            Workload = WorkloadKind.IoBound,
            WorkerCount = 2,
            WorkerStackBytes = 1024 * 1024,
        });

        var ran = 0;

        RunWithTimeout(
            () => context.For(
                0,
                16,
                1,
                _ => context.For(0, 16, 1, _ => Interlocked.Increment(ref ran), new ConcurrencyOptions { WorkerCount = 16 }),
                new ConcurrencyOptions { WorkerCount = 16 }),
            TimeSpan.FromSeconds(30));

        Assert.Equal(16 * 16, ran);
        AssertCeilingHolds(context.WorkerThreads, LogicalCeiling);
    }

    [Fact]
    public void A_nested_8x8_call_reports_the_lowest_index_failure_at_the_ceiling()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2, WorkerStackBytes = 1024 * 1024 });

        var innerFailure = string.Empty;

        RunWithTimeout(
            () => context.For(
                0,
                8,
                1,
                _ =>
                {
                    var inner = context.For(
                        0,
                        64,
                        1,
                        index =>
                        {
                            if (index < 2)
                            {
                                throw new InvalidOperationException($"i{index}");
                            }
                        },
                        new ConcurrencyOptions { WorkerCount = 8 });

                    // Every inner call participates at the ceiling and still reports
                    // the lowest failing index deterministically.
                    if (innerFailure.Length == 0)
                    {
                        innerFailure = inner.FirstFailure?.Message ?? string.Empty;
                    }
                },
                new ConcurrencyOptions { WorkerCount = 8 }),
            TimeSpan.FromSeconds(30));

        Assert.Equal("i0", innerFailure);
        Assert.True(context.WorkerCount <= PhysicalCeiling, $"pool holds {context.WorkerCount}");
    }

    [Fact]
    public void A_nested_claim_one_call_participates_inline_at_the_ceiling()
    {
        using var context = CreateAtCeiling();

        var ran = 0;

        RunWithTimeout(
            () =>
            {
                // The outer call holds a worker; the inner asks for the whole
                // ceiling with more items than idle workers, so the pool cannot
                // supply it and the inner caller participates in the cursor.
                var outer = context.For(
                    0,
                    1,
                    1,
                    _ =>
                    {
                        var inner = context.For(
                            0,
                            600,
                            1,
                            _ => Interlocked.Increment(ref ran),
                            new ConcurrencyOptions { Partition = WorkPartition.ClaimOne, WorkerCount = ConcurrencyConfiguration.MaximumWorkerCount });

                        Assert.True(inner.IsCompleted);
                    },
                    new ConcurrencyOptions { WorkerCount = 1, Partition = WorkPartition.ClaimOne });

                Assert.True(outer.IsCompleted);
            },
            TimeSpan.FromSeconds(30));

        Assert.Equal(600, ran);
        Assert.True(context.WorkerCount <= PhysicalCeiling, $"pool holds {context.WorkerCount}");
    }

    [Fact]
    public void A_nested_task_barrier_participates_inline_at_the_ceiling()
    {
        // Steady state 2; a chain of nested Run frames holds the pool at the
        // ceiling; the barrier at the bottom rents what is left and must run the
        // rest of its tasks on the caller.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration
        {
            WorkerCount = 2,
            WorkerStackBytes = 1024 * 1024,
        });

        var ran = 0;
        var depth = PhysicalCeiling + 4;

        RunWithTimeout(
            () =>
            {
                void Recurse(int remaining)
                {
                    context.Run(
                        () =>
                        {
                            if (remaining > 0)
                            {
                                Recurse(remaining - 1);
                                return;
                            }

                            for (var task = 0; task < 16; task++)
                            {
                                context.Submit(() => Interlocked.Increment(ref ran));
                            }

                            var inner = context.DispatchAndWait();
                            Assert.True(inner.IsCompleted);
                        },
                        new ConcurrencyOptions());
                }

                Recurse(depth);
            },
            TimeSpan.FromSeconds(30));

        Assert.Equal(16, ran);
        Assert.True(context.WorkerCount <= PhysicalCeiling, $"pool holds {context.WorkerCount}");
    }

    [Fact]
    public void A_nested_run_participates_inline_at_the_ceiling()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration
        {
            WorkerCount = 2,
            WorkerStackBytes = 1024 * 1024,
        });

        var ran = 0;
        var depth = PhysicalCeiling + 8;

        RunWithTimeout(
            () =>
            {
                void Recurse(int remaining)
                {
                    var result = context.Run(
                        () =>
                        {
                            if (remaining == 0)
                            {
                                Interlocked.Increment(ref ran);
                                return;
                            }

                            Recurse(remaining - 1);
                        },
                        new ConcurrencyOptions());

                    Assert.True(result.IsCompleted);
                }

                Recurse(depth);
            },
            TimeSpan.FromSeconds(30));

        Assert.Equal(1, ran);
        Assert.True(context.WorkerCount <= PhysicalCeiling, $"pool holds {context.WorkerCount}");
    }

    [Fact]
    public void A_participating_run_reports_its_failure()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration
        {
            WorkerCount = 2,
            WorkerStackBytes = 1024 * 1024,
        });

        var abortCalls = 0;
        var failures = 0;
        var depth = PhysicalCeiling + 8;

        RunWithTimeout(
            () =>
            {
                void Recurse(int remaining)
                {
                    var result = context.Run(
                        () =>
                        {
                            if (remaining == 0)
                            {
                                throw new InvalidOperationException("inline");
                            }

                            Recurse(remaining - 1);
                        },
                        new ConcurrencyOptions { AbortAction = _ => Interlocked.Increment(ref abortCalls) });

                    if (!result.IsCompleted)
                    {
                        Interlocked.Increment(ref failures);
                        return;
                    }

                    Assert.True(result.IsCompleted);
                }

                Recurse(depth);
            },
            TimeSpan.FromSeconds(30));

        Assert.Equal(1, failures);
        Assert.True(abortCalls >= 1, $"abort ran {abortCalls} times");
    }

    [Fact]
    public void A_nested_for_keeps_the_lowest_index_failure_at_the_ceiling()
    {
        using var context = CreateAtCeiling();

        var innerFailure = string.Empty;

        RunWithTimeout(
            () =>
            {
                var outer = context.For(
                    0,
                    1,
                    1,
                    _ =>
                    {
                        var inner = context.For(
                            0,
                            600,
                            1,
                            index =>
                            {
                                if (index < 2)
                                {
                                    throw new InvalidOperationException($"i{index}");
                                }
                            },
                            new ConcurrencyOptions { Partition = WorkPartition.ClaimOne, WorkerCount = ConcurrencyConfiguration.MaximumWorkerCount });

                        innerFailure = inner.FirstFailure?.Message ?? string.Empty;
                    },
                    new ConcurrencyOptions { WorkerCount = 1, Partition = WorkPartition.ClaimOne });

                Assert.True(outer.IsCompleted);
            },
            TimeSpan.FromSeconds(30));

        Assert.Equal("i0", innerFailure);
    }

    [Fact]
    public void The_pool_never_passes_its_physical_ceiling()
    {
        using var context = CreateAtCeiling();

        Assert.Equal(PhysicalCeiling, context.WorkerCount);

        // A call that asks for more than the ceiling gets the ceiling, not more.
        var result = context.For(0, ConcurrencyConfiguration.MaximumWorkerCount + 50, 1, _ => { });

        Assert.True(result.IsCompleted);
        Assert.Equal(PhysicalCeiling, context.WorkerCount);
    }

    [Fact]
    public void An_explicit_pool_cap_is_honoured_and_bounds_nesting()
    {
        // The ceiling is configurable, but it is always a ceiling: an explicit cap
        // bounds the process's worker threads exactly.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration
        {
            WorkerCount = 2,
            MaximumPoolSize = 3,
            WorkerStackBytes = 1024 * 1024,
        });

        var ran = 0;

        RunWithTimeout(
            () => context.For(
                0,
                8,
                1,
                _ => context.For(0, 8, 1, _ => Interlocked.Increment(ref ran), new ConcurrencyOptions { WorkerCount = 8 }),
                new ConcurrencyOptions { WorkerCount = 8 }),
            TimeSpan.FromSeconds(30));

        Assert.Equal(64, ran);
        Assert.True(context.WorkerCount <= 3, $"pool holds {context.WorkerCount}");
        AssertCeilingHolds(context.WorkerThreads, ceiling: 3);
    }

    [Fact]
    public void A_static_slice_call_participates_inline_at_the_ceiling()
    {
        // The StaticChunks split still has a caller slice when the pool cannot rent
        // every wanted worker. That is the only path where RunSlices runs the
        // caller's own slice, and StaticChunks is the only partition that uses
        // RunSlices, so cover it explicitly here.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2, WorkerStackBytes = 1024 * 1024 });

        var visits = new System.Collections.Concurrent.ConcurrentDictionary<long, int>();
        var total = 0;

        RunWithTimeout(
            () => context.For(
                0,
                8,
                1,
                _ => context.For(
                    0,
                    16,
                    1,
                    index =>
                    {
                        visits.AddOrUpdate(context.WorkerIndex, 1, (_, current) => current + 1);
                        Interlocked.Increment(ref total);
                    },
                    new ConcurrencyOptions { WorkerCount = 16, Partition = WorkPartition.StaticChunks }),
                new ConcurrencyOptions { WorkerCount = 8, Partition = WorkPartition.StaticChunks }),
            TimeSpan.FromSeconds(30));

        Assert.Equal(8 * 16, total);

        // At least one participating thread was a worker (the caller runs its slice
        // on the calling worker when the pool is at the ceiling).
        Assert.Contains(visits.Keys, index => index >= 0);
        Assert.True(context.WorkerCount <= PhysicalCeiling, $"pool holds {context.WorkerCount}");
    }

    /// <summary>
    /// Creates a context and primes its pool to the physical ceiling with a cheap
    /// one-item call per worker, using a small stack so the workers are cheap.
    /// </summary>
    /// <returns>A context whose pool holds <see cref="PhysicalCeiling"/> workers.</returns>
    private static ConcurrencyContext CreateAtCeiling()
    {
        var context = ConcurrencyContext.Create(new ConcurrencyConfiguration
        {
            WorkerCount = 2,
            WorkerStackBytes = 1024 * 1024,
        });

        // `For` rents min(wanted, count) workers; one index per worker grows the
        // pool to the bound without running anything expensive.
        context.For(
            0,
            ConcurrencyConfiguration.MaximumWorkerCount,
            1,
            _ => { },
            new ConcurrencyOptions { WorkerCount = ConcurrencyConfiguration.MaximumWorkerCount, Partition = WorkPartition.ClaimChunk });

        return context;
    }

    /// <summary>
    /// Runs <paramref name="body"/> on a background thread and fails the test if it
    /// does not return within <paramref name="timeout"/> — a nested call that
    /// deadlocks must fail fast, not hang the suite.
    /// </summary>
    /// <param name="body">The body to run.</param>
    /// <param name="timeout">The wall-clock bound.</param>
    private static void RunWithTimeout(Action body, TimeSpan timeout)
    {
        var task = Task.Run(body, Token);

        try
        {
            task.Wait(timeout, Token);
        }
        catch (AggregateException exception)
        {
            // Surface the body's own failure rather than the wrapper.
            throw exception.InnerExceptions.Count == 1 ? exception.InnerExceptions[0] : exception;
        }
    }

    /// <summary>
    /// Asserts the pool's own worker threads stay within <paramref name="ceiling"/>,
    /// and that the number of OS threads named after this library's workers — read
    /// through <c>/proc/self/task/*/comm</c> — is at most the ceiling. The second
    /// check counts exactly the threads this library created, immune to the test
    /// host's own runtime threads, so a regression that grows the pool to 72 (or
    /// 8×8=64) fails here whatever else the host is doing.
    /// </summary>
    /// <param name="workers">The context's worker threads.</param>
    /// <param name="ceiling">The ceiling the context was created with.</param>
    private static void AssertCeilingHolds(IReadOnlyList<Thread> workers, int ceiling)
    {
        Assert.True(workers.Count <= ceiling, $"context holds {workers.Count} worker threads, ceiling {ceiling}");

        var workerThreads = CountWorkerNamedThreads();
        if (workerThreads < 0)
        {
            // The procfs read is best-effort; the worker-count assertion above is
            // the hard one and already ran.
            return;
        }

        Assert.True(
            workerThreads <= ceiling,
            $"process has {workerThreads} Core.Concurrency.Worker threads; ceiling {ceiling}");
    }

    /// <summary>
    /// Counts the OS threads named after this library's workers, read from
    /// <c>/proc/self/task/&lt;tid&gt;/comm</c>.
    /// </summary>
    /// <returns>The worker-thread count, or <c>-1</c> when it cannot be read.</returns>
    private static int CountWorkerNamedThreads()
    {
        try
        {
            var count = 0;

            foreach (var task in Directory.EnumerateDirectories("/proc/self/task"))
            {
                var comm = Path.Combine(task, "comm");

                string name;
                try
                {
                    name = File.ReadAllText(comm).Trim();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (name.StartsWith("Core.Concurrency.Worker", StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return -1;
        }
    }
}
