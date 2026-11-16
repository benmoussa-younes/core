// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.CompilerServices;

namespace Core.Concurrency.Tests;

/// <summary>
/// Guards <see cref="ConcurrencyConfiguration.WorkerStackBytes"/>: a worker runs on the
/// stack the caller chose, so a deep recursion that does not fit a small stack
/// fits a large one.
/// </summary>
/// <remarks>
/// The recursion asks <see cref="RuntimeHelpers.EnsureSufficientExecutionStack"/>
/// for permission before every frame, so it throws the catchable
/// <see cref="InsufficientExecutionStackException"/> while there is still stack
/// left instead of overrunning the worker and killing the process. That is what
/// makes the depth a measurable, safe proxy for the usable stack: a real
/// overflow is a native crash, which a test must never provoke.
/// </remarks>
public sealed class WorkerStackTests
{
    /// <summary>The frame size each probe invocation consumes (1 KiB).</summary>
    private const int FrameBytes = 1024;

    /// <summary>
    /// A worker created with the documented default stack reaches a depth a small
    /// stack cannot: the default is 32 MiB, sized so the bcs interpreter's
    /// ≈ 2.3 KiB frame reaches the documented depth-10000 contract with margin.
    /// </summary>
    [Fact]
    public void A_large_worker_stack_survives_a_deep_recursion_a_small_one_cannot()
    {
        const int depth = 4_000;

        var small = DeepestRecursionOnAWorker(256 * 1024, depth);
        var large = DeepestRecursionOnAWorker(ConcurrencyConfiguration.DefaultWorkerStackBytes, depth);

        // The small-stack worker cannot reach the depth and reports it by throwing
        // before the real stack end; the default-stack worker reaches it.
        Assert.True(small < depth, $"a {256} KiB stack reached {small}");
        Assert.Equal(depth, large);
    }

    /// <summary>
    /// The default stack is the library's own choice and reaches well past the
    /// measured minimum: a 1 MiB stack cannot reach 4 000 × 1 KiB frames, the
    /// 32 MiB default can.
    /// </summary>
    [Fact]
    public void The_default_stack_is_not_the_old_one_mebibyte_default()
    {
        Assert.Equal(32 * 1024 * 1024, ConcurrencyConfiguration.DefaultWorkerStackBytes);

        const int depth = 4_000;
        Assert.Equal(depth, DeepestRecursionOnAWorker(ConcurrencyConfiguration.DefaultWorkerStackBytes, depth));
        Assert.True(DeepestRecursionOnAWorker(1024 * 1024, depth) < depth);
    }

    [Fact]
    public void The_stack_size_is_the_one_the_config_asked_for()
    {
        var smallContext = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 1, WorkerStackBytes = 256 * 1024 });
        var largeContext = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 1, WorkerStackBytes = 16 * 1024 * 1024 });

        using (smallContext)
        using (largeContext)
        {
            Assert.Equal(256 * 1024, smallContext.Config.WorkerStackBytes);
            Assert.Equal(16 * 1024 * 1024, largeContext.Config.WorkerStackBytes);

            var small = DeepestRecursion(smallContext);
            var large = DeepestRecursion(largeContext);

            // The usable depth tracks the requested stack size.
            Assert.True(large > small * 4, $"small {small}, large {large}");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_stack_size_is_a_programmer_error(int stackBytes)
    {
        // MINOR 11 regression: WorkerStackBytes = 0 silently meant "the OS default"
        // (8 MiB, too small for the deep recursion this library runs). It is a
        // typed argument error now, like a negative value.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 1, WorkerStackBytes = stackBytes }));
    }

    private static int DeepestRecursionOnAWorker(int stackBytes, int ceiling)
    {
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 1, WorkerStackBytes = stackBytes });

        return DeepestRecursion(context, ceiling);
    }

    private static int DeepestRecursion(ConcurrencyContext context, int ceiling = int.MaxValue)
    {
        var depth = 0;

        var result = context.Run(() =>
        {
            try
            {
                Recurse(ceiling);
            }
            catch (InsufficientExecutionStackException)
            {
                // The worker's stack is too small for the requested depth; the
                // depth counter holds what it managed.
            }
        });

        Assert.True(result.IsCompleted);

        return depth;

        void Recurse(int limit)
        {
            Span<byte> frame = stackalloc byte[FrameBytes];
            frame[0] = 1;
            depth++;

            if (depth >= limit)
            {
                return;
            }

            RuntimeHelpers.EnsureSufficientExecutionStack();
            Recurse(limit);
        }
    }
}
