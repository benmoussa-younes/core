// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Concurrency.Tests;

/// <summary>
/// Guards <see cref="ConcurrencyContext.For(long, long, long, Action{long}, ConcurrencyOptions)"/>:
/// both partitions compute the same result, the range is split and never
/// duplicated, and a failure stops the run as the partition promises.
/// </summary>
public sealed class ForTests
{
    [Theory]
    [InlineData(WorkPartition.StaticChunks)]
    [InlineData(WorkPartition.ClaimOne)]
    [InlineData(WorkPartition.ClaimChunk)]
    public void Every_index_runs_exactly_once_in_both_partitions(WorkPartition partition)
    {
        const int count = 10_000;
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var visits = new int[count];

        var result = context.For(0, count, 1, index => Interlocked.Increment(ref visits[index]), new ConcurrencyOptions { Partition = partition });

        Assert.True(result.IsCompleted);

        // Every slot was visited once: the sum is the count and no slot is 0 or 2.
        Assert.Equal(count, visits.Sum());
        Assert.DoesNotContain(0, visits);
        Assert.DoesNotContain(2, visits);
    }

    [Theory]
    [InlineData(WorkPartition.StaticChunks)]
    [InlineData(WorkPartition.ClaimOne)]
    [InlineData(WorkPartition.ClaimChunk)]
    public void A_start_end_and_step_are_honoured(WorkPartition partition)
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var visited = new System.Collections.Concurrent.ConcurrentBag<long>();

        var result = context.For(3, 30, 4, visited.Add, new ConcurrencyOptions { Partition = partition });

        Assert.True(result.IsCompleted);

        // 3, 7, 11, 15, 19, 23, 27 - the indices [3, 30) with stride 4.
        Assert.Equal([3L, 7L, 11L, 15L, 19L, 23L, 27L], visited.Order().ToArray());
    }

    [Theory]
    [InlineData(WorkPartition.StaticChunks)]
    [InlineData(WorkPartition.ClaimOne)]
    [InlineData(WorkPartition.ClaimChunk)]
    public void An_empty_range_completes_without_running_the_body(WorkPartition partition)
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var ran = 0;

        var result = context.For(5, 5, 1, _ => Interlocked.Increment(ref ran), new ConcurrencyOptions { Partition = partition });

        Assert.True(result.IsCompleted);
        Assert.Equal(0, ran);
    }

    [Theory]
    [InlineData(WorkPartition.StaticChunks)]
    [InlineData(WorkPartition.ClaimOne)]
    [InlineData(WorkPartition.ClaimChunk)]
    public void A_single_worker_runs_the_whole_range_in_order(WorkPartition partition)
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 1 });

        var order = new List<long>();

        var result = context.For(0, 100, 1, order.Add, new ConcurrencyOptions { Partition = partition });

        Assert.True(result.IsCompleted);
        Assert.Equal(Enumerable.Range(0, 100).Select(index => (long)index), order);
    }

    [Fact]
    public void A_claim_one_failure_stops_new_claims()
    {
        const int count = 1_000;
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var claimed = 0;

        var result = context.For(
            0,
            count,
            1,
            _ =>
            {
                Interlocked.Increment(ref claimed);
                throw new InvalidOperationException("stop");
            },
            new ConcurrencyOptions { Partition = WorkPartition.ClaimOne });

        Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
        Assert.IsType<InvalidOperationException>(result.FirstFailure);

        // A failing body records one claim per worker at most: the first claim to
        // fail sets the stop flag, and no worker claims a new item after that, so
        // far fewer than count items are ever claimed.
        Assert.True(claimed < count, $"claimed {claimed} of {count}");
    }

    [Fact]
    public void The_lowest_index_failure_is_reported_whatever_the_completion_order()
    {
        // MAJOR 3 regression: the surfaced failure must be the lowest-index item's,
        // not whichever worker finished first. Indices 0 and 1 both fail with equal
        // work; over many runs the winner of the race changes, but the reported
        // index must not.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        for (var run = 0; run < 50; run++)
        {
            var result = context.For(
                0,
                100,
                1,
                index =>
                {
                    if (index < 2)
                    {
                        throw new InvalidOperationException($"i{index}");
                    }
                },
                new ConcurrencyOptions { Partition = WorkPartition.ClaimOne });

            Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
            Assert.Equal("i0", result.FirstFailure!.Message);
        }
    }

    [Fact]
    public void A_collected_run_reports_its_failures_in_index_order_not_completion_order()
    {
        // MAJOR 3 regression for the collect-all arm: every index fails on a
        // different worker at a different time, so completion order is
        // scheduling-dependent. The reported list and its head must be in index
        // order, whatever the completion order was.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        for (var run = 0; run < 25; run++)
        {
            var result = context.For(
                0,
                100,
                1,
                index => throw new InvalidOperationException($"i{index}"),
                new ConcurrencyOptions { Partition = WorkPartition.ClaimOne, CollectAllFailures = true });

            Assert.Equal(ConcurrencyStatus.Faulted, result.Status);
            Assert.Equal(100, result.Failures.Count);

            var indices = result.Failures
                .Select(failure => int.Parse(failure.Message.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();

            Assert.Equal(Enumerable.Range(0, 100), indices);
            Assert.Equal("i0", result.FirstFailure!.Message);
        }
    }

    [Fact]
    public void A_collect_all_run_records_every_failure()
    {
        const int count = 64;
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var result = context.For(
            0,
            count,
            1,
            index => throw new InvalidOperationException($"fail {index}"),
            new ConcurrencyOptions { Partition = WorkPartition.ClaimOne, CollectAllFailures = true });

        Assert.Equal(ConcurrencyStatus.Faulted, result.Status);
        Assert.Equal(count, result.Failures.Count);
    }

    [Fact]
    public void The_state_overload_passes_the_same_value_to_every_index()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var indexSource = new int[32];
        var sum = 0;

        var result = context.For(0, indexSource.Length, 1, indexSource, (index, state) => Interlocked.Add(ref sum, state[index] + 1));

        Assert.True(result.IsCompleted);
        Assert.Equal(indexSource.Length, sum);
    }

    [Fact]
    public void The_per_call_bound_caps_the_workers_that_take_part()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var observed = new System.Collections.Concurrent.ConcurrentDictionary<long, byte>();

        var result = context.For(
            0,
            100,
            1,
            _ => observed.TryAdd(context.WorkerIndex, 0),
            new ConcurrencyOptions { Partition = WorkPartition.ClaimOne, WorkerCount = 2 });

        Assert.True(result.IsCompleted);
        Assert.True(observed.Count <= 2, $"used {observed.Count} workers");
    }

    [Theory]
    [InlineData(WorkPartition.StaticChunks)]
    [InlineData(WorkPartition.ClaimOne)]
    [InlineData(WorkPartition.ClaimChunk)]
    public void A_range_near_long_MaxValue_runs_every_index_without_overflowing(WorkPartition partition)
    {
        // MAJOR 4 regression: `start + cursor*step` and `index += step` overflow a
        // long for a range near the type's edge, so the slice loop wrapped negative
        // and either ran nothing or ran forever. The count and the per-index
        // arithmetic are computed in Int128 now.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var seen = new System.Collections.Concurrent.ConcurrentBag<long>();
        var result = context.For(long.MaxValue - 5, long.MaxValue, 10, seen.Add, new ConcurrencyOptions { Partition = partition });

        Assert.True(result.IsCompleted);
        Assert.Equal([long.MaxValue - 5], seen.Order().ToArray());

        var strideTwo = new System.Collections.Concurrent.ConcurrentBag<long>();
        var result2 = context.For(long.MaxValue - 6, long.MaxValue, 2, strideTwo.Add, new ConcurrencyOptions { Partition = partition });

        Assert.True(result2.IsCompleted);
        Assert.Equal([long.MaxValue - 6, long.MaxValue - 4, long.MaxValue - 2], strideTwo.Order().ToArray());
    }

    [Theory]
    [InlineData(WorkPartition.StaticChunks)]
    [InlineData(WorkPartition.ClaimOne)]
    [InlineData(WorkPartition.ClaimChunk)]
    public void A_span_wider_than_a_long_counts_without_overflowing(WorkPartition partition)
    {
        // end - start does not fit a long for a range as wide as the type. The
        // count is ceil(span / step) computed in Int128, so these return the three
        // indices they contain instead of dropping them.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var seen = new System.Collections.Concurrent.ConcurrentBag<long>();
        var result = context.For(long.MinValue, long.MaxValue, long.MaxValue, seen.Add, new ConcurrencyOptions { Partition = partition });

        Assert.True(result.IsCompleted);
        Assert.Equal([long.MinValue, -1L, long.MaxValue - 1], seen.Order().ToArray());

        var halves = new System.Collections.Concurrent.ConcurrentBag<long>();
        var result2 = context.For(0, long.MaxValue, long.MaxValue / 2, halves.Add, new ConcurrencyOptions { Partition = partition });

        Assert.True(result2.IsCompleted);
        Assert.Equal([0L, long.MaxValue / 2, long.MaxValue - 1], halves.Order().ToArray());
    }

    [Theory]
    [InlineData(WorkPartition.StaticChunks)]
    [InlineData(WorkPartition.ClaimOne)]
    [InlineData(WorkPartition.ClaimChunk)]
    public void A_degenerate_range_has_a_defined_empty_result(WorkPartition partition)
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        var ran = 0;

        // end < start and end == start are empty; a step larger than the span runs
        // only the first index.
        Assert.True(context.For(10, 5, 1, _ => Interlocked.Increment(ref ran), new ConcurrencyOptions { Partition = partition }).IsCompleted);
        Assert.True(context.For(5, 5, 1, _ => Interlocked.Increment(ref ran), new ConcurrencyOptions { Partition = partition }).IsCompleted);
        Assert.Equal(0, ran);

        var single = new System.Collections.Concurrent.ConcurrentBag<long>();
        Assert.True(context.For(0, 3, 100, single.Add, new ConcurrencyOptions { Partition = partition }).IsCompleted);
        Assert.Equal([0L], single.Order().ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(257)]
    [InlineData(int.MaxValue)]
    public void A_per_call_bound_outside_one_to_256_is_rejected_never_clamped(int bound)
    {
        // MAJOR 5 regression: the per-call `workers` bound is 1..256 and rejects
        // out of range instead of silently clamping; a bound of int.MaxValue once
        // rented a worker per item.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        Assert.Throws<ArgumentOutOfRangeException>(
            () => context.For(0, 4, 1, _ => { }, new ConcurrencyOptions { WorkerCount = bound }));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => context.Run(() => { }, new ConcurrencyOptions { WorkerCount = bound }));

        // The rejected call never grew the pool.
        Assert.Equal(2, context.WorkerCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(256)]
    public void A_per_call_bound_at_the_edges_is_honoured(int bound)
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        // Few items, so a large bound rents only as many workers as there are
        // items: the edge is accepted, not tested for its thread cost.
        var result = context.For(0, 3, 1, _ => { }, new ConcurrencyOptions { WorkerCount = bound, Partition = WorkPartition.ClaimOne });

        Assert.True(result.IsCompleted);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void A_non_positive_step_is_a_programmer_error(long step)
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        Assert.Throws<ArgumentOutOfRangeException>(() => context.For(0, 10, step, _ => { }));
    }

    [Fact]
    public void A_null_body_is_a_programmer_error()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        Assert.Throws<ArgumentNullException>(() => context.For(0, 10, 1, (Action<long>)null!));
    }
}
