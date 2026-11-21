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

        if (!Directory.Exists(path))
        {
            if (File.Exists(path) || new FileInfo(path).LinkTarget is not null)
            {
                throw new IOException("The state path is not a directory.");
            }

            CreateOwnerOnly(path);
            return;
        }

        // WHY: Directory.Exists follows the link, so a dangling or redirected state
        // directory would write the logs wherever the link points.
        if (new FileInfo(path).LinkTarget is not null)
        {
            throw new IOException("The state directory is a symbolic link.");
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

    // WHY: Directory.CreateDirectory applies the process umask, which can leave a
    // newly created <state>/<vendor> group-writable; every component we create is
    // tightened to 0700, so a group member cannot replace the application directory.
    private static void CreateOwnerOnly(string path)
    {
        var missing = new Stack<string>();
        var current = path;

        while (!string.IsNullOrEmpty(current) && !Directory.Exists(current))
        {
            missing.Push(current);
            current = Path.GetDirectoryName(current);
        }

        while (missing.Count > 0)
        {
            var directory = missing.Pop();
            Directory.CreateDirectory(directory);

            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(
                    directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }
}
