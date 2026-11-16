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
    private readonly IApplicationLogger _logger;
    private readonly IOperationJournal _journal;
    private readonly ICrashLog _crashLog;
    private readonly ISingleInstance? _singleInstance;
    private readonly IProcessRunner _processes;
    private readonly ISystemLinkLauncher _links;
    private readonly ISystemNotifier _notifier;
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _shutdownGate = new();
    private TrayService? _tray;
    private CoreWindow? _window;
    private bool _started;
    private int _disposed;
    private Task? _shutdownTask;

    private CoreHost(
        ApplicationPaths paths,
        IApplicationLogger logger,
        IOperationJournal journal,
        ICrashLog crashLog,
        ISingleInstance? singleInstance,
        IProcessRunner processes,
        ISystemLinkLauncher links,
        ISystemNotifier notifier)
    {
        Paths = paths;
        _logger = logger;
        _journal = journal;
        _crashLog = crashLog;
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
    public IApplicationLogger Logger => _logger;

    /// <summary>Gets the operation journal.</summary>
    public IOperationJournal Journal => _journal;

    /// <summary>Gets the last-chance crash log.</summary>
    public ICrashLog CrashLog => _crashLog;

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

        var paths = ApplicationPaths.Create(directories.Value, options.Vendor, options.Application);

        if (paths.TryGetError(out var pathError))
        {
            return Result<CoreHost>.Failure(pathError);
        }

        if (options.LogMaxBytes <= 0 || options.RetainedFiles <= 0 || options.CrashMaximumLines <= 0)
        {
            return Result<CoreHost>.Failure(new CoreError(
                CoreErrorCodes.HostInvalidOptions,
                "The log size, the retained archive count and the crash line count must be positive."));
        }

        var runtimeDirectory = directories.Value.RuntimeDirectory;

        if (options.SingleInstance &&
            (string.IsNullOrWhiteSpace(runtimeDirectory) || !Path.IsPathRooted(runtimeDirectory)))
        {
            return Result<CoreHost>.Failure(new CoreError(
                CoreErrorCodes.HostRuntimeDirectoryUnset,
                "A single-instance host needs an absolute XDG_RUNTIME_DIR; disable the lock explicitly to run without one."));
        }

        IApplicationLogger? logger = null;
        IOperationJournal? journal = null;
        ICrashLog? crashLog = null;
        ISingleInstance? singleInstance = null;

        try
        {
            services.PrepareStateDirectory(paths.Value.StateDirectory);

            logger = services.CreateLogger(
                paths.Value.StateFile("application.log"),
                options.LogMaxBytes,
                options.RetainedFiles);
            journal = services.CreateJournal(
                paths.Value.StateFile("operations.log"),
                options.LogMaxBytes,
                options.RetainedFiles);
            crashLog = services.CreateCrashLog(
                paths.Value.StateFile("crash.log"),
                options.LogMaxBytes,
                options.RetainedFiles,
                options.CrashMaximumLines);

            if (options.SingleInstance)
            {
                var user = PathComponent.Validate(Environment.UserName).ValueOr("user");
                var socketPath = Path.Combine(
                    runtimeDirectory!,
                    $"core-{options.Application}-{user}.sock");
                singleInstance = services.CreateSingleInstance(socketPath);
            }

            return Result<CoreHost>.Success(new CoreHost(
                paths.Value,
                logger,
                journal,
                crashLog,
                singleInstance,
                services.Processes,
                services.Links,
                services.CreateNotifier(options.Application)));
        }
        catch (ArgumentException exception)
        {
            DisposeAll(logger, journal, crashLog, singleInstance);

            return Result<CoreHost>.Failure(new CoreError(
                CoreErrorCodes.HostSocketPathInvalid,
                PathRedaction.ForMessage(exception.Message)));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            PlatformNotSupportedException)
        {
            DisposeAll(logger, journal, crashLog, singleInstance);

            return Result<CoreHost>.Failure(new CoreError(
                CoreErrorCodes.HostStateUnwritable,
                $"The state directory '{PathRedaction.ForMessage(paths.Value.StateDirectory)}' could not be created."));
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
            _logger.Log(
                LogLevel.Error,
                $"The single-instance lock could not start: {exception.GetType().Name}.");
            return false;
        }

        _started = true;
        _logger.Log(LogLevel.Information, "application started");
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
                _logger.Log(
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var previous = application.UnhandledExceptionHandler;
        application.UnhandledExceptionHandler = exception =>
        {
            _crashLog.Write(exception, "application");
            previous?.Invoke(exception);
        };
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
            return new ValueTask(_shutdownTask ??= ShutdownCoreAsync());
        }
    }

    private async Task ShutdownCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);

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
                        _journal.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            _logger.Dispose();
                        }
                        finally
                        {
                            try
                            {
                                _crashLog.Dispose();
                            }
                            finally
                            {
                                _stopping.Dispose();
                            }
                        }
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ShutdownAsync();

    internal void OnActivated(ReadOnlyMemory<byte> payload)
    {
        // The instance lock raises this on a background thread. Once the window
        // exists, the product expects to touch the UI tree, so marshal; a host
        // without a window (a headless test or a pre-window launch) stays direct.
        if (_window is null)
        {
            Raise();
            return;
        }

        DispatchToUi(Raise);

        void Raise()
        {
            if (payload.IsEmpty)
            {
                ActivationRequested?.Invoke();
                return;
            }

            Activated?.Invoke(payload.ToArray());
        }
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

    private static void DisposeAll(
        IApplicationLogger? logger,
        IOperationJournal? journal,
        ICrashLog? crashLog,
        ISingleInstance? singleInstance)
    {
        singleInstance?.Dispose();
        journal?.Dispose();
        logger?.Dispose();
        crashLog?.Dispose();
    }
}
