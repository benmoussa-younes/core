// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Models;
using Core.Mvp.Presenters;
using Core.Mvp.Views;
using Core.Results;

namespace Core.Mvp.Tests;

public sealed class PresenterBaseTests
{
    [Fact]
    public void A_null_view_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new TestPresenter(null!));
    }

    [Fact]
    public void The_view_is_exposed()
    {
        var view = new FakeView();
        var presenter = new TestPresenter(view);

        Assert.Same(view, presenter.View);
    }

    [Fact]
    public void The_view_is_exposed_through_the_non_generic_shape()
    {
        var view = new FakeView();
        IViewPresenter presenter = new TestPresenter(view);

        Assert.Same(view, presenter.View);
    }

    [Fact]
    public async Task The_lifecycle_hooks_receive_the_token()
    {
        using var source = new CancellationTokenSource();
        var presenter = new TestPresenter(new FakeView());

        await presenter.InitializeAsync(source.Token);
        await presenter.ActivateAsync(source.Token);
        await presenter.DeactivateAsync(source.Token);

        Assert.Equal(1, presenter.Initialized);
        Assert.Equal(1, presenter.Activated);
        Assert.Equal(1, presenter.Deactivated);
        Assert.Equal(source.Token, presenter.LastToken);
    }

    [Fact]
    public async Task Disposal_is_idempotent()
    {
        var presenter = new TestPresenter(new FakeView());

        await presenter.DisposeAsync();
        await presenter.DisposeAsync();

        Assert.Equal(1, presenter.Disposed);
    }

    [Fact]
    public async Task Disposal_runs_once_under_a_race()
    {
        var presenter = new TestPresenter(new FakeView());

        await Task.WhenAll(
            Task.Run(async () => await presenter.DisposeAsync(), TestContext.Current.CancellationToken),
            Task.Run(async () => await presenter.DisposeAsync(), TestContext.Current.CancellationToken));

        Assert.Equal(1, presenter.Disposed);
    }

    [Fact]
    public async Task Initialize_is_idempotent()
    {
        var presenter = new TestPresenter(new FakeView());

        await presenter.InitializeAsync(TestContext.Current.CancellationToken);
        await presenter.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, presenter.Initialized);
    }

    [Fact]
    public async Task Use_after_dispose_is_rejected()
    {
        var presenter = new TestPresenter(new FakeView());
        await presenter.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => presenter.InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Throws<ObjectDisposedException>(() => presenter.ActivateAsync(TestContext.Current.CancellationToken));
        Assert.Throws<ObjectDisposedException>(() => presenter.DeactivateAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => presenter.GuardAsync(
                _ => ValueTask.FromResult(Result<Unit>.Success(Unit.Value)),
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Activating_before_initialization_is_rejected()
    {
        var presenter = new TestPresenter(new FakeView());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => presenter.ActivateAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task A_failed_initialization_can_be_retried()
    {
        var presenter = new FlakyPresenter(new FakeView(), failFirst: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => presenter.InitializeAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.False(presenter.IsInitialized);
        Assert.Equal(1, presenter.Attempts);

        await presenter.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(presenter.IsInitialized);
        Assert.Equal(2, presenter.Attempts);

        await presenter.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, presenter.Attempts);
    }

    [Fact]
    public async Task An_asynchronous_initialization_failure_can_be_retried()
    {
        var presenter = new AsyncFlakyPresenter(new FakeView());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => presenter.InitializeAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.False(presenter.IsInitialized);

        await presenter.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(presenter.IsInitialized);
        Assert.Equal(2, presenter.Attempts);
    }

    [Fact]
    public async Task Disposal_observes_a_failed_initialization_and_still_releases()
    {
        var presenter = new AsyncFailingPresenter(new FakeView());
        var initialization = presenter.InitializeAsync(TestContext.Current.CancellationToken).AsTask();
        var disposal = presenter.DisposeAsync().AsTask();

        presenter.Fail();
        await Assert.ThrowsAsync<InvalidOperationException>(() => initialization);
        await disposal;

        Assert.Equal(1, presenter.Disposed);
    }

    [Fact]
    public async Task A_completion_after_disposal_does_not_touch_the_view()
    {
        var view = new FakeView();
        var presenter = new TestPresenter(view);
        var operation = new TaskCompletionSource();

        var guarded = presenter.GuardAsync(
            async _ =>
            {
                await operation.Task;
                return Result<Unit>.Success(Unit.Value);
            },
            TestContext.Current.CancellationToken).AsTask();

        await presenter.DisposeAsync();
        operation.SetResult();
        await guarded;

        Assert.Null(view.LastError);

        // Disposal clears the overlay even though the view outlives the presenter.
        Assert.Equal([true, false], view.BusyStates);
    }

    [Fact]
    public async Task An_activation_failure_resets_the_active_state()
    {
        var presenter = new ThrowingActivatePresenter(new FakeView());
        await presenter.InitializeAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => presenter.ActivateAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.False(presenter.IsActive);
    }

    [Fact]
    public async Task Busy_after_disposal_is_ignored()
    {
        var view = new FakeView();
        var presenter = new TestPresenter(view);

        await presenter.DisposeAsync();
        presenter.MarkBusy(true);

        Assert.False(presenter.IsBusy);
        Assert.Empty(view.BusyStates);
    }

    [Fact]
    public async Task Concurrent_initialization_waits_on_one_operation()
    {
        var presenter = new ControlledPresenter(new FakeView());
        var first = presenter.InitializeAsync(TestContext.Current.CancellationToken).AsTask();
        var second = presenter.InitializeAsync(TestContext.Current.CancellationToken).AsTask();

        Assert.Equal(1, presenter.Attempts);
        Assert.False(presenter.IsInitialized);

        presenter.CompleteInitialization();
        await Task.WhenAll(first, second);

        Assert.True(presenter.IsInitialized);
        Assert.Equal(1, presenter.Attempts);
    }

    [Fact]
    public async Task Activation_and_deactivation_are_idempotent_and_tracked()
    {
        var presenter = new TestPresenter(new FakeView());
        await presenter.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.False(presenter.IsActive);
        await presenter.ActivateAsync(TestContext.Current.CancellationToken);
        await presenter.ActivateAsync(TestContext.Current.CancellationToken);

        Assert.True(presenter.IsActive);
        Assert.Equal(1, presenter.Activated);

        await presenter.DeactivateAsync(TestContext.Current.CancellationToken);
        await presenter.DeactivateAsync(TestContext.Current.CancellationToken);

        Assert.False(presenter.IsActive);
        Assert.Equal(1, presenter.Deactivated);
    }

    [Fact]
    public async Task Disposal_deactivates_an_active_presenter()
    {
        var presenter = new TestPresenter(new FakeView());
        await presenter.InitializeAsync(TestContext.Current.CancellationToken);
        await presenter.ActivateAsync(TestContext.Current.CancellationToken);

        await presenter.DisposeAsync();

        Assert.Equal(1, presenter.Deactivated);
        Assert.Equal(1, presenter.Disposed);
    }

    [Fact]
    public async Task Disposal_cancels_a_pending_initialization_that_observes_its_token()
    {
        var presenter = new ControlledPresenter(new FakeView());
        var initialization = presenter.InitializeAsync(TestContext.Current.CancellationToken).AsTask();
        var disposal = presenter.DisposeAsync().AsTask();

        await disposal.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initialization);

        Assert.Equal(1, presenter.Disposed);
    }

    [Fact]
    public async Task Disposal_waits_for_an_in_flight_activation_then_deactivates()
    {
        var presenter = new SlowActivatePresenter(new FakeView());
        await presenter.InitializeAsync(TestContext.Current.CancellationToken);

        var activation = presenter.ActivateAsync(TestContext.Current.CancellationToken).AsTask();
        var disposal = presenter.DisposeAsync().AsTask();

        Assert.False(disposal.IsCompleted);

        presenter.ReleaseActivation();
        await activation;
        await disposal;

        Assert.Equal(["activate-start", "activate-end", "deactivate", "dispose"], presenter.Log);
    }

    [Fact]
    public void An_unmatched_busy_exit_does_not_touch_the_view()
    {
        var view = new FakeView();
        var presenter = new TestPresenter(view);

        presenter.MarkBusy(false);

        Assert.False(presenter.IsBusy);
        Assert.Empty(view.BusyStates);
    }

    [Fact]
    public async Task Overlapping_operations_stay_busy_until_the_last_finishes()
    {
        var view = new FakeView();
        var presenter = new TestPresenter(view);
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();

        var firstTask = presenter.GuardAsync(
            async _ =>
            {
                await first.Task;
                return Result<Unit>.Success(Unit.Value);
            },
            TestContext.Current.CancellationToken).AsTask();

        var secondTask = presenter.GuardAsync(
            async _ =>
            {
                await second.Task;
                return Result<Unit>.Success(Unit.Value);
            },
            TestContext.Current.CancellationToken).AsTask();

        Assert.True(presenter.IsBusy);

        first.SetResult();
        await firstTask;

        // The second operation is still running, so the view stays busy.
        Assert.True(presenter.IsBusy);

        second.SetResult();
        await secondTask;

        Assert.False(presenter.IsBusy);
        Assert.Equal([true, false], view.BusyStates);
    }

    [Fact]
    public async Task A_successful_operation_toggles_busy_without_an_error()
    {
        var view = new FakeView();
        var presenter = new TestPresenter(view);

        await presenter.GuardAsync(
            _ => ValueTask.FromResult(Result<Unit>.Success(Unit.Value)),
            TestContext.Current.CancellationToken);

        Assert.False(presenter.IsBusy);
        Assert.Null(view.LastError);
        Assert.Equal([true, false], view.BusyStates);
    }

    [Fact]
    public async Task A_failed_operation_presents_the_error_and_clears_busy()
    {
        var view = new FakeView();
        var presenter = new TestPresenter(view);
        var error = new CoreError("test.failed", "failed");

        await presenter.GuardAsync(
            _ => ValueTask.FromResult(Result<Unit>.Failure(error)),
            TestContext.Current.CancellationToken);

        Assert.False(presenter.IsBusy);
        Assert.Same(error, view.LastError);
        Assert.Equal([true, false], view.BusyStates);
    }

    [Fact]
    public async Task A_throwing_operation_clears_busy_and_propagates()
    {
        var view = new FakeView();
        var presenter = new TestPresenter(view);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => presenter.GuardAsync(
                _ => throw new InvalidOperationException("boom"),
                TestContext.Current.CancellationToken).AsTask());

        Assert.False(presenter.IsBusy);
        Assert.Equal([true, false], view.BusyStates);
    }

    [Fact]
    public async Task A_successful_operation_applies_the_value()
    {
        var view = new FakeView();
        var presenter = new TestPresenter(view);
        var applied = 0;

        await presenter.GuardAsync(
            _ => ValueTask.FromResult(Result<int>.Success(42)),
            value => applied = value,
            TestContext.Current.CancellationToken);

        Assert.Equal(42, applied);
        Assert.Null(view.LastError);
    }

    [Fact]
    public async Task A_failed_operation_does_not_apply_the_value()
    {
        var view = new FakeView();
        var presenter = new TestPresenter(view);
        var error = new CoreError("test.failed", "failed");
        var applied = 0;

        await presenter.GuardAsync(
            _ => ValueTask.FromResult(Result<int>.Failure(error)),
            value => applied = value,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, applied);
        Assert.Same(error, view.LastError);
    }

    [Fact]
    public async Task Null_operations_are_rejected()
    {
        var presenter = new TestPresenter(new FakeView());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => presenter.GuardAsync(null!, TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => presenter.GuardAsync(
                _ => ValueTask.FromResult(Result<int>.Success(1)),
                null!,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task The_returning_overload_returns_a_success()
    {
        var view = new FakeView();
        var presenter = new TestPresenter(view);

        var result = await presenter.GuardAsync(
            _ => ValueTask.FromResult(Result<int>.Success(7)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value);
        Assert.Null(view.LastError);
        Assert.Equal([true, false], view.BusyStates);
    }

    [Fact]
    public async Task The_returning_overload_presents_a_failure()
    {
        var view = new FakeView();
        var presenter = new TestPresenter(view);
        var error = new CoreError("test.failed", "failed");

        var result = await presenter.GuardAsync(
            _ => ValueTask.FromResult(Result<int>.Failure(error)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Same(error, view.LastError);
    }

    [Fact]
    public async Task The_returning_overload_rejects_a_null_operation()
    {
        var presenter = new TestPresenter(new FakeView());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => presenter.GuardAsync<int>(null!, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public void Busy_state_is_applied_to_the_view()
    {
        var view = new FakeView();
        var presenter = new TestPresenter(view);

        presenter.MarkBusy(true);
        Assert.True(presenter.IsBusy);

        presenter.MarkBusy(false);
        Assert.False(presenter.IsBusy);

        Assert.Equal([true, false], view.BusyStates);
    }

    [Fact]
    public async Task The_default_lifecycle_hooks_are_no_ops()
    {
        var presenter = new PlainPresenter(new FakeView());

        await presenter.InitializeAsync(TestContext.Current.CancellationToken);
        await presenter.ActivateAsync(TestContext.Current.CancellationToken);
        await presenter.DeactivateAsync(TestContext.Current.CancellationToken);
        await presenter.DisposeAsync();

        Assert.False(presenter.IsBusy);
    }

    [Fact]
    public async Task The_view_callbacks_run_on_the_captured_synchronization_context()
    {
        var previous = SynchronizationContext.Current;
        var context = new RecordingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var view = new FakeView();
            var presenter = new TestPresenter(view);

            await presenter.GuardAsync(
                async _ =>
                {
                    await Task.Delay(1).ConfigureAwait(false);
                    return Result<Unit>.Failure(new CoreError("test.failed", "failed"));
                },
                TestContext.Current.CancellationToken);

            Assert.True(view.BusyStates.Count >= 2);
            Assert.All(view.CallbackContexts, observed => Assert.Same(context, observed));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class ControlledPresenter(FakeView view) : PresenterBase<FakeView>(view)
    {
        private readonly TaskCompletionSource _initialization = new();

        public int Attempts { get; private set; }

        public int Disposed { get; private set; }

        public void CompleteInitialization() => _initialization.TrySetResult();

        protected override async ValueTask OnInitializeAsync(CancellationToken cancellationToken)
        {
            Attempts++;
            await _initialization.Task.WaitAsync(cancellationToken);
        }

        protected override ValueTask OnDisposeAsync()
        {
            Disposed++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FlakyPresenter(FakeView view, bool failFirst) : PresenterBase<FakeView>(view)
    {
        private bool _fail = failFirst;

        public int Attempts { get; private set; }

        protected override ValueTask OnInitializeAsync(CancellationToken cancellationToken)
        {
            Attempts++;

            if (_fail)
            {
                _fail = false;
                throw new InvalidOperationException("the first attempt fails");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class AsyncFlakyPresenter(FakeView view) : PresenterBase<FakeView>(view)
    {
        public int Attempts { get; private set; }

        protected override async ValueTask OnInitializeAsync(CancellationToken cancellationToken)
        {
            Attempts++;
            await Task.Yield();

            if (Attempts == 1)
            {
                throw new InvalidOperationException("the first attempt fails");
            }
        }
    }

    private sealed class AsyncFailingPresenter(FakeView view) : PresenterBase<FakeView>(view)
    {
        private readonly TaskCompletionSource _failure = new();

        public int Disposed { get; private set; }

        public void Fail() => _failure.TrySetResult();

        protected override async ValueTask OnInitializeAsync(CancellationToken cancellationToken)
        {
            // This presenter models an uncooperative hook: disposal still waits
            // for it, then releases the presenter once the failure surfaces.
            await _failure.Task;
            throw new InvalidOperationException("initialization failed");
        }

        protected override ValueTask OnDisposeAsync()
        {
            Disposed++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingActivatePresenter(FakeView view) : PresenterBase<FakeView>(view)
    {
        protected override ValueTask OnActivateAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("activation failed");
    }

    private sealed class SlowActivatePresenter(FakeView view) : PresenterBase<FakeView>(view)
    {
        private readonly TaskCompletionSource _activation = new();

        public List<string> Log { get; } = [];

        public void ReleaseActivation() => _activation.TrySetResult();

        protected override async ValueTask OnActivateAsync(CancellationToken cancellationToken)
        {
            Log.Add("activate-start");
            await _activation.Task;
            Log.Add("activate-end");
        }

        protected override ValueTask OnDeactivateAsync(CancellationToken cancellationToken)
        {
            Log.Add("deactivate");
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnDisposeAsync()
        {
            Log.Add("dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PlainPresenter : PresenterBase<FakeView>
    {
        public PlainPresenter(FakeView view)
            : base(view)
        {
        }
    }

    private sealed class FakeView : IView
    {
        public List<bool> BusyStates { get; } = [];

        public List<SynchronizationContext?> CallbackContexts { get; } = [];

        public CoreError? LastError { get; private set; }

        public void SetBusy(bool busy)
        {
            BusyStates.Add(busy);
            CallbackContexts.Add(SynchronizationContext.Current);
        }

        public void ShowError(CoreError error)
        {
            LastError = error;
            CallbackContexts.Add(SynchronizationContext.Current);
        }
    }

    private sealed class TestPresenter : PresenterBase<FakeView>
    {
        public TestPresenter(FakeView view)
            : base(view)
        {
        }

        public int Initialized { get; private set; }

        public int Activated { get; private set; }

        public int Deactivated { get; private set; }

        public int Disposed { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public ValueTask GuardAsync(
            Func<CancellationToken, ValueTask<Result<Unit>>> operation,
            CancellationToken cancellationToken = default)
        {
            return RunGuardedAsync(operation, cancellationToken);
        }

        public ValueTask GuardAsync<T>(
            Func<CancellationToken, ValueTask<Result<T>>> operation,
            Action<T> apply,
            CancellationToken cancellationToken = default)
        {
            return RunGuardedAsync(operation, apply, cancellationToken);
        }

        public ValueTask<Result<T>> GuardAsync<T>(
            Func<CancellationToken, ValueTask<Result<T>>> operation,
            CancellationToken cancellationToken)
        {
            return RunGuardedAsync(operation, cancellationToken);
        }

        public void MarkBusy(bool busy) => SetBusy(busy);

        protected override ValueTask OnInitializeAsync(CancellationToken cancellationToken)
        {
            Initialized++;
            LastToken = cancellationToken;
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnActivateAsync(CancellationToken cancellationToken)
        {
            Activated++;
            LastToken = cancellationToken;
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnDeactivateAsync(CancellationToken cancellationToken)
        {
            Deactivated++;
            LastToken = cancellationToken;
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnDisposeAsync()
        {
            Disposed++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // A real UI context installs itself for the posted work; without it
            // SynchronizationContext.Current inside the continuation is null and
            // the guard would not discriminate.
            ThreadPool.QueueUserWorkItem(
                _ =>
                {
                    var previous = Current;
                    SetSynchronizationContext(this);
                    try
                    {
                        d(state);
                    }
                    finally
                    {
                        SetSynchronizationContext(previous);
                    }
                });
        }
    }
}
