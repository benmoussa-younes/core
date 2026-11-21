// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Diagnostics;
using Core.Paths;
using Core.Primitives;
using Core.Results;
using Core.Security;

namespace Core.UI.Hosting;

/// <summary>
/// The durable diagnostics of one application: the application log, the operation
/// journal and the crash log, all below its XDG state directory.
/// </summary>
/// <remarks>
/// The layout is the one <see cref="CoreHost"/> uses and it is named by
/// <see cref="ApplicationPaths"/>: <c>&lt;state&gt;/&lt;vendor&gt;/&lt;application&gt;</c>.
/// A product that does not create a host can build this alone, assign it to
/// <see cref="CoreApplication.Diagnostics"/> and still get a crash entry instead of
/// silence. The identity is never guessed: the vendor and the application come
/// from <see cref="CoreHostOptions"/>, exactly as they do for the host.
/// </remarks>
public sealed class CoreDiagnostics : IDisposable
{
    private readonly IApplicationLogger _logger;
    private readonly IOperationJournal _journal;
    private readonly ICrashLog _crashLog;
    private int _disposed;

    private CoreDiagnostics(
        PathSet directories,
        ApplicationPaths paths,
        IApplicationLogger logger,
        IOperationJournal journal,
        ICrashLog crashLog)
    {
        Directories = directories;
        Paths = paths;
        _logger = logger;
        _journal = journal;
        _crashLog = crashLog;
    }

    /// <summary>Gets the resolved base directories.</summary>
    public PathSet Directories { get; }

    /// <summary>Gets the application directories, which name the three files.</summary>
    public ApplicationPaths Paths { get; }

    /// <summary>Gets the durable application log.</summary>
    public IApplicationLogger Logger => _logger;

    /// <summary>Gets the operation journal.</summary>
    public IOperationJournal Journal => _journal;

    /// <summary>Gets the last-chance crash log.</summary>
    public ICrashLog CrashLog => _crashLog;

    /// <summary>
    /// Resolves the XDG state directory, creates it, then opens the application
    /// log, the operation journal and the crash log.
    /// </summary>
    /// <param name="options">The identity, the retention sizes and the platform composition.</param>
    /// <returns>The diagnostics, or a typed failure when a path or an option is invalid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static Result<CoreDiagnostics> Create(CoreHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // WHY: the composition is checked before the environment is read, so a
        // missing platform reports host.platform_required rather than an XDG
        // failure; the sizes and the paths are then checked by the shared overload,
        // in the same order the host follows.
        if (options.Platform?.HostServices is null)
        {
            return Result<CoreDiagnostics>.Failure(new CoreError(
                CoreErrorCodes.HostPlatformRequired,
                "Diagnostics need an explicit platform composition; the caller supplies CorePlatform."));
        }

        var resolved = options.Platform.Paths.Resolve();

        return resolved.TryGetError(out var error)
            ? Result<CoreDiagnostics>.Failure(error)
            : Create(resolved.Value, options);
    }

    /// <summary>
    /// Creates the three sinks below an already resolved state home; the host uses
    /// this overload so it resolves the XDG paths once for its own needs.
    /// </summary>
    /// <param name="directories">The resolved base directories.</param>
    /// <param name="options">The identity, the retention sizes and the platform composition.</param>
    /// <returns>The diagnostics, or a typed failure when a path or an option is invalid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="directories"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static Result<CoreDiagnostics> Create(PathSet directories, CoreHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(directories);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Platform?.HostServices is not { } services)
        {
            return Result<CoreDiagnostics>.Failure(new CoreError(
                CoreErrorCodes.HostPlatformRequired,
                "Diagnostics need an explicit platform composition; the caller supplies CorePlatform."));
        }

        if (options.LogMaxBytes <= 0 || options.RetainedFiles <= 0 || options.CrashMaximumLines <= 0)
        {
            return Result<CoreDiagnostics>.Failure(new CoreError(
                CoreErrorCodes.HostInvalidOptions,
                "The log size, the retained archive count and the crash line count must be positive."));
        }

        var paths = ApplicationPaths.Create(directories, options.Vendor, options.Application);

        if (paths.TryGetError(out var pathError))
        {
            return Result<CoreDiagnostics>.Failure(pathError);
        }

        IApplicationLogger? logger = null;
        IOperationJournal? journal = null;
        ICrashLog? crashLog = null;

        try
        {
            services.PrepareStateDirectory(paths.Value.StateDirectory);

            logger = services.CreateLogger(
                paths.Value.ApplicationLogFile,
                options.LogMaxBytes,
                options.RetainedFiles);
            journal = services.CreateJournal(
                paths.Value.OperationJournalFile,
                options.LogMaxBytes,
                options.RetainedFiles);
            crashLog = services.CreateCrashLog(
                paths.Value.CrashLogFile,
                options.LogMaxBytes,
                options.RetainedFiles,
                options.CrashMaximumLines);

            return Result<CoreDiagnostics>.Success(
                new CoreDiagnostics(directories, paths.Value, logger, journal, crashLog));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            PlatformNotSupportedException or ArgumentException)
        {
            DisposeAll(logger, journal, crashLog);

            return Result<CoreDiagnostics>.Failure(new CoreError(
                CoreErrorCodes.HostStateUnwritable,
                $"The state directory '{PathRedaction.ForMessage(paths.Value.StateDirectory)}' could not be created."));
        }
    }

    /// <summary>Writes one unhandled exception to the crash log and to the application log.</summary>
    /// <param name="exception">The exception.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Both sinks receive the failure: the crash log keeps the stack for the last
    /// chance, the application log is the trail an operator reads. The message line
    /// is redacted the same way in both.
    /// </remarks>
    public void Report(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        _crashLog.Write(exception, "application");
        _logger.Log(
            LogLevel.Error,
            string.Concat(
                "unhandled exception: ",
                LogRedaction.SingleLine(
                    PathRedaction.Scrub(SecretRedaction.Scrub(exception.Message)), 2_048)));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

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
                _crashLog.Dispose();
            }
        }
    }

    private static void DisposeAll(
        IApplicationLogger? logger,
        IOperationJournal? journal,
        ICrashLog? crashLog)
    {
        // WHY: the cleanup runs after a failed creation, so each sink is released
        // even when one of them throws while being disposed.
        try
        {
            journal?.Dispose();
        }
        finally
        {
            try
            {
                logger?.Dispose();
            }
            finally
            {
                crashLog?.Dispose();
            }
        }
    }
}
