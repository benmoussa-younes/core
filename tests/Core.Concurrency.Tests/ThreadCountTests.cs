// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Concurrency.Platform.Linux;

namespace Core.Concurrency.Tests;

/// <summary>
/// Guards <see cref="ThreadCount.Resolve"/> (the sizing rule) and
/// <see cref="ThreadCount.ResolvePoolCap"/> (the design §4a HARD RULE: the pool's
/// total worker count never exceeds the <em>workload's</em> ceiling — physical
/// cores for CPU-bound work, logical processors for I/O-bound work — at any nesting
/// depth).
/// </summary>
public sealed class ThreadCountTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(256)]
    public void An_explicit_bound_is_honoured_verbatim(int explicitCount)
    {
        var config = new ConcurrencyConfiguration { WorkerCount = explicitCount };

        Assert.Equal(explicitCount, ThreadCount.Resolve(config));
    }

    [Fact]
    public void The_default_is_the_physical_cores_minus_one()
    {
        var expected = Math.Max(1, PhysicalCores() - 1);

        Assert.Equal(expected, ThreadCount.Resolve(ConcurrencyConfiguration.Default));
    }

    [Fact]
    public void Oversubscribing_selects_the_logical_processors_minus_one()
    {
        var expected = Math.Max(1, ThreadCount.LogicalProcessorCount() - 1);

        Assert.Equal(expected, ThreadCount.Resolve(new ConcurrencyConfiguration { AllowOversubscribe = true }));
    }

    [Fact]
    public void Oversubscribing_never_exceeds_the_logical_processors()
    {
        var resolved = ThreadCount.Resolve(new ConcurrencyConfiguration { AllowOversubscribe = true });

        Assert.InRange(resolved, 1, ThreadCount.LogicalProcessorCount());
    }

    [Fact]
    public void An_io_bound_workload_sizes_on_the_logical_processors()
    {
        var expected = Math.Max(1, ThreadCount.LogicalProcessorCount() - 1);

        Assert.Equal(expected, ThreadCount.Resolve(new ConcurrencyConfiguration { Workload = WorkloadKind.IoBound }));
    }

    [Fact]
    public void A_cpu_bound_workload_sizes_on_the_physical_cores()
    {
        var expected = Math.Max(1, PhysicalCores() - 1);

        Assert.Equal(expected, ThreadCount.Resolve(new ConcurrencyConfiguration { Workload = WorkloadKind.CpuBound }));
    }

    [Fact]
    public void An_unknown_workload_sizes_on_the_physical_cores_like_cpu_bound()
    {
        // Unknown is the default and is the CPU rule: SMT is not assumed to help.
        Assert.Equal(
            ThreadCount.Resolve(new ConcurrencyConfiguration { Workload = WorkloadKind.CpuBound }),
            ThreadCount.Resolve(new ConcurrencyConfiguration { Workload = WorkloadKind.Unknown }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(257)]
    [InlineData(int.MaxValue)]
    public void An_explicit_bound_outside_the_allowed_range_is_a_programmer_error(int explicitCount)
    {
        var config = new ConcurrencyConfiguration { WorkerCount = explicitCount };

        Assert.Throws<ArgumentOutOfRangeException>(() => ThreadCount.Resolve(config));
    }

    [Fact]
    public void A_null_config_is_a_programmer_error()
    {
        Assert.Throws<ArgumentNullException>(() => ThreadCount.Resolve(null!));
    }

    [Fact]
    public void The_default_pool_cap_is_the_physical_cores()
    {
        // The CPU-bound ceiling (the default and CpuBound) is the physical core
        // count — not the logical count, not cpus+1, not 256. The logical count is
        // the *I/O* ceiling, asserted in the next test.
        Assert.Equal(PhysicalCores(), ThreadCount.ResolvePoolCap(ConcurrencyConfiguration.Default));
        Assert.Equal(PhysicalCores(), ThreadCount.ResolvePoolCap(new ConcurrencyConfiguration { Workload = WorkloadKind.CpuBound }));
    }

    [Fact]
    public void An_io_bound_workload_raises_the_pool_cap_to_the_logical_processors()
    {
        // The I/O mode's ceiling is the logical count — legitimate, not
        // oversubscription — and even then the ceiling applies to nesting, so it
        // never multiplies.
        Assert.Equal(ThreadCount.LogicalProcessorCount(), ThreadCount.ResolvePoolCap(new ConcurrencyConfiguration { Workload = WorkloadKind.IoBound }));
        Assert.Equal(ThreadCount.LogicalProcessorCount(), ThreadCount.ResolvePoolCap(new ConcurrencyConfiguration { AllowOversubscribe = true }));
    }

    [Fact]
    public void An_explicit_pool_cap_wins()
    {
        // The resolved worker count is physical-1 on this host; a cap at or above it
        // is honoured exactly.
        var workerCount = ThreadCount.Resolve(ConcurrencyConfiguration.Default);

        Assert.Equal(workerCount, ThreadCount.ResolvePoolCap(new ConcurrencyConfiguration { MaximumPoolSize = workerCount }));
    }

    [Fact]
    public void The_pool_cap_never_falls_below_the_resolved_worker_count()
    {
        // A cap below the resolved steady-state count is raised to it: the pool must
        // be able to hold every worker the context creates at Create time.
        var workerCount = ThreadCount.Resolve(ConcurrencyConfiguration.Default);

        Assert.Equal(workerCount, ThreadCount.ResolvePoolCap(new ConcurrencyConfiguration { MaximumPoolSize = 1 }));

        // An explicit per-config worker count above the physical count likewise
        // leaves the pool able to hold every one of them.
        Assert.Equal(32, ThreadCount.ResolvePoolCap(new ConcurrencyConfiguration { WorkerCount = 32, MaximumPoolSize = 1 }));
    }

    [Fact]
    public void The_pool_cap_is_never_above_the_hard_maximum()
    {
        Assert.Equal(
            ConcurrencyConfiguration.MaximumWorkerCount,
            ThreadCount.ResolvePoolCap(new ConcurrencyConfiguration { MaximumPoolSize = int.MaxValue }));
    }

    [Fact]
    public void The_default_config_has_the_documented_values()
    {
        var config = ConcurrencyConfiguration.Default;

        Assert.Null(config.WorkerCount);
        Assert.False(config.AllowOversubscribe);
        Assert.Equal(WorkloadKind.Unknown, config.Workload);
        Assert.Null(config.MaximumPoolSize);
        Assert.Equal(32 * 1024 * 1024, config.WorkerStackBytes);
        Assert.False(config.PinToCpu);
        Assert.Equal(32 * 1024 * 1024, ConcurrencyConfiguration.DefaultWorkerStackBytes);
        Assert.Equal(1, ConcurrencyConfiguration.MinimumWorkerCount);
        Assert.Equal(256, ConcurrencyConfiguration.MaximumWorkerCount);
    }

    [Fact]
    public void The_default_options_are_the_documented_values()
    {
        var options = ConcurrencyOptions.Default;

        Assert.Equal(WorkPartition.ClaimChunk, options.Partition);
        Assert.Equal(ConcurrencyOptions.DefaultClaimChunkSize, options.ClaimChunkSize);
        Assert.Equal(64, options.ClaimChunkSize);
        Assert.Equal(AbortMode.Drain, options.AbortMode);
        Assert.False(options.CollectAllFailures);
        Assert.Null(options.AbortAction);
        Assert.Null(options.WorkerCount);
    }

    private static int PhysicalCores()
    {
        // The test only mirrors the rule; the physical count itself is the
        // library's internal topology read.
        return ProcessorTopology.PhysicalCoreCount();
    }
}
