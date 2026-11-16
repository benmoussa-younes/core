// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.CompilerServices;
using Core.IO.Pipeline;

namespace Core.Pipeline.Tests;

public sealed class BlockPipelineTests
{
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(30);

    [Fact]
    public void Default_options_are_the_documented_values()
    {
        var options = BlockPipelineOptions.Default;

        Assert.Equal(32, options.InitialBlockSize);
        Assert.Equal(512, options.MaximumBlockSize);
        Assert.Equal(16, options.InFlightBlocksPerThread);
        Assert.Equal(1024, BlockPipelineOptions.MaximumThreads);
    }

    [Fact]
    public void The_options_struct_keeps_its_three_int_layout()
    {
        Assert.Equal(12, Unsafe.SizeOf<BlockPipelineOptions>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(16)]
    public async Task Emits_every_item_in_source_order_for_any_thread_count(int threads)
    {
        var source = Enumerable.Range(0, 5000).ToArray();
        var emitted = new List<int>(source.Length);

        await BlockPipeline.RunAsync<int, int, WorkerState>(
            source,
            threads,
            Options(16, 128, 4),
            static () => new WorkerState(),
            null,
            static (item, _) => item * 2,
            emitted.Add,
            CancellationToken.None);

        Assert.Equal(source.Length, emitted.Count);
        for (var index = 0; index < emitted.Count; index++)
        {
            Assert.Equal(index * 2, emitted[index]);
        }
    }

    [Fact]
    public async Task Emits_a_partial_final_block_exactly_once()
    {
        var emitted = new List<int>();

        await RunIntsAsync(Enumerable.Range(0, 10).ToArray(), 3, Options(4, 4, 4), emitted.Add);

        Assert.Equal(Enumerable.Range(0, 10), emitted);
    }

    [Fact]
    public async Task Grows_the_block_size_up_to_the_maximum()
    {
        var emitted = new List<int>();

        await RunIntsAsync(Enumerable.Range(0, 20).ToArray(), 1, Options(3, 4, 2), emitted.Add);

        Assert.Equal(Enumerable.Range(0, 20), emitted);
    }

    [Fact]
    public async Task Completes_without_emitting_for_an_empty_source()
    {
        var emitted = new List<int>();

        await RunIntsAsync([], 4, Options(), emitted.Add);

        Assert.Empty(emitted);
    }

    [Fact]
    public async Task Emits_a_large_single_item_unchanged()
    {
        var payload = new string('x', 1 << 20);
        var emitted = new List<string>();

        await BlockPipeline.RunAsync<string, string, WorkerState>(
            [payload],
            4,
            Options(8, 32, 2),
            static () => new WorkerState(),
            null,
            static (item, _) => item,
            emitted.Add,
            CancellationToken.None);

        var single = Assert.Single(emitted);
        Assert.Same(payload, single);
    }

    [Fact]
    public async Task Creates_and_disposes_exactly_one_state_per_thread()
    {
        var created = 0;
        var disposed = 0;

        await BlockPipeline.RunAsync<int, int, WorkerState>(
            Enumerable.Range(0, 200).ToArray(),
            7,
            Options(),
            () =>
            {
                Interlocked.Increment(ref created);
                return new WorkerState();
            },
            _ => Interlocked.Increment(ref disposed),
            static (item, _) => item,
            static _ => { },
            CancellationToken.None);

        Assert.Equal(7, created);
        Assert.Equal(7, disposed);
    }

    [Fact]
    public async Task A_throwing_dispose_fails_a_successful_run_and_disposes_every_state()
    {
        var calls = 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BlockPipeline.RunAsync<int, int, WorkerState>(
                [1, 2, 3],
                3,
                Options(),
                static () => new WorkerState(),
                _ =>
                {
                    calls++;
                    throw new InvalidOperationException("dispose");
                },
                static (item, _) => item,
                static _ => { },
                CancellationToken.None));

        Assert.Equal("dispose", exception.Message);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task A_throwing_dispose_does_not_mask_the_original_failure()
    {
        var calls = 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BlockPipeline.RunAsync<int, int, WorkerState>(
                [1, 2, 3],
                3,
                Options(),
                static () => new WorkerState(),
                _ =>
                {
                    calls++;
                    throw new InvalidOperationException("dispose");
                },
                (_, _) => throw new InvalidOperationException("process"),
                static _ => { },
                CancellationToken.None));

        Assert.Equal("process", exception.Message);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Disposes_the_states_already_created_when_the_factory_throws()
    {
        var calls = 0;
        var disposed = 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BlockPipeline.RunAsync<int, int, WorkerState>(
                [1],
                3,
                Options(),
                () =>
                {
                    calls++;
                    return calls == 3
                        ? throw new InvalidOperationException("factory")
                        : new WorkerState();
                },
                _ => disposed++,
                static (item, _) => item,
                static _ => { },
                CancellationToken.None));

        Assert.Equal("factory", exception.Message);
        Assert.Equal(3, calls);
        Assert.Equal(2, disposed);
    }

    [Fact]
    public async Task Reorders_blocks_that_complete_out_of_order()
    {
        using var itemTwoDone = new ManualResetEventSlim();
        var emitted = new List<int>(3);

        await BlockPipeline.RunAsync<int, int, WorkerState>(
            [0, 1, 2],
            2,
            Options(1, 1, 8),
            static () => new WorkerState(),
            null,
            (item, _) =>
            {
                if (item == 0 && !itemTwoDone.Wait(WaitBudget))
                {
                    throw new TimeoutException("item 0 was never released.");
                }

                if (item == 2)
                {
                    itemTwoDone.Set();
                }

                return item;
            },
            emitted.Add,
            CancellationToken.None);

        Assert.Equal([0, 1, 2], emitted);
    }

    [Fact]
    public async Task Keeps_the_in_flight_window_bounded()
    {
        const int Threads = 4;
        const int InFlight = 2;
        var tracker = new WindowTracker();
        var source = Enumerable.Range(0, 5000).ToArray();
        var produced = 0;
        var emitted = 0;

        await BlockPipeline.RunAsync<int, int, WorkerState>(
            source,
            Threads,
            Options(1, 1, InFlight),
            static () => new WorkerState(),
            null,
            (item, _) =>
            {
                var inFlight = Interlocked.Increment(ref produced) - Volatile.Read(ref emitted);
                tracker.Observe(inFlight);
                return item;
            },
            _ =>
            {
                Interlocked.Increment(ref emitted);
            },
            CancellationToken.None);

        Assert.True(tracker.Maximum <= Threads * InFlight, $"observed {tracker.Maximum} in-flight blocks.");
        Assert.Equal(source.Length, Volatile.Read(ref emitted));
    }

    [Fact(Timeout = 60000)]
    public async Task Applies_back_pressure_without_deadlocking()
    {
        var source = Enumerable.Range(0, 20_000).ToArray();
        var emitted = new List<int>(source.Length);

        await BlockPipeline.RunAsync<int, int, WorkerState>(
            source,
            4,
            Options(8, 32, 1),
            static () => new WorkerState(),
            null,
            static (item, _) =>
            {
                if ((item & 0xFF) == 0)
                {
                    Thread.SpinWait(4096);
                }

                return item;
            },
            emitted.Add,
            TestContext.Current.CancellationToken);

        Assert.Equal(source, emitted);
    }

    [Fact]
    public async Task Surfaces_a_throwing_source()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BlockPipeline.RunAsync<int, int, WorkerState>(
                ThrowingSource(),
                2,
                Options(4, 4, 2),
                static () => new WorkerState(),
                null,
                static (item, _) => item,
                static _ => { },
                CancellationToken.None));

        Assert.Equal("source", exception.Message);
    }

    [Fact]
    public async Task Surfaces_a_throwing_processor()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BlockPipeline.RunAsync<int, int, WorkerState>(
                Enumerable.Range(0, 64).ToArray(),
                4,
                Options(4, 8, 2),
                static () => new WorkerState(),
                null,
                static (item, _) => item == 3 ? throw new InvalidOperationException("process") : item,
                static _ => { },
                CancellationToken.None));

        Assert.Equal("process", exception.Message);
    }

    [Fact]
    public async Task Surfaces_a_processor_operation_canceled_exception()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BlockPipeline.RunAsync<int, int, WorkerState>(
                Enumerable.Range(0, 64).ToArray(),
                4,
                Options(4, 8, 2),
                static () => new WorkerState(),
                null,
                static (item, _) => item == 3 ? throw new OperationCanceledException("process") : item,
                static _ => { },
                CancellationToken.None));
    }

    [Fact]
    public async Task Surfaces_a_throwing_emit()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BlockPipeline.RunAsync<int, int, WorkerState>(
                Enumerable.Range(0, 64).ToArray(),
                4,
                Options(4, 8, 2),
                static () => new WorkerState(),
                null,
                static (item, _) => item,
                static item =>
                {
                    if (item == 2)
                    {
                        throw new InvalidOperationException("emit");
                    }
                },
                CancellationToken.None));

        Assert.Equal("emit", exception.Message);
    }

    [Fact(Timeout = 60000)]
    public async Task Surfaces_a_throwing_emit_while_blocks_are_pending()
    {
        using var itemTwoDone = new ManualResetEventSlim();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BlockPipeline.RunAsync<int, int, WorkerState>(
                [0, 1, 2],
                2,
                Options(1, 1, 8),
                static () => new WorkerState(),
                null,
                (item, _) =>
                {
                    if (item == 0 && !itemTwoDone.Wait(WaitBudget))
                    {
                        throw new TimeoutException("item 0 was never released.");
                    }

                    if (item == 2)
                    {
                        itemTwoDone.Set();
                    }

                    return item;
                },
                static value =>
                {
                    if (value == 0)
                    {
                        throw new InvalidOperationException("emit");
                    }
                },
                TestContext.Current.CancellationToken));

        Assert.Equal("emit", exception.Message);
    }

    [Fact(Timeout = 60000)]
    public async Task Surfaces_the_process_exception_when_it_cancels_then_throws()
    {
        for (var iteration = 0; iteration < 50; iteration++)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BlockPipeline.RunAsync<int, int, WorkerState>(
                    Enumerable.Range(0, 64).ToArray(),
                    4,
                    Options(1, 4, 4),
                    static () => new WorkerState(),
                    null,
                    (item, _) =>
                    {
                        if (item == 7)
                        {
                            cancellation.Cancel();
                            throw new InvalidOperationException("process");
                        }

                        return item;
                    },
                    static _ => { },
                    cancellation.Token));

            Assert.Equal("process", exception.Message);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task Surfaces_the_emit_exception_when_it_cancels_then_throws()
    {
        for (var iteration = 0; iteration < 50; iteration++)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BlockPipeline.RunAsync<int, int, WorkerState>(
                    Enumerable.Range(0, 64).ToArray(),
                    4,
                    Options(4, 8, 2),
                    static () => new WorkerState(),
                    null,
                    static (item, _) => item,
                    item =>
                    {
                        if (item == 2)
                        {
                            cancellation.Cancel();
                            throw new InvalidOperationException("emit");
                        }
                    },
                    cancellation.Token));

            Assert.Equal("emit", exception.Message);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task Surfaces_the_source_exception_when_it_cancels_then_throws()
    {
        for (var iteration = 0; iteration < 50; iteration++)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BlockPipeline.RunAsync<int, int, WorkerState>(
                    CancellingThenThrowingSource(cancellation),
                    4,
                    Options(1, 4, 2),
                    static () => new WorkerState(),
                    null,
                    static (item, _) => item,
                    static _ => { },
                    cancellation.Token));

            Assert.Equal("source", exception.Message);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task Cancellation_with_no_failure_still_throws_operation_canceled()
    {
        for (var iteration = 0; iteration < 50; iteration++)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                BlockPipeline.RunAsync<int, int, WorkerState>(
                    Enumerable.Range(0, 1024).ToArray(),
                    4,
                    Options(1, 4, 2),
                    static () => new WorkerState(),
                    null,
                    (item, _) =>
                    {
                        if (item == 3)
                        {
                            cancellation.Cancel();
                        }

                        return item;
                    },
                    static _ => { },
                    cancellation.Token));
        }
    }

    [Fact(Timeout = 60000)]
    public async Task Cancels_before_the_first_item()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BlockPipeline.RunAsync<int, int, WorkerState>(
                Enumerable.Range(0, 1000).ToArray(),
                4,
                Options(),
                static () => new WorkerState(),
                null,
                static (item, _) => item,
                static _ => { },
                cancellation.Token));
    }

    [Fact(Timeout = 60000)]
    public async Task Cancels_while_the_producer_is_blocked()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using var consumerEntered = new ManualResetEventSlim();
        using var releaseConsumer = new ManualResetEventSlim();
        var emitted = 0;

        var run = BlockPipeline.RunAsync<int, int, WorkerState>(
            Enumerable.Range(0, 1_000_000),
            1,
            Options(1, 1, 1),
            static () => new WorkerState(),
            null,
            static (item, _) => item,
            _ =>
            {
                if (Interlocked.Increment(ref emitted) == 1)
                {
                    consumerEntered.Set();
                    releaseConsumer.Wait(WaitBudget);
                }
            },
            cancellation.Token);

        Assert.True(consumerEntered.Wait(WaitBudget, TestContext.Current.CancellationToken));

        cancellation.Cancel();
        releaseConsumer.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact(Timeout = 60000)]
    public async Task Cancels_while_the_consumer_is_busy()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using var consumerEntered = new ManualResetEventSlim();
        using var releaseConsumer = new ManualResetEventSlim();
        var emitted = 0;

        var run = BlockPipeline.RunAsync<int, int, WorkerState>(
            Enumerable.Range(0, 1_000_000),
            4,
            Options(4, 16, 2),
            static () => new WorkerState(),
            null,
            static (item, _) => item,
            _ =>
            {
                if (Interlocked.Increment(ref emitted) == 1)
                {
                    consumerEntered.Set();
                    releaseConsumer.Wait(WaitBudget);
                }
            },
            cancellation.Token);

        Assert.True(consumerEntered.Wait(WaitBudget, TestContext.Current.CancellationToken));

        cancellation.Cancel();
        releaseConsumer.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task Clears_reference_arrays_before_pooling_them()
    {
        var references = new List<WeakReference>(4096);

        await BlockPipeline.RunAsync<Payload, Payload, WorkerState>(
            GeneratePayloads(references, 4096),
            4,
            Options(16, 64, 4),
            static () => new WorkerState(),
            null,
            static (item, _) => item,
            static _ => { },
            CancellationToken.None);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.All(references, reference => Assert.False(reference.IsAlive));
    }

    [Fact]
    public async Task Rejects_null_arguments()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BlockPipeline.RunAsync<int, int, WorkerState>(
                null!,
                1,
                Options(),
                static () => new WorkerState(),
                null,
                static (item, _) => item,
                static _ => { },
                CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BlockPipeline.RunAsync<int, int, WorkerState>(
                [1],
                1,
                Options(),
                null!,
                null,
                static (item, _) => item,
                static _ => { },
                CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BlockPipeline.RunAsync<int, int, WorkerState>(
                [1],
                1,
                Options(),
                static () => new WorkerState(),
                null,
                null!,
                static _ => { },
                CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BlockPipeline.RunAsync<int, int, WorkerState>(
                [1],
                1,
                Options(),
                static () => new WorkerState(),
                null,
                static (item, _) => item,
                null!,
                CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1025)]
    public async Task Rejects_out_of_range_thread_counts(int threads)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            BlockPipeline.RunAsync<int, int, WorkerState>(
                [1],
                threads,
                Options(),
                static () => new WorkerState(),
                null,
                static (item, _) => item,
                static _ => { },
                CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 512, 16)]
    [InlineData(64, 32, 16)]
    [InlineData(32, 512, 0)]
    public async Task Rejects_invalid_options(int initialBlockSize, int maximumBlockSize, int inFlightBlocksPerThread)
    {
        var created = 0;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            BlockPipeline.RunAsync<int, int, WorkerState>(
                [1],
                2,
                Options(initialBlockSize, maximumBlockSize, inFlightBlocksPerThread),
                () =>
                {
                    created++;
                    return new WorkerState();
                },
                null,
                static (item, _) => item,
                static _ => { },
                CancellationToken.None));

        Assert.Equal(0, created);
    }

    [Fact]
    public async Task Rejects_an_in_flight_bound_that_overflows()
    {
        var created = 0;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            BlockPipeline.RunAsync<int, int, WorkerState>(
                [1],
                BlockPipelineOptions.MaximumThreads,
                Options(1, 1, 3_000_000),
                () =>
                {
                    created++;
                    return new WorkerState();
                },
                null,
                static (item, _) => item,
                static _ => { },
                CancellationToken.None));

        Assert.Equal(0, created);
    }

    private static Task RunIntsAsync(
        IReadOnlyList<int> source,
        int threads,
        BlockPipelineOptions options,
        Action<int> emit)
    {
        return BlockPipeline.RunAsync<int, int, WorkerState>(
            source,
            threads,
            options,
            static () => new WorkerState(),
            null,
            static (item, _) => item,
            emit,
            CancellationToken.None);
    }

    private static IEnumerable<int> ThrowingSource()
    {
        yield return 1;
        throw new InvalidOperationException("source");
    }

    private static IEnumerable<int> CancellingThenThrowingSource(CancellationTokenSource cancellation)
    {
        yield return 1;
        cancellation.Cancel();
        throw new InvalidOperationException("source");
    }

    private static IEnumerable<Payload> GeneratePayloads(List<WeakReference> references, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var payload = new Payload(index);
            references.Add(new WeakReference(payload));
            yield return payload;
        }

        // The compiler-generated iterator keeps the last yielded value in its
        // current field, so the final tracked payload would stay alive as a false
        // positive. This untracked trailing item takes that slot, leaving every
        // tracked payload reachable only through a pooled array if one were not
        // cleared.
        yield return new Payload(count);
    }

    private static BlockPipelineOptions Options(int initialBlockSize = 32, int maximumBlockSize = 512, int inFlightBlocksPerThread = 16)
    {
        return new BlockPipelineOptions
        {
            InitialBlockSize = initialBlockSize,
            MaximumBlockSize = maximumBlockSize,
            InFlightBlocksPerThread = inFlightBlocksPerThread,
        };
    }

    private sealed class WorkerState;

    private sealed class Payload(int value)
    {
        public int Value { get; } = value;
    }

    private sealed class WindowTracker
    {
        private int _maximum;

        public int Maximum => Volatile.Read(ref _maximum);

        public void Observe(int inFlight)
        {
            var current = Volatile.Read(ref _maximum);
            while (inFlight > current)
            {
                var observed = Interlocked.CompareExchange(ref _maximum, inFlight, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }
}
