// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.Versioning;
using Core.Diagnostics;
using Core.Execution;
using Core.Notifications;

namespace Core.Platform.Linux;

/// <summary>
/// The Linux host services: owner-only rotated files, a Unix-domain single
/// instance claim and the system process seam.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxCoreHostServices : ICoreHostServices
{
    private readonly SystemProcessRunner _processes = new();
    private readonly SystemLinkLauncher _links;

    /// <summary>Initializes a new instance of the <see cref="LinuxCoreHostServices"/> class.</summary>
    public LinuxCoreHostServices()
    {
        _links = new SystemLinkLauncher(_processes);
    }

    /// <inheritdoc />
    public IProcessRunner Processes => _processes;

    /// <inheritdoc />
    public ISystemLinkLauncher Links => _links;

    /// <inheritdoc />
    public IApplicationLogger CreateLogger(string path, long maxBytes, int retainedFiles) =>
        new RotatingFileLogger(path, maxBytes, retainedFiles);

    /// <inheritdoc />
    public IOperationJournal CreateJournal(string path, long maxBytes, int retainedFiles) =>
        new RotatingOperationJournal(path, maxBytes, retainedFiles);

    /// <inheritdoc />
    public ICrashLog CreateCrashLog(string path, long maxBytes, int retainedFiles, int maximumLines) =>
        new CrashLog(path, maxBytes, retainedFiles, maximumLines);

    /// <inheritdoc />
    public ISingleInstance CreateSingleInstance(string socketPath) => new SingleInstanceLock(socketPath);

    /// <inheritdoc />
    public ISystemNotifier CreateNotifier(string application) => new NotifySendNotifier(_processes, application);

    /// <inheritdoc />
    public void PrepareStateDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var created = !Directory.Exists(path);
        Directory.CreateDirectory(path);

        if (created)
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return;
        }

        // A pre-existing state directory must not let another local user replace
        // a settings/log file. Read/execute for group/other is fine; write is not.
        if (LinuxFileSystem.TryStat(path, out var status) &&
            status.Type == LinuxFileType.Directory &&
            (status.Mode & 0x12) != 0)
        {
            throw new IOException("The state directory is group- or world-writable.");
        }
    }
}
