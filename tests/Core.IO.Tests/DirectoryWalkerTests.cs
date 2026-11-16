// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform.Linux;
using Core.Platform;
using Core.IO.Walking;
using Core.Primitives;

namespace Core.IO.Tests;

public sealed class DirectoryWalkerTests
{
    [Fact]
    public void WalksInOrdinalOrder()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("b.txt", "b");
        temp.WriteFile("a.txt", "a");
        temp.WriteFile("c/d.txt", "d");
        temp.WriteFile("e.txt", "e");

        Assert.Equal(["a.txt", "b.txt", "c/d.txt", "e.txt"], Files(Walk(temp.Path)));
    }

    [Fact]
    public void SingleFileRootYieldsItsNameAndPath()
    {
        using var temp = new TempDirectory();
        var file = temp.WriteFile("only.txt", "x");

        var entry = Assert.Single(Walk(file));

        Assert.Equal("only.txt", entry.RelativePath);
        Assert.Equal(file, entry.FullPath);
        Assert.Equal(WalkEntryKind.File, entry.Kind);
        Assert.Null(entry.SkipReason);
        Assert.True(entry.IsFile);
    }

    [Fact]
    public void IncludeFiltersFilesByPattern()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("a.txt", "a");
        temp.WriteFile("b.bin", "b");

        Assert.Equal(["a.txt"], Files(Walk(temp.Path, includes: ["*.txt"])));
    }

    [Fact]
    public void IncludeDoesNotPruneDirectories()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("sub/a.txt", "a");
        temp.WriteFile("other.txt", "o");

        Assert.Equal(["sub/a.txt"], Files(Walk(temp.Path, includes: ["sub/*.txt"])));
    }

    [Fact]
    public void ExcludePrunesDirectories()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("keep/a.txt", "a");
        temp.WriteFile("secret/b.txt", "b");

        Assert.Equal(["keep/a.txt"], Files(Walk(temp.Path, excludes: ["secret/**"])));
    }

    [Fact]
    public void ExcludeFiltersFiles()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("a.txt", "a");
        temp.WriteFile("b.tmp", "b");

        Assert.Equal(["a.txt"], Files(Walk(temp.Path, excludes: ["*.tmp"])));
    }

    [Fact]
    public void LevelsLimitFileDepth()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("top.txt", "top");
        temp.WriteFile("sub/nested.txt", "nested");
        temp.WriteFile("sub/deep/deeper.txt", "deeper");

        Assert.Equal(["top.txt"], Files(Walk(temp.Path, depth: 1)));
        Assert.Equal(["sub/nested.txt", "top.txt"], Files(Walk(temp.Path, depth: 2)));
        Assert.Equal(["sub/deep/deeper.txt", "sub/nested.txt", "top.txt"], Files(Walk(temp.Path, depth: 0)));
    }

    [Fact]
    public void SymlinksAreSkippedWhenNotFollowing()
    {
        using var temp = new TempDirectory();
        var target = temp.WriteFile("target.txt", "x");
        temp.CreateFileSymlink("link.txt", target);

        var entries = Walk(temp.Path).ToList();
        var skipped = Assert.Single(entries, entry => entry.Kind == WalkEntryKind.Skipped);

        Assert.Equal("link.txt", skipped.RelativePath);
        Assert.Equal(WalkSkipReason.SymbolicLink, skipped.SkipReason);
        Assert.False(skipped.IsFile);
        Assert.Equal(["target.txt"], Files(entries));
    }

    [Fact]
    public void FileSymlinksAreFollowedWhenRequested()
    {
        using var temp = new TempDirectory();
        var target = temp.WriteFile("target.txt", "x");
        temp.CreateFileSymlink("link.txt", target);

        Assert.Equal(["link.txt", "target.txt"], Files(Walk(temp.Path, follow: true)));
    }

    [Fact]
    public void DirectorySymlinksAreFollowedWhenRequested()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("real/a.txt", "a");
        temp.CreateDirectorySymlink("alias", temp.FullPath("real"));

        var files = Files(Walk(temp.Path, follow: true));

        Assert.Contains("alias/a.txt", files);
        Assert.Contains("real/a.txt", files);
    }

    [Fact]
    public void DirectoryCyclesAreSkippedWhenFollowing()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("a/data.txt", "d");
        temp.CreateDirectorySymlink("a/loop", temp.FullPath("a"));

        var entries = Walk(temp.Path, follow: true).ToList();
        var cycle = Assert.Single(entries, entry => entry.SkipReason == WalkSkipReason.Cycle);

        Assert.Equal("a/loop", cycle.RelativePath);
    }

    [Fact]
    public void ARepeatedDirectoryIdentityIsSkippedAsCycleWithoutFollowingLinks()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("data.txt", "d");
        using var handle = LinuxDirectoryHandle.Open(temp.Path).Value;
        Assert.True(handle.TryGetStatus(out var status));

        // The directory's own identity is already among its ancestors, which is
        // what a bind mount looks like: a plain directory, not a symbolic link.
        List<(ulong Device, ulong Inode)> ancestors = [(status.Device, status.Inode)];

        var entries = Walker()
            .WalkDirectory(handle, temp.Path, string.Empty, 0, ancestors)
            .ToList();

        var cycle = Assert.Single(entries);
        Assert.Equal(".", cycle.RelativePath);
        Assert.Equal(WalkSkipReason.Cycle, cycle.SkipReason);
    }

    [Fact]
    public void CyclesThroughASymlinkedDirectoryAreSkippedWhenFollowing()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("a/data.txt", "d");
        temp.CreateDirectorySymlink("b", temp.FullPath("a"));
        temp.CreateDirectorySymlink("a/loop", temp.FullPath("b"));

        var entries = Walk(temp.Path, follow: true).ToList();
        var cycles = entries
            .Where(entry => entry.SkipReason == WalkSkipReason.Cycle)
            .Select(entry => entry.RelativePath)
            .ToList();

        Assert.Equal(["a/loop", "b/loop"], cycles);
        Assert.DoesNotContain(
            entries,
            entry => entry.RelativePath.StartsWith("b/loop/", StringComparison.Ordinal));
        Assert.Equal(["a/data.txt", "b/data.txt"], Files(entries));
    }

    [Fact]
    public void RelativeDotDotUnderASymlinkedParentIsSkippedAsCycle()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("m/real/sub/f.txt", "f");
        temp.CreateDirectorySymlink("m/real/sub/up", "..");
        temp.CreateDirectorySymlink("m/link", "real");

        var entries = Walk(temp.FullPath("m"), follow: true).ToList();
        var cycles = entries
            .Where(entry => entry.SkipReason == WalkSkipReason.Cycle)
            .Select(entry => entry.RelativePath)
            .ToList();

        Assert.Equal(["link/sub/up", "real/sub/up"], cycles);
        Assert.Equal(["link/sub/f.txt", "real/sub/f.txt"], Files(entries));
        Assert.DoesNotContain(
            entries,
            entry => entry.RelativePath.StartsWith("link/sub/up/", StringComparison.Ordinal));
    }

    [Fact]
    public void BrokenSymlinkFallsThroughToAFile()
    {
        using var temp = new TempDirectory();
        temp.CreateFileSymlink("broken", temp.FullPath("missing"));

        var entry = Assert.Single(Walk(temp.Path, follow: true));

        Assert.Equal("broken", entry.RelativePath);
        Assert.True(entry.IsFile);
    }

    [Fact]
    public void MissingRootYieldsItsNameAsUnreadable()
    {
        using var temp = new TempDirectory();
        var path = temp.FullPath("missing");

        var entry = Assert.Single(Walk(path));

        Assert.Equal("missing", entry.RelativePath);
        Assert.Equal(path, entry.FullPath);
        Assert.Equal(WalkSkipReason.Unreadable, entry.SkipReason);
    }

    [Fact]
    public void DanglingSymlinkRootYieldsItsNameAsUnreadable()
    {
        using var temp = new TempDirectory();
        var path = temp.CreateFileSymlink("dangling", temp.FullPath("does-not-exist"));

        var entry = Assert.Single(Walk(path));

        Assert.Equal("dangling", entry.RelativePath);
        Assert.Equal(path, entry.FullPath);
        Assert.Equal(WalkSkipReason.Unreadable, entry.SkipReason);
    }

    [Fact]
    public void ANonDirectoryRootYieldsAFileEntry()
    {
        using var temp = new TempDirectory();
        var path = temp.FullPath("pipe");
        RawFileSystem.MakeFifo(path, 0x1A4);

        var entry = Assert.Single(Walk(path));

        Assert.Equal("pipe", entry.RelativePath);
        Assert.Equal(path, entry.FullPath);
        Assert.Equal(WalkEntryKind.File, entry.Kind);
        Assert.Null(entry.SkipReason);
        Assert.True(entry.IsFile);
    }

    [Fact]
    public void UnreadableRootYieldsDotSkipped()
    {
        using var temp = new TempDirectory();
        var directory = temp.CreateDirectory("locked");
        File.SetUnixFileMode(directory, UnixFileMode.None);

        try
        {
            var entry = Assert.Single(Walk(directory));

            Assert.Equal(".", entry.RelativePath);
            Assert.Equal(WalkSkipReason.Unreadable, entry.SkipReason);
        }
        finally
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void UnreadableSubdirectoryYieldsRelativeSkipped()
    {
        using var temp = new TempDirectory();
        var directory = temp.CreateDirectory("locked");
        temp.WriteFile("keep.txt", "k");
        File.SetUnixFileMode(directory, UnixFileMode.None);

        try
        {
            var entries = Walk(temp.Path).ToList();
            var skipped = Assert.Single(entries, entry => entry.Kind == WalkEntryKind.Skipped);

            Assert.Equal("locked", skipped.RelativePath);
            Assert.Equal(WalkSkipReason.Unreadable, skipped.SkipReason);
        }
        finally
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void FollowedSymlinkToAnUnreadableDirectoryYieldsUnreadable()
    {
        using var temp = new TempDirectory();
        var real = temp.CreateDirectory("real");
        temp.CreateDirectorySymlink("alias", real);
        File.SetUnixFileMode(real, UnixFileMode.None);

        try
        {
            var entries = Walk(temp.Path, follow: true).ToList();

            Assert.Contains(
                entries,
                entry => entry.RelativePath == "alias" && entry.SkipReason == WalkSkipReason.Unreadable);
        }
        finally
        {
            File.SetUnixFileMode(real, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void NonUtf8NamesAreSkipped()
    {
        using var temp = new TempDirectory();
        RawFileSystem.CreateFileWithRawName(temp.Path, [0xFF, (byte)'b', (byte)'a', (byte)'d']);

        var entry = Assert.Single(Walk(temp.Path));

        Assert.Equal(WalkSkipReason.NonUtf8, entry.SkipReason);
    }

    [Theory]
    [InlineData("/proc")]
    [InlineData("/proc/1")]
    [InlineData("/sys")]
    [InlineData("/dev")]
    [InlineData("/run")]
    [InlineData("/run/user")]
    public void RecognisesPseudoFileSystems(string path)
    {
        Assert.True(DirectoryWalker.IsPseudoFileSystem(path));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/home/proc")]
    [InlineData("/tmp/run")]
    [InlineData("/procfile")]
    [InlineData("/devices")]
    public void DoesNotMistakeLookalikePathsForPseudoFileSystems(string path)
    {
        Assert.False(DirectoryWalker.IsPseudoFileSystem(path));
    }

    [Fact]
    public void APseudoFileSystemRootIsSkippedByDefault()
    {
        var entry = Assert.Single(Walk("/proc"));

        Assert.Equal("proc", entry.RelativePath);
        Assert.Equal("/proc", entry.FullPath);
        Assert.Equal(WalkSkipReason.PseudoFilesystem, entry.SkipReason);
    }

    [Fact]
    public void APseudoFileSystemRootIsWalkedWhenRequested()
    {
        var entries = Walk("/proc", depth: 1, includePseudo: true).ToList();

        Assert.NotEmpty(entries);
        Assert.DoesNotContain(entries, entry => entry.SkipReason == WalkSkipReason.PseudoFilesystem);
    }

    [Fact]
    public void TopLevelPseudoFileSystemsAreSkippedByDefault()
    {
        var entries = Walk("/", depth: 1).ToList();
        var proc = Assert.Single(entries, entry => entry.RelativePath == "proc");

        Assert.Equal("/proc", proc.FullPath);
        Assert.Equal(WalkSkipReason.PseudoFilesystem, proc.SkipReason);
    }

    [Fact]
    public void TopLevelPseudoFileSystemsAreSkippedEvenWhenExcluded()
    {
        var entries = Walk("/", excludes: ["**"], depth: 1).ToList();
        var proc = Assert.Single(entries, entry => entry.RelativePath == "proc");

        Assert.Equal(WalkSkipReason.PseudoFilesystem, proc.SkipReason);
    }

    [Fact]
    public void TopLevelPseudoFileSystemsAreWalkedWhenRequested()
    {
        var entries = Walk("/", depth: 1, includePseudo: true).ToList();

        Assert.DoesNotContain(entries, entry => entry.SkipReason == WalkSkipReason.PseudoFilesystem);
    }

    [Fact]
    public void NullRootIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Walker().Walk(null!).ToList());
    }

    [Fact]
    public void EmptyRootIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Walker().Walk(string.Empty).ToList());
    }

    [Fact]
    public void ResolveTypePassesKnownTypesAndStatsUnknownOnes()
    {
        using var temp = new TempDirectory();
        var file = temp.WriteFile("f.txt", "x");
        temp.CreateDirectory("sub");
        using var handle = LinuxDirectoryHandle.Open(temp.Path).Value;

        Assert.Equal(
            FileType.Directory,
            DirectoryWalker.ResolveType(handle, "sub\0"u8, FileType.Directory));

        Assert.Equal(
            FileType.RegularFile,
            DirectoryWalker.ResolveType(handle, "f.txt\0"u8, FileType.Unknown));

        Assert.Equal(
            FileType.Directory,
            DirectoryWalker.ResolveType(handle, "sub\0"u8, FileType.Unknown));

        Assert.Equal(
            FileType.Unknown,
            DirectoryWalker.ResolveType(handle, "missing\0"u8, FileType.Unknown));
    }

    [Fact]
    public void AGetDentsFailureIsClassifiedAsUnreadable()
    {
        using var temp = new TempDirectory();
        var file = temp.WriteFile("file.txt", "x");
        var descriptor = RawFileSystem.OpenReadOnly(file);
        using var handle = LinuxDirectoryHandle.FromDescriptor(descriptor);

        var entries = Walker()
            .WalkDirectory(handle, temp.Path, "sub", 0, [])
            .ToList();

        var entry = Assert.Single(entries);
        Assert.Equal("sub", entry.RelativePath);
        Assert.Equal(WalkSkipReason.Unreadable, entry.SkipReason);
    }

    private static List<string> Files(IEnumerable<WalkEntry> entries)
    {
        var paths = new List<string>();

        foreach (var entry in entries)
        {
            if (entry.IsFile)
            {
                paths.Add(entry.RelativePath);
            }
        }

        return paths;
    }

    private static List<WalkEntry> Walk(
        string root,
        string[]? includes = null,
        string[]? excludes = null,
        int depth = 0,
        bool follow = false,
        bool includePseudo = false)
    {
        return Walker(includes, excludes, depth, follow, includePseudo).Walk(root).ToList();
    }

    private static DirectoryWalker Walker(
        string[]? includes = null,
        string[]? excludes = null,
        int depth = 0,
        bool follow = false,
        bool includePseudo = false)
    {
        return new DirectoryWalker(new WalkOptions(Compile(includes), Compile(excludes), depth, follow, includePseudo), new LinuxFileSystemAdapter());
    }

    private static PathPattern[] Compile(string[]? patterns)
    {
        if (patterns is null)
        {
            return [];
        }

        var compiled = new PathPattern[patterns.Length];

        for (var index = 0; index < patterns.Length; index++)
        {
            compiled[index] = PathPattern.Create(patterns[index]).Value;
        }

        return compiled;
    }
}
