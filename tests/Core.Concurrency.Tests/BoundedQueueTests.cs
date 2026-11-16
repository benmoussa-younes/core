// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Collections.Concurrent;

namespace Core.Concurrency.Tests;

/// <summary>
/// Guards <see cref="BoundedQueue{T}"/>: the bound applies back-pressure, a close
/// drains before it reports empty, and concurrent producers and consumers lose
/// and duplicate nothing.
/// </summary>
/// <remarks>
/// The blocking tests run their producer or consumer on a task and prove it is
/// <em>still</em> blocked with a bounded wait. Every wait is bounded, so a
/// regressed queue fails the test instead of hanging the suite.
/// </remarks>
public sealed class BoundedQueueTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Push_then_pop_returns_the_items_in_order()
    {
        using var queue = new BoundedQueue<int>(4);

        Assert.True(queue.Push(1));
        Assert.True(queue.Push(2));
        Assert.True(queue.Push(3));

        Assert.Equal(3, queue.Count);

        Assert.True(queue.Pop(out var first));
        Assert.True(queue.Pop(out var second));
        Assert.True(queue.Pop(out var third));

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(3, third);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task A_full_queue_blocks_the_producer_until_a_consumer_frees_a_slot()
    {
        using var queue = new BoundedQueue<int>(1);
        Assert.True(queue.Push(1));

        // The producer blocks in Push(2) because the bound is one and it is full.
        var producer = Task.Run(() => queue.Push(2), Token);

        // The producer must stay blocked while the queue is full.
        await Task.Delay(50, Token);
        Assert.False(producer.IsCompleted);

        Assert.True(queue.Pop(out var first));
        Assert.Equal(1, first);

        Assert.True(await producer.WaitAsync(TimeSpan.FromSeconds(10), Token));
    }

    [Fact]
    public void Close_drains_the_queue_then_returns_false()
    {
        using var queue = new BoundedQueue<int>(4);
        Assert.True(queue.Push(1));
        Assert.True(queue.Push(2));

        queue.Close();

        Assert.True(queue.IsClosed);

        Assert.True(queue.Pop(out var first));
        Assert.True(queue.Pop(out var second));
        Assert.Equal(1, first);
        Assert.Equal(2, second);

        // Drained and closed: Pop now returns false instead of blocking.
        Assert.False(queue.Pop(out _));
    }

    [Fact]
    public void A_push_after_close_fails_instead_of_blocking()
    {
        using var queue = new BoundedQueue<int>(1);
        queue.Close();

        Assert.False(queue.Push(1));
        Assert.True(queue.IsClosed);
    }

    [Fact]
    public async Task A_push_blocked_on_a_full_queue_returns_false_when_it_is_closed()
    {
        using var queue = new BoundedQueue<int>(1);
        Assert.True(queue.Push(1));

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var producer = Task.Run(
            () =>
            {
                started.SetResult();
                return queue.Push(2);
            },
            Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await Task.Delay(100, Token);
        Assert.False(producer.IsCompleted);

        queue.Close();

        // The blocked producer is woken and refused, not left waiting.
        await producer.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.False(await producer);
    }

    [Fact]
    public async Task Closing_wakes_a_blocked_consumer()
    {
        using var queue = new BoundedQueue<int>(1);

        var consumer = Task.Run(() => queue.Pop(out _), Token);
        await Task.Delay(50, Token);
        Assert.False(consumer.IsCompleted);

        queue.Close();

        await consumer.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.False(await consumer);
    }

    [Fact]
    public async Task Concurrent_producers_and_consumers_lose_and_duplicate_nothing()
    {
        const int producers = 4;
        const int perProducer = 5_000;
        const int total = producers * perProducer;

        using var queue = new BoundedQueue<int>(64);
        var seen = new ConcurrentDictionary<int, byte>();

        var consumerTasks = new Task[4];
        for (var index = 0; index < consumerTasks.Length; index++)
        {
            consumerTasks[index] = Task.Run(
                () =>
                {
                    while (queue.Pop(out var item))
                    {
                        // TryAdd fails on a duplicate, which the assertion below sees.
                        seen.TryAdd(item, 0);
                    }
                },
                Token);
        }

        var producerTasks = new Task[producers];
        for (var producer = 0; producer < producers; producer++)
        {
            var start = producer * perProducer;
            producerTasks[producer] = Task.Run(
                () =>
                {
                    for (var offset = 0; offset < perProducer; offset++)
                    {
                        queue.Push(start + offset);
                    }
                },
                Token);
        }

        await Task.WhenAll(producerTasks);
        queue.Close();
        await Task.WhenAll(consumerTasks);

        Assert.Equal(total, seen.Count);
    }

    [Fact]
    public async Task An_empty_queue_blocks_a_consumer_until_an_item_arrives()
    {
        using var queue = new BoundedQueue<int>(4);

        var consumer = Task.Run(() => queue.Pop(out var item) ? item : -1, Token);
        await Task.Delay(50, Token);
        Assert.False(consumer.IsCompleted);

        Assert.True(queue.Push(42));

        Assert.Equal(42, await consumer.WaitAsync(TimeSpan.FromSeconds(10), Token));
    }

    [Fact]
    public async Task Dispose_closes_the_queue_and_wakes_the_waiters()
    {
        var queue = new BoundedQueue<int>(1);
        Assert.True(queue.Push(1));

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var producer = Task.Run(
            () =>
            {
                started.SetResult();
                return queue.Push(2);
            },
            Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await Task.Delay(100, Token);
        Assert.False(producer.IsCompleted);

        queue.Dispose();

        await producer.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.False(await producer);
    }

    [Fact]
    public void A_non_positive_capacity_is_a_programmer_error()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedQueue<int>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedQueue<int>(-1));
    }

    [Fact]
    public void A_capacity_past_the_supported_maximum_is_a_programmer_error_not_an_oom()
    {
        // MINOR 9 regression: a capacity the backing array cannot hold used to be
        // accepted and then threw OutOfMemoryException on first use. It is a typed
        // argument error now.
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedQueue<int>(int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedQueue<int>(BoundedQueue<int>.MaximumCapacity + 1));

        // The documented bound is the largest the backing store supports; a large
        // but supported capacity still constructs.
        using var large = new BoundedQueue<int>(8 * 1024 * 1024);
        Assert.Equal(8 * 1024 * 1024, large.Capacity);
    }

    [Fact]
    public void Push_and_pop_after_dispose_are_programmer_errors()
    {
        var queue = new BoundedQueue<int>(1);
        queue.Dispose();

        Assert.Throws<ObjectDisposedException>(() => queue.Push(1));
        Assert.Throws<ObjectDisposedException>(() => queue.Pop(out _));
    }

    [Fact]
    public void Capacity_reports_the_bound_it_was_created_with()
    {
        using var queue = new BoundedQueue<int>(7);

        Assert.Equal(7, queue.Capacity);
    }

    [Fact]
    public void Closing_twice_is_idempotent()
    {
        using var queue = new BoundedQueue<int>(1);
        Assert.True(queue.Push(1));

        queue.Close();
        queue.Close();

        // The second close returns at the guard; the already-queued item still
        // drains before Pop reports empty.
        Assert.True(queue.IsClosed);
        Assert.True(queue.Pop(out var item));
        Assert.Equal(1, item);
        Assert.False(queue.Pop(out _));
    }
}
