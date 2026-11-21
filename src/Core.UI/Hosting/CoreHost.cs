// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Threading;
using Core.UI.Chrome;
using Core.UI.Tray;
using Core.Diagnostics;
using Core.Execution;
using Core.Notifications;
using Core.Paths;
using Core.Platform;
using Core.Primitives;
using Core.Results;

namespace Core.UI.Hosting;

/// <summary>
/// The explicit composition root of a Core application: it owns the state
/// paths, the durable diagnostics, the optional single-instance lock, the process
/// seam and the tray/window pair. Every platform implementation comes from
/// <see cref="CorePlatform.HostServices"/>; the host itself has no Linux type and
/// no reflection, so the same code is used by the shipping NativeAOT binary.
/// </summary>
/// <remarks>
/// <para>
/// The startup order is fixed: resolve the paths, create the state directory,
/// open the logger, journal and crash log, acquire the single-instance lock, then
/// create the window. <see cref="ShutdownAsync"/> reverses it after stopping the
/// presenters.
/// </para>
/// <para>
/// A product keeps the host for the process lifetime, calls
/// <see cref="TryStart"/> once and <see cref="CreateWindow"/>, then awaits
/// <see cref="ShutdownAsync"/> from its quit path before stopping the lifetime.
/// </para>
/// </remarks>
public sealed class CoreHost : IAsyncDisposable
{
    private readonly CoreDiagnostics _diagnostics;
    private readonly ISingleInstance? _singleInstance;
    private readonly IProcessRunner _processes;
    private readonly ISystemLinkLauncher _links;
    private readonly ISystemNotifier _notifier;
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _shutdownGate = new();
    private TrayService? _tray;
    private CoreWindow? _window;
    private CoreApplication? _attachedApplication;
    private Action<Exception>? _applicationHandler;
    private Action<Exception>? _previousApplicationHandler;
    private bool _started;
    private int _disposed;
    private Task? _shutdownTask;

    private CoreHost(
        CoreDiagnostics diagnostics,
        ISingleInstance? singleInstance,
        IProcessRunner processes,
        ISystemLinkLauncher links,
        ISystemNotifier notifier)
    {
        Paths = diagnostics.Paths;
        _diagnostics = diagnostics;
        _singleInstance = singleInstance;
        _processes = processes;
        _links = links;
        _notifier = notifier;

        if (singleInstance is not null)
        {
            // The lock raises both events for a payload launch; the host exposes
            // exactly one so a product cannot handle the same launch twice.
            singleInstance.Activated += OnActivated;
        }
    }

    /// <summary>Gets the application directories.</summary>
    public ApplicationPaths Paths { get; }

    /// <summary>Gets the durable application log.</summary>
    public IApplicationLogger Logger => _diagnostics.Logger;

    /// <summary>Gets the operation journal.</summary>
    public IOperationJournal Journal => _diagnostics.Journal;

    /// <summary>Gets the last-chance crash log.</summary>
    public ICrashLog CrashLog => _diagnostics.CrashLog;

    /// <summary>Gets the process runner used by the platform adapters.</summary>
    public IProcessRunner Processes => _processes;

    /// <summary>Gets the URL launcher.</summary>
    public ISystemLinkLauncher Links => _links;

    /// <summary>Gets the desktop notifier.</summary>
    public ISystemNotifier Notifier => _notifier;

    /// <summary>Gets the single-instance lock, or <see langword="null"/> when disabled.</summary>
    public ISingleInstance? SingleInstance => _singleInstance;

    /// <summary>Gets the token cancelled when the host starts shutting down.</summary>
    public CancellationToken Stopping => _stopping.Token;

    /// <summary>Gets the window created by <see cref="CreateWindow"/>.</summary>
    /// <exception cref="InvalidOperationException">The window has not been created.</exception>
    public CoreWindow Window =>
        _window ?? throw new InvalidOperationException("Create the window before using it.");

    /// <summary>
    /// Raised when another launch asks this instance to surface. It runs on the
    /// instance-lock background thread; a UI host marshals it to its own thread.
    /// </summary>
    public event Action? ActivationRequested;

    /// <summary>
    /// Raised with a copy of the activation payload. It runs on the instance-lock
    /// background thread; a UI host marshals it to its own thread.
    /// </summary>
    public event Action<byte[]>? Activated;

    /// <summary>Raised when the tray activates a command id.</summary>
    public event Action<string>? TrayCommandInvoked;

    /// <summary>
    /// Raised when the window asks the application to quit. A handler returns a
    /// value task, so the window awaits a flush before its presenters are disposed.
    /// </summary>
    public event Func<ValueTask>? QuitRequested;

    /// <summary>
    /// Builds the host: resolves the XDG paths, creates the state directory and
    /// opens the logger, journal, crash log and instance lock. It creates no
    /// window and displays nothing.
    /// </summary>
    /// <param name="options">The host options.</param>
    /// <returns>The host, or a typed failure when a path or an option is invalid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static Result<CoreHost> Create(CoreHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Platform is null)
        {
            return Result<CoreHost>.Failure(new CoreError(
                CoreErrorCodes.HostPlatformRequired,
                "A host needs an explicit platform composition; the caller supplies CorePlatform."));
        }

        var services = options.Platform.HostServices;

        if (services is null)
        {
            return Result<CoreHost>.Failure(new CoreError(
                CoreErrorCodes.HostPlatformRequired,
                "A host needs a platform composition that supplies the host services."));
        }

        var directories = options.Platform.Paths.Resolve();

        if (directories.TryGetError(out var directoryError))
        {
            return Result<CoreHost>.Failure(directoryError);
        }

        // The diagnostics own the path and the retention validation: they resolve
        // the same <vendor>/<application> paths and report the same typed failures.
        var runtimeDirectory = directories.Value.RuntimeDirectory;

        if (options.SingleInstance &&
            (string.IsNullOrWhiteSpace(runtimeDirectory) || !Path.IsPathRooted(runtimeDirectory)))
        {
            return Result<CoreHost>.Failure(new CoreError(
                CoreErrorCodes.HostRuntimeDirectoryUnset,
                "A single-instance host needs an absolute XDG_RUNTIME_DIR; disable the lock explicitly to run without one."));
        }

        var created = CoreDiagnostics.Create(directories.Value, options);

        if (created.TryGetError(out var diagnosticsError))
        {
            return Result<CoreHost>.Failure(diagnosticsError);
        }

        var diagnostics = created.Value;
        ISingleInstance? singleInstance = null;

        try
        {
            if (options.SingleInstance)
            {
                // The vendor is part of the name: the state directory already
                // isolates two vendors' products, and the lock must too.
                var socketName = options.IncludeUserInSingleInstanceName
                    ? SingleInstanceSocketName.For(
                        options.Vendor,
                        options.Application,
                        PathComponent.Validate(Environment.UserName).ValueOr("user"))
                    : SingleInstanceSocketName.For(options.Vendor, options.Application);
                var socketPath = Path.Combine(runtimeDirectory!, socketName);
                singleInstance = services.CreateSingleInstance(socketPath);
            }

            return Result<CoreHost>.Success(new CoreHost(
                diagnostics,
                singleInstance,
                services.Processes,
                services.Links,
                services.CreateNotifier(options.Application)));
        }
        catch (ArgumentException exception)
        {
            diagnostics.Dispose();
            singleInstance?.Dispose();

            return Result<CoreHost>.Failure(new CoreError(
                CoreErrorCodes.HostSocketPathInvalid,
                PathRedaction.ForMessage(exception.Message)));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            PlatformNotSupportedException)
        {
            diagnostics.Dispose();
            singleInstance?.Dispose();

            return Result<CoreHost>.Failure(new CoreError(
                CoreErrorCodes.HostStateUnwritable,
                $"The state directory '{PathRedaction.ForMessage(diagnostics.Paths.StateDirectory)}' could not be created."));
        }
    }

    /// <summary>
    /// Acquires the per-user instance lock when enabled. A product calls it once
    /// and exits when it returns <see langword="false"/>.
    /// </summary>
    /// <returns>Whether this process is the running instance.</returns>
    public bool TryStart()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (_started)
        {
            return true;
        }

        try
        {
            if (_singleInstance is not null && !_singleInstance.TryAcquire())
            {
                return false;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SocketException
                or ArgumentException or PlatformNotSupportedException)
        {
            _diagnostics.Logger.Log(
                LogLevel.Error,
                $"The single-instance lock could not start: {exception.GetType().Name}.");
            return false;
        }

        _started = true;
        _diagnostics.Logger.Log(LogLevel.Information, "application started");
        return true;
    }

    /// <summary>Creates the shell window and its tray service.</summary>
    /// <param name="trayIcon">The tray icon, or <see langword="null"/> when none is available.</param>
    /// <returns>The window.</returns>
    /// <exception cref="InvalidOperationException">The window was already created.</exception>
    public CoreWindow CreateWindow(WindowIcon? trayIcon = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (_window is not null)
        {
            throw new InvalidOperationException("The window has already been created.");
        }

        _tray = new TrayService(trayIcon, id => DispatchToUi(() => TrayCommandInvoked?.Invoke(id)));
        _window = new CoreWindow(_tray);
        _window.QuitRequested += ForwardQuitAsync;
        return _window;
    }

    private async ValueTask ForwardQuitAsync()
    {
        if (QuitRequested is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<ValueTask>>())
        {
            try
            {
                await handler().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                // One failing flush must not skip the remaining handlers; the
                // window still proceeds to its presenter shutdown.
                _diagnostics.Logger.Log(
                    LogLevel.Error,
                    $"A quit handler failed: {exception.GetType().Name}.");
            }
        }
    }

    /// <summary>
    /// Attaches the crash log to an application's last-chance handler. The
    /// process-level hooks route through this handler.
    /// </summary>
    /// <param name="application">The application.</param>
    /// <exception cref="ArgumentNullException"><paramref name="application"/> is <see langword="null"/>.</exception>
    public void Attach(CoreApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        lock (_shutdownGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            Detach();

            _attachedApplication = application;
            _previousApplicationHandler = application.UnhandledExceptionHandler;

            Action<Exception> handler = exception =>
            {
                _diagnostics.Report(exception);
                _previousApplicationHandler?.Invoke(exception);
            };

            _applicationHandler = handler;
            application.UnhandledExceptionHandler = handler;
        }
    }

    /// <summary>
    /// Stops the presenters, disposes the window/tray, releases the instance lock
    /// and closes the diagnostics. It is idempotent and safe to await from the
    /// product's quit path.
    /// </summary>
    /// <returns>A value task that completes when the host has released its resources.</returns>
    public ValueTask ShutdownAsync()
    {
        lock (_shutdownGate)
        {
            if (_shutdownTask is null)
            {
                // WHY: the flag is set under the gate Attach takes, so a concurrent
                // Attach cannot install a handler that captures a diagnostics this
                // shutdown then disposes.
                Interlocked.Exchange(ref _disposed, 1);
                _shutdownTask = ShutdownCoreAsync();
            }

            return new ValueTask(_shutdownTask);
        }
    }

    private async Task ShutdownCoreAsync()
    {
        try
        {
            _stopping.Cancel();

            if (_window is not null)
            {
                await _window.ShutdownAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            // The handler captures this host and its crash log: leaving it on the
            // application would swallow every later failure once the log is closed.
            Detach();

            // Every resource is released even when one of them throws; the first
            // failure still surfaces to the caller.
            try
            {
                _tray?.Dispose();
            }
            finally
            {
                try
                {
                    _singleInstance?.Dispose();
                }
                finally
                {
                    try
                    {
                        _diagnostics.Dispose();
                    }
                    finally
                    {
                        _stopping.Dispose();
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ShutdownAsync();

    internal void OnActivated(ReadOnlyMemory<byte> payload)
    {
        // A host that shut down must not raise activation on a stopped application.
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        // The lock lends the buffer for the duration of the callback, and the UI
        // dispatch below is deferred, so the copy happens before this returns.
        var copy = payload.IsEmpty ? [] : payload.ToArray();

        // The instance lock raises this on a background thread. Once the window
        // exists, the product expects to touch the UI tree, so marshal; a host
        // without a window (a headless test or a pre-window launch) stays direct.
        if (_window is null)
        {
            Raise(copy);
            return;
        }

        DispatchToUi(() => Raise(copy));

        void Raise(byte[] bytes)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            if (bytes.Length == 0)
            {
                ActivationRequested?.Invoke();
                return;
            }

            Activated?.Invoke(bytes);
        }
    }

    private void Detach()
    {
        // Only restore a handler that is still ours: a product that installed its
        // own after Attach keeps it.
        if (_attachedApplication is { } application &&
            ReferenceEquals(application.UnhandledExceptionHandler, _applicationHandler))
        {
            application.UnhandledExceptionHandler = _previousApplicationHandler;
        }

        _attachedApplication = null;
        _applicationHandler = null;
        _previousApplicationHandler = null;
    }

    private static void DispatchToUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }
}
