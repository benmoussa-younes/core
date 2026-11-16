// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace Core.IO.Pipeline;

/// <summary>
/// Runs a producer, a fixed set of shared-nothing workers and a deterministic
/// re-sequencing consumer over a generic input sequence.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline keeps at most <c>threads * BlockPipelineOptions.InFlightBlocksPerThread</c>
/// produced-but-unemitted blocks alive: <see cref="BlockPipelineOptions.InFlightBlocksPerThread"/>
/// bounds each worker thread, so the total across all workers is
/// <c>threads * InFlightBlocksPerThread</c>. Items are emitted in source order for
/// any thread count. Worker state is created once per thread and never shared.
/// </para>
/// <para>
/// Exceptions from the source, <c>process</c> or <c>emit</c> are programmer errors:
/// they cancel the run, unwind every task and are rethrown unchanged. Cancellation
/// yields an <see cref="OperationCanceledException"/>.
/// </para>
/// </remarks>
public static class BlockPipeline
{
    private const int MaximumChannelCapacity = 4096;

    /// <summary>
    /// Processes <paramref name="source"/> with <paramref name="threads"/> workers and
    /// emits the results in source order.
    /// </summary>
    /// <typeparam name="TInput">The input element type.</typeparam>
    /// <typeparam name="TOutput">The output element type.</typeparam>
    /// <typeparam name="TWorkerState">The per-worker state type; a class, never shared between workers.</typeparam>
    /// <param name="source">The sequence to process; enumerated once by the producer.</param>
    /// <param name="threads">The number of worker threads, between 1 and <see cref="BlockPipelineOptions.MaximumThreads"/>.</param>
    /// <param name="options">The block and in-flight bounds.</param>
    /// <param name="createWorkerState">Creates the state for one worker; called exactly <paramref name="threads"/> times.</param>
    /// <param name="disposeWorkerState">Disposes one worker state after the run; may be <see langword="null"/>.</param>
    /// <param name="process">Transforms one item with its worker state.</param>
    /// <param name="emit">Receives every output in source order on the consumer thread.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>A task that completes when every item has been emitted or the run has stopped.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A count or an option is out of range.</exception>
    /// <exception cref="OperationCanceledException">The run was cancelled.</exception>
    public static async Task RunAsync<TInput, TOutput, TWorkerState>(
        IEnumerable<TInput> source,
        int threads,
        BlockPipelineOptions options,
        Func<TWorkerState> createWorkerState,
        Action<TWorkerState>? disposeWorkerState,
        Func<TInput, TWorkerState, TOutput> process,
        Action<TOutput> emit,
        CancellationToken cancellationToken)
        where TWorkerState : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(createWorkerState);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(emit);

        if (threads is < 1 or > BlockPipelineOptions.MaximumThreads)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threads),
                threads,
                "The thread count must be between 1 and MaximumThreads.");
        }

        ValidateOptions(options);

        var maximumInFlight = (long)threads * options.InFlightBlocksPerThread;
        if (maximumInFlight > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.InFlightBlocksPerThread,
                "InFlightBlocksPerThread is too large for the requested thread count.");
        }

        var workerStates = new TWorkerState[threads];
        var created = 0;
        try
        {
            for (; created < threads; created++)
            {
                workerStates[created] = createWorkerState();
            }
        }
        catch
        {
            // A cleanup failure must not mask the factory failure that is unwinding.
            DisposeAll(workerStates, created, disposeWorkerState);
            throw;
        }

        Exception? cleanupFailure = null;

        try
        {
            await RunCoreAsync<TInput, TOutput, TWorkerState>(
                source,
                threads,
                options.InitialBlockSize,
                options.MaximumBlockSize,
                (int)maximumInFlight,
                workerStates,
                process,
                emit,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Dispose every state even when one disposer throws, so no per-worker
            // state leaks and the run's own exception is never replaced.
            cleanupFailure = DisposeAll(workerStates, threads, disposeWorkerState);
        }

        // A dispose failure surfaces only when the run itself succeeded; otherwise
        // the primary failure already propagated from the finally above.
        if (cleanupFailure is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }

    private static Exception? DisposeAll<TWorkerState>(
        TWorkerState[] states,
        int count,
        Action<TWorkerState>? dispose)
        where TWorkerState : class
    {
        Exception? failure = null;

        for (var index = 0; index < count; index++)
        {
            try
            {
                dispose?.Invoke(states[index]);
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }

        return failure;
    }

    private static void ValidateOptions(BlockPipelineOptions options)
    {
        if (options.InitialBlockSize < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.InitialBlockSize,
                "InitialBlockSize must be at least 1.");
        }

        if (options.MaximumBlockSize < options.InitialBlockSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaximumBlockSize,
                "MaximumBlockSize must be at least InitialBlockSize.");
        }

        if (options.InFlightBlocksPerThread < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.InFlightBlocksPerThread,
                "InFlightBlocksPerThread must be at least 1.");
        }
    }

    private static async Task RunCoreAsync<TInput, TOutput, TWorkerState>(
        IEnumerable<TInput> source,
        int threads,
        int initialBlockSize,
        int maximumBlockSize,
        int maximumInFlight,
        TWorkerState[] workerStates,
        Func<TInput, TWorkerState, TOutput> process,
        Action<TOutput> emit,
        CancellationToken cancellationToken)
        where TWorkerState : class
    {
        var channelCapacity = Math.Min(maximumInFlight, MaximumChannelCapacity);
        using var slots = new SemaphoreSlim(maximumInFlight, maximumInFlight);
        var work = Channel.CreateBounded<PipelineBlock<TInput, TOutput>>(
            new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
        var completed = Channel.CreateBounded<PipelineBlock<TInput, TOutput>>(
            new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = cancellation.Token;
        var state = new PipelineRunState(threads);

        var tasks = new Task[threads + 2];
        tasks[0] = Task.Run(
            () => ProducerCoreAsync(
                source,
                threads,
                initialBlockSize,
                maximumBlockSize,
                work.Writer,
                slots,
                state,
                cancellation,
                token),
            CancellationToken.None);
        for (var index = 0; index < threads; index++)
        {
            var workerState = workerStates[index];
            tasks[index + 1] = Task.Run(
                () => WorkerCoreAsync(
                    workerState,
                    work.Reader,
                    completed.Writer,
                    process,
                    state,
                    cancellation,
                    token),
                CancellationToken.None);
        }

        tasks[^1] = Task.Run(
            () => ConsumerCoreAsync(
                completed.Reader,
                emit,
                slots,
                state,
                cancellation,
                token),
            CancellationToken.None);

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            state.Fail(exception);
        }

        // The original exception from the source, process or emit must survive a
        // concurrent caller cancellation. Only when no such failure was recorded
        // does the caller token decide the outcome.
        if (state.Failure is { } failure)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (state.Cancellation is { } cancellationException)
        {
            ExceptionDispatchInfo.Capture(cancellationException).Throw();
        }
    }

    private static async Task ProducerCoreAsync<TInput, TOutput>(
        IEnumerable<TInput> source,
        int threads,
        int initialBlockSize,
        int maximumBlockSize,
        ChannelWriter<PipelineBlock<TInput, TOutput>> writer,
        SemaphoreSlim slots,
        PipelineRunState state,
        CancellationTokenSource cancellation,
        CancellationToken cancellationToken)
    {
        var sequence = 0;
        var blockSize = initialBlockSize;
        var blocksAtCurrentSize = 0;
        PipelineBlock<TInput, TOutput>? current = null;
        try
        {
            foreach (var item in source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (current is null)
                {
                    await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
                    current = PipelineBlock<TInput, TOutput>.Rent(blockSize, sequence++);
                }

                current.Inputs[current.Count++] = item;

                if (current.Count == blockSize)
                {
                    await writer.WriteAsync(current, cancellationToken).ConfigureAwait(false);
                    current = null;
                    blocksAtCurrentSize++;
                    if (blocksAtCurrentSize >= threads && blockSize < maximumBlockSize)
                    {
                        blockSize = Grow(blockSize, maximumBlockSize);
                        blocksAtCurrentSize = 0;
                    }
                }
            }

            if (current is not null)
            {
                await writer.WriteAsync(current, cancellationToken).ConfigureAwait(false);
                current = null;
            }
        }
        catch (Exception exception)
        {
            state.Fail(exception);
            cancellation.Cancel();
            throw;
        }
        finally
        {
            current?.Return();
            writer.TryComplete();
        }
    }

    private static async Task WorkerCoreAsync<TInput, TOutput, TWorkerState>(
        TWorkerState workerState,
        ChannelReader<PipelineBlock<TInput, TOutput>> reader,
        ChannelWriter<PipelineBlock<TInput, TOutput>> writer,
        Func<TInput, TWorkerState, TOutput> process,
        PipelineRunState state,
        CancellationTokenSource cancellation,
        CancellationToken cancellationToken)
        where TWorkerState : class
    {
        try
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var block))
                {
                    var written = false;
                    try
                    {
                        for (var index = 0; index < block.Count; index++)
                        {
                            block.Outputs[index] = process(block.Inputs[index], workerState);
                        }

                        await writer.WriteAsync(block, cancellationToken).ConfigureAwait(false);
                        written = true;
                    }
                    finally
                    {
                        if (!written)
                        {
                            block.Return();
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            state.Fail(exception);
            cancellation.Cancel();
            throw;
        }
        finally
        {
            if (Interlocked.Decrement(ref state.WorkersRemaining) == 0)
            {
                writer.TryComplete();
            }
        }
    }

    private static async Task ConsumerCoreAsync<TInput, TOutput>(
        ChannelReader<PipelineBlock<TInput, TOutput>> reader,
        Action<TOutput> emit,
        SemaphoreSlim slots,
        PipelineRunState state,
        CancellationTokenSource cancellation,
        CancellationToken cancellationToken)
    {
        var pending = new Dictionary<int, PipelineBlock<TInput, TOutput>>();
        var next = 0;
        try
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var block))
                {
                    if (block.Sequence == next)
                    {
                        EmitBlock(block, emit, slots);
                        next++;
                        while (pending.Remove(next, out var ready))
                        {
                            EmitBlock(ready, emit, slots);
                            next++;
                        }
                    }
                    else
                    {
                        pending.Add(block.Sequence, block);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            state.Fail(exception);
            cancellation.Cancel();
            throw;
        }
        finally
        {
            foreach (var block in pending.Values)
            {
                block.Return();
            }
        }
    }

    private static void EmitBlock<TInput, TOutput>(
        PipelineBlock<TInput, TOutput> block,
        Action<TOutput> emit,
        SemaphoreSlim slots)
    {
        try
        {
            for (var index = 0; index < block.Count; index++)
            {
                emit(block.Outputs[index]);
            }
        }
        finally
        {
            block.Return();
            slots.Release();
        }
    }

    private static int Grow(int blockSize, int maximumBlockSize)
    {
        return blockSize <= maximumBlockSize / 2 ? blockSize * 2 : maximumBlockSize;
    }

    private sealed class PipelineRunState
    {
        private Exception? _failure;
        private Exception? _cancellation;

        public PipelineRunState(int workers)
        {
            WorkersRemaining = workers;
        }

        public int WorkersRemaining;

        public Exception? Failure => Volatile.Read(ref _failure);

        public Exception? Cancellation => Volatile.Read(ref _cancellation);

        public void Fail(Exception exception)
        {
            // The tasks race: a cancellation-induced OperationCanceledException may
            // be offered before the source, process or emit exception it raced with.
            // Keep the two kinds apart so the original exception always wins.
            if (exception is OperationCanceledException)
            {
                Interlocked.CompareExchange(ref _cancellation, exception, null);
                return;
            }

            Interlocked.CompareExchange(ref _failure, exception, null);
        }
    }

    private sealed class PipelineBlock<TInput, TOutput>
    {
        public TInput[] Inputs = null!;

        public TOutput[] Outputs = null!;

        public int Sequence;

        public int Count;

        public static PipelineBlock<TInput, TOutput> Rent(int capacity, int sequence)
        {
            return new PipelineBlock<TInput, TOutput>
            {
                Inputs = ArrayPool<TInput>.Shared.Rent(capacity),
                Outputs = ArrayPool<TOutput>.Shared.Rent(capacity),
                Sequence = sequence,
            };
        }

        public void Return()
        {
            var inputs = Inputs;
            var outputs = Outputs;
            Inputs = null!;
            Outputs = null!;

            if (RuntimeHelpers.IsReferenceOrContainsReferences<TInput>())
            {
                Array.Clear(inputs, 0, inputs.Length);
            }

            if (RuntimeHelpers.IsReferenceOrContainsReferences<TOutput>())
            {
                Array.Clear(outputs, 0, outputs.Length);
            }

            ArrayPool<TInput>.Shared.Return(inputs);
            ArrayPool<TOutput>.Shared.Return(outputs);
        }
    }
}
