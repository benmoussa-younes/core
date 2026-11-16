// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Diagnostics;
using Core.Execution;
using Core.Notifications;

namespace Core.Platform;

/// <summary>
/// The platform-owned services the UI host needs but must not construct itself:
/// durable diagnostics, the single-instance claim, the process seam, the link
/// launcher and the notifier. A product supplies one implementation through
/// <see cref="CorePlatform"/>.
/// </summary>
public interface ICoreHostServices
{
    /// <summary>Gets the process runner shared by the link launcher and the notifier.</summary>
    IProcessRunner Processes { get; }

    /// <summary>Gets the URL launcher.</summary>
    ISystemLinkLauncher Links { get; }

    /// <summary>Creates the durable application log.</summary>
    /// <param name="path">The log file path.</param>
    /// <param name="maxBytes">The size at which the file rotates; positive.</param>
    /// <param name="retainedFiles">How many rotated archives to keep; positive.</param>
    IApplicationLogger CreateLogger(string path, long maxBytes, int retainedFiles);

    /// <summary>Creates the operation journal.</summary>
    /// <param name="path">The journal file path.</param>
    /// <param name="maxBytes">The size at which the file rotates; positive.</param>
    /// <param name="retainedFiles">How many rotated archives to keep; positive.</param>
    IOperationJournal CreateJournal(string path, long maxBytes, int retainedFiles);

    /// <summary>Creates the last-chance crash log.</summary>
    /// <param name="path">The crash file path.</param>
    /// <param name="maxBytes">The size at which the file rotates; positive.</param>
    /// <param name="retainedFiles">How many rotated archives to keep; positive.</param>
    /// <param name="maximumLines">How many stack lines one entry keeps; positive.</param>
    ICrashLog CreateCrashLog(string path, long maxBytes, int retainedFiles, int maximumLines);

    /// <summary>Creates the single-instance claim.</summary>
    /// <param name="socketPath">The platform-owned claim path.</param>
    ISingleInstance CreateSingleInstance(string socketPath);

    /// <summary>Creates the desktop notifier for one application identity.</summary>
    /// <param name="application">The application path component.</param>
    ISystemNotifier CreateNotifier(string application);

    /// <summary>
    /// Creates the state directory when missing and applies the platform's
    /// owner-only mode. It throws the ordinary I/O exceptions on failure.
    /// </summary>
    /// <param name="path">The absolute state directory path.</param>
    void PrepareStateDirectory(string path);
}
