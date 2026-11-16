// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Concurrency.Tests;

/// <summary>
/// R8 (bounded dispose, owner decision 2026-10-18): a dispose never waits forever for
/// a body that does not return. The worker threads are background threads, so an
/// abandoned worker cannot keep the process alive.
/// </summary>
public sealed class BoundedDisposeTests
{
    private static readonly TimeSpan Budget = WorkerPool.DisposeBudget;

    [Fact]
    public void Disposing_a_pool_whose_worker_blocks_returns_within_the_budget()
    {
        var token = TestContext.Current.CancellationToken;
        var pool = new WorkerPool(initialCount: 1, stackBytes: 256 * 1024);
        var worker = pool.Rent(1).Single();

        // Hand the worker a wave whose body blocks until the test releases it: the
        // worker is mid-body when the pool is disposed.
        using var gate = new ManualResetEventSlim(false);
        using var started = new ManualResetEventSlim(false);
        using var failures = new FailureRecord(collectAll: false);
        worker.Dispatch(new Wave.Tasks([() =>
        {
            started.Set();
            gate.Wait(token);
        }], 0, 1, failures));

        Assert.True(started.Wait(TimeSpan.FromSeconds(10), token), "the body never started");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        pool.Dispose();
        stopwatch.Stop();

        // Bounded by the pool budget (plus the drain wait that precedes the joins),
        // never by the blocked body, which would wait forever.
        Assert.True(
            stopwatch.Elapsed <= (2 * Budget) + TimeSpan.FromSeconds(2),
            $"Dispose took {stopwatch.Elapsed}, beyond the budget");

        // Release the body; the abandoned worker leaves on its own.
        gate.Set();
        Assert.True(worker.Thread.Join(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void A_bounded_worker_dispose_joins_a_stopped_worker()
    {
        var worker = new Worker(index: 0, stackBytes: 256 * 1024);
        worker.Start();

        Assert.True(worker.Dispose(TimeSpan.FromSeconds(1)));
        Assert.False(worker.Thread.IsAlive);
    }

    [Fact]
    public void A_normal_dispose_stays_immediate()
    {
        var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 4 });

        context.For(0, 100, 1, _ => { });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        context.Dispose();
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Dispose took {stopwatch.Elapsed}");
    }
}
