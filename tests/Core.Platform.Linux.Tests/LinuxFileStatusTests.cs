// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Diagnostics;
using Core.Platform.Linux;


namespace Core.Platform.Linux.Tests;

public sealed class LinuxFileStatusTests
{
    private static readonly string? StatBinary = ResolveStatBinary();

    /// <summary>A symbolic link's own mode is always 0777 on Linux.</summary>
    private const uint SymbolicLinkMode = 0x1FF;

    [Theory]
    [InlineData("640")]
    [InlineData("755")]
    [InlineData("440")]
    [InlineData("1777")]
    [InlineData("4755")]
    public void TryStatMatchesTheStatOracleOnPermissionsAndOwnership(string octalMode)
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("oracle.txt", "x");
        var mode = (UnixFileMode)Convert.ToInt32(octalMode, 8);
        File.SetUnixFileMode(path, mode);

        var expected = StatOracle(path);

        Assert.True(LinuxFileSystem.TryStat(path, out var status));
        Assert.Equal(expected.UserId, status.OwnerUserId);
        Assert.Equal(expected.GroupId, status.OwnerGroupId);
        Assert.Equal(expected.Mode, status.Mode);
        Assert.Equal(LinuxFileType.RegularFile, status.Type);
    }

    [Fact]
    public void TryStatMatchesTheStatOracleForADirectory()
    {
        using var temp = new TempDirectory();
        var path = temp.CreateDirectory("owned");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var expected = StatOracle(path);

        Assert.True(LinuxFileSystem.TryStat(path, out var status));
        Assert.Equal(expected.UserId, status.OwnerUserId);
        Assert.Equal(expected.GroupId, status.OwnerGroupId);
        Assert.Equal(expected.Mode, status.Mode);
        Assert.Equal(LinuxFileType.Directory, status.Type);
    }

    [Fact]
    public void FStatAtFallbackReadsTheSameOwnershipAndMode()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("fallback-owner.txt", "x");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        var expected = StatOracle(path);

        Assert.True(LinuxFileSystem.TryStatWithFallback(path, out var status));
        Assert.Equal(expected.UserId, status.OwnerUserId);
        Assert.Equal(expected.GroupId, status.OwnerGroupId);
        Assert.Equal(expected.Mode, status.Mode);
        Assert.Equal(0x1A0u, status.Mode);
    }

    [Fact]
    public void TryStatDescriptorReportsTheOwnershipAndModeOfTheOpenObject()
    {
        using var temp = new TempDirectory();
        var path = temp.CreateDirectory("descriptor");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var expected = StatOracle(path);

        using var handle = LinuxDirectoryHandle.Open(path).Value;

        Assert.True(handle.TryGetStatus(out var status));
        Assert.Equal(expected.UserId, status.OwnerUserId);
        Assert.Equal(expected.GroupId, status.OwnerGroupId);
        Assert.Equal(expected.Mode, status.Mode);
    }

    [Fact]
    public void NoFollowStatusReportsTheLinksOwnModeAndOwnerNotTheTargets()
    {
        using var temp = new TempDirectory();
        var target = temp.WriteFile("target.txt", "x");
        File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        temp.CreateFileSymlink("link.txt", target);
        var expected = StatOracle(target);

        using var handle = LinuxDirectoryHandle.Open(temp.Path).Value;

        // No-follow (lstat semantics, pinned to the parent descriptor) sees the
        // link itself: its own type, its own 0777 mode and the link's owner.
        Assert.True(LinuxFileSystem.TryStatAt(handle, "link.txt\0"u8, followSymbolicLinks: false, out var link));
        Assert.Equal(LinuxFileType.SymbolicLink, link.Type);
        Assert.Equal(SymbolicLinkMode, link.Mode);
        Assert.Equal(expected.UserId, link.OwnerUserId);
        Assert.Equal(expected.GroupId, link.OwnerGroupId);

        // Following the link yields the target's status instead.
        Assert.True(LinuxFileSystem.TryStatAt(handle, "link.txt\0"u8, followSymbolicLinks: true, out var followed));
        Assert.Equal(LinuxFileType.RegularFile, followed.Type);
        Assert.Equal(expected.Mode, followed.Mode);
        Assert.Equal(expected.UserId, followed.OwnerUserId);
    }

    private static string? ResolveStatBinary()
    {
        foreach (var candidate in new[] { "/usr/bin/stat", "/bin/stat" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static (uint UserId, uint GroupId, uint Mode) StatOracle(string path)
    {
        var stat = StatBinary;

        if (stat is null)
        {
            // The oracle is unavailable on this host; skip rather than fail.
            Assert.Skip("The 'stat' binary was not found under /usr/bin or /bin.");
            return default;
        }

        var startInfo = new ProcessStartInfo(stat)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("%u %g %a");
        startInfo.ArgumentList.Add(path);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);

        var fields = output.Split(' ');

        return (uint.Parse(fields[0]), uint.Parse(fields[1]), Convert.ToUInt32(fields[2], 8));
    }
}
