// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Concurrency.Tests;

/// <summary>
/// Guards the resource contract: workers are created once and reused, so many
/// calls neither grow the process's thread count nor leak a thread on dispose.
/// </summary>
public sealed class WorkerLifecycleTests
{
    [Fact]
    public void Create_reserves_exactly_the_resolved_worker_count()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 5 });

        Assert.Equal(5, context.WorkerCount);
    }

    [Fact]
    public void Many_calls_do_not_grow_the_pool_or_its_threads()
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        // Warm up so the pool is at its steady state before the baseline.
        context.For(0, 64, 1, _ => { });
        context.Run(() => { });

        var poolBefore = context.WorkerCount;
        var threadsBefore = context.WorkerThreads.Count;

        for (var call = 0; call < 500; call++)
        {
            context.For(0, 64, 1, _ => { });
            context.Run(() => { });
        }

        // The pool neither grows nor shrinks: 500 top-level calls create no worker.
        Assert.Equal(poolBefore, context.WorkerCount);
        Assert.Equal(threadsBefore, context.WorkerThreads.Count);
        Assert.All(context.WorkerThreads, thread => Assert.True(thread.IsAlive));
    }

    [Fact]
    public void Dispose_joins_every_worker()
    {
        var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 6 });

        var threads = context.WorkerThreads;
        Assert.Equal(6, threads.Count);
        context.Run(() => { });
        Assert.All(threads, thread => Assert.True(thread.IsAlive));

        context.Dispose();

        // The join is deterministic: every worker thread has ended.
        Assert.All(threads, thread => Assert.False(thread.IsAlive));
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });

        context.Dispose();
        context.Dispose();
    }

    [Fact]
    public void A_call_after_dispose_is_a_programmer_error()
    {
        var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2 });
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.For(0, 10, 1, _ => { }));
        Assert.Throws<ObjectDisposedException>(() => context.Run(() => { }));
        Assert.Throws<ObjectDisposedException>(() => context.Submit(() => { }));
        Assert.Throws<ObjectDisposedException>(() => context.DispatchAndWait());
        Assert.Throws<ObjectDisposedException>(() => context.SubmitBatch([]));
        Assert.Throws<ObjectDisposedException>(() => context.RegisterSlot(_ => 1));
    }

    [Fact]
    public void A_context_with_no_config_uses_the_resolved_count()
    {
        using var context = ConcurrencyContext.Create();

        Assert.Equal(ThreadCount.Resolve(ConcurrencyConfiguration.Default), context.WorkerCount);
    }
}
