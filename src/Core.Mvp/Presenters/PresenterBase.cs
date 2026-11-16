// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Models;
using Core.Mvp.Views;
using Core.Results;

namespace Core.Mvp.Presenters;

/// <summary>
/// A base presenter for a passive view. It owns the view, tracks the busy state
/// and turns service results into view updates, so a derived presenter only
/// describes what a screen does.
/// </summary>
/// <typeparam name="TView">The view type the presenter drives.</typeparam>
/// <remarks>
/// <para>
/// The lifecycle is a state machine: a presenter is initialized once, activated
/// and deactivated as its view is shown and hidden, then disposed. Concurrent
/// initialization waits on the same operation; a failed initialization can be
/// retried; activating before initialization is a programmer error.
/// </para>
/// <para>
/// The view is mutated on the context that calls the guarded operations: the
/// continuations do not use <c>ConfigureAwait(false)</c>, so a UI presenter must
/// be driven from the UI thread and <see cref="IView"/> is not thread-safe.
/// </para>
/// <para>
/// The lifecycle calls themselves must be serialized by the host: a concurrent
/// activate/deactivate pair is not ordered by this type. The shell queues one
/// operation at a time; a direct consumer must do the same. An initialization
/// hook should observe its cancellation token, because disposal waits for it.
/// </para>
/// </remarks>
public abstract class PresenterBase<TView> : IPresenter<TView>, IViewPresenter
    where TView : class, IView
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _initialization;
    private Task? _activation;
    private bool _initialized;
    private bool _active;
    private int _busy;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PresenterBase{TView}"/> class.
    /// </summary>
    /// <param name="view">The view the presenter drives.</param>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is <see langword="null"/>.</exception>
    protected PresenterBase(TView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        View = view;
    }

    /// <inheritdoc />
    public TView View { get; }

    /// <inheritdoc />
    public bool IsBusy => Volatile.Read(ref _busy) > 0;

    /// <summary>Gets a value indicating whether initialization has completed.</summary>
    public bool IsInitialized
    {
        get
        {
            lock (_gate)
            {
                return _initialized;
            }
        }
    }

    /// <summary>Gets a value indicating whether the presenter is currently active.</summary>
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    IView IViewPresenter.View => View;

    /// <inheritdoc />
    /// <remarks>
    /// Initializes the presenter once. A concurrent caller waits on the same
    /// operation; a failed or cancelled initialization resets the state, so the
    /// caller can retry.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The presenter has been disposed.</exception>
    public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            if (_initialized)
            {
                return ValueTask.CompletedTask;
            }

            // A caller that has observed a failed task must be able to retry; the
            // faulted task is replaced here, in the same lock, without a detached
            // continuation that could race the retry.
            if (_initialization is null ||
                _initialization.IsFaulted ||
                _initialization.IsCanceled)
            {
                _initialization = InitializeCoreAsync(cancellationToken);
            }

            return new ValueTask(_initialization);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Activating before a successful initialization is a programmer error;
    /// activating an already active presenter is a no-op.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The presenter has been disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The presenter has not been initialized successfully.
    /// </exception>
    public ValueTask ActivateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        Task activation;

        lock (_gate)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    "Initialize the presenter before activating it.");
            }

            if (_active)
            {
                return ValueTask.CompletedTask;
            }

            _active = true;
            activation = ActivateCoreAsync(cancellationToken);
            _activation = activation;
        }

        return new ValueTask(activation);
    }

    /// <inheritdoc />
    /// <remarks>Deactivating an inactive presenter is a no-op.</remarks>
    /// <exception cref="ObjectDisposedException">The presenter has been disposed.</exception>
    public ValueTask DeactivateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            if (!_active)
            {
                return ValueTask.CompletedTask;
            }

            _active = false;
        }

        return DeactivateCoreAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Disposal is idempotent. It cancels the presenter's lifetime token, waits
    /// for an in-flight initialization and activation, then deactivates the
    /// presenter when it is active, so a direct caller gets the same cleanup as
    /// the shell. A hook that ignores its token still delays disposal; the token
    /// is the contract that lets disposal interrupt a long initialization.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _lifetime.Cancel();

        Task? initialization;
        Task? activation;

        lock (_gate)
        {
            initialization = _initialization;
            activation = _activation;
        }

        if (initialization is not null)
        {
            try
            {
                await initialization.ConfigureAwait(true);
            }
            catch
            {
                // A failed or cancelled initialization has already surfaced through
                // its caller; disposal still releases whatever the presenter built.
            }
        }

        if (activation is not null)
        {
            try
            {
                await activation.ConfigureAwait(true);
            }
            catch
            {
                // An activation that failed or was cancelled is already reflected
                // in IsActive; disposal still runs the release path.
            }
        }

        bool active;

        lock (_gate)
        {
            active = _active;
            _active = false;
        }

        try
        {
            if (active)
            {
                await OnDeactivateAsync(CancellationToken.None).ConfigureAwait(true);
            }
        }
        finally
        {
            // A view that outlives the presenter must not keep the busy overlay:
            // ExitBusy deliberately does not touch the view after disposal.
            if (Volatile.Read(ref _busy) > 0)
            {
                Interlocked.Exchange(ref _busy, 0);
                View.SetBusy(false);
            }

            await OnDisposeAsync().ConfigureAwait(true);
            _lifetime.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Runs a value-less service operation under the busy state and presents a failure.
    /// </summary>
    /// <param name="operation">The operation to run.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A value task that completes when the operation has finished.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    protected ValueTask RunGuardedAsync(
        Func<CancellationToken, ValueTask<Result<Unit>>> operation,
        CancellationToken cancellationToken)
    {
        return RunGuardedAsync(operation, static _ => { }, cancellationToken);
    }

    /// <summary>
    /// Runs a service operation under the busy state, applies its value on
    /// success and presents a failure otherwise.
    /// </summary>
    /// <typeparam name="T">The value type produced on success.</typeparam>
    /// <param name="operation">The service operation.</param>
    /// <param name="apply">The action applied to the value on success.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A value task that completes when the operation has finished.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> or <paramref name="apply"/> is <see langword="null"/>.</exception>
    protected async ValueTask RunGuardedAsync<T>(
        Func<CancellationToken, ValueTask<Result<T>>> operation,
        Action<T> apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(apply);
        ThrowIfDisposed();

        EnterBusy();

        try
        {
            var result = await operation(cancellationToken);

            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            if (result.TryGetValue(out var value))
            {
                apply(value);
            }
            else
            {
                View.ShowError(result.Error);
            }
        }
        finally
        {
            ExitBusy();
        }
    }

    /// <summary>
    /// Runs a service operation under the busy state and returns its result,
    /// presenting a failure. Unlike the callback overload, this adds no
    /// capturing closure, so a call whose operation delegate is cached stays
    /// allocation-free.
    /// </summary>
    /// <typeparam name="T">The value type produced on success.</typeparam>
    /// <param name="operation">The service operation.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The operation result, after any failure has been presented.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    protected async ValueTask<Result<T>> RunGuardedAsync<T>(
        Func<CancellationToken, ValueTask<Result<T>>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();

        EnterBusy();

        try
        {
            var result = await operation(cancellationToken);

            if (result.IsFailure && Volatile.Read(ref _disposed) == 0)
            {
                View.ShowError(result.Error);
            }

            return result;
        }
        finally
        {
            ExitBusy();
        }
    }

    /// <summary>
    /// Sets the busy state and applies it to the view. Enter and exit are counted,
    /// so overlapping operations keep the view busy until the last one finishes.
    /// </summary>
    /// <param name="busy">Whether the presentation is busy.</param>
    protected void SetBusy(bool busy)
    {
        if (busy)
        {
            EnterBusy();
        }
        else
        {
            ExitBusy();
        }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        // The caller can cancel its own initialization; disposal cancels the
        // lifetime token, so a hook that observes its token cannot block shutdown.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);

        await OnInitializeAsync(linked.Token).ConfigureAwait(true);

        lock (_gate)
        {
            _initialized = true;
        }
    }

    private async Task ActivateCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);

            await OnActivateAsync(linked.Token).ConfigureAwait(true);
        }
        catch
        {
            lock (_gate)
            {
                _active = false;
            }

            throw;
        }
    }

    private async ValueTask DeactivateCoreAsync(CancellationToken cancellationToken)
    {
        await OnDeactivateAsync(cancellationToken).ConfigureAwait(true);
    }

    private void EnterBusy()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (Interlocked.Increment(ref _busy) == 1)
        {
            View.SetBusy(true);
        }
    }

    private void ExitBusy()
    {
        var remaining = Interlocked.Decrement(ref _busy);

        if (remaining == 0)
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                View.SetBusy(false);
            }

            return;
        }

        // An unmatched exit is a caller bug, but it must not flip the view's
        // busy state while another operation still owns the counter. Clamp the
        // counter back to zero without touching the view; the class is UI-thread
        // affine, so this repair cannot race a legitimate EnterBusy.
        if (remaining < 0)
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    /// <summary>
    /// Initializes the presenter. Called once by <see cref="InitializeAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A value task that completes when the presenter is initialized.</returns>
    protected virtual ValueTask OnInitializeAsync(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Activates the presenter.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A value task that completes when the presenter is active.</returns>
    protected virtual ValueTask OnActivateAsync(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Deactivates the presenter.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A value task that completes when the presenter is inactive.</returns>
    protected virtual ValueTask OnDeactivateAsync(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Releases the presenter's resources. Called once by <see cref="DisposeAsync"/>.
    /// </summary>
    /// <returns>A value task that completes when the presenter has been disposed.</returns>
    protected virtual ValueTask OnDisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
