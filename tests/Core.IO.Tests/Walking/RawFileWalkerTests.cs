// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;
using Core.IO.Walking;
using Core.Platform.Linux;

namespace Core.IO.Tests;

public sealed class RawFileWalkerTests
{
    [Fact]
    public void It_reports_the_same_regular_files_as_the_string_walker()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("b.txt", "b");
        temp.WriteFile("a.txt", "a");
        temp.WriteFile("c/d.txt", "d");
        temp.WriteFile("e.txt", "e");

        var expected = Files(new DirectoryWalker(new WalkOptions([], [], 0, followSymbolicLinks: false), new LinuxFileSystemAdapter()).Walk(temp.Path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var actual = new List<string>();
        new RawFileWalker().Walk(
            temp.Path,
            (relativePath, _, _, _) => actual.Add(Encoding.UTF8.GetString(relativePath)));

        Assert.Equal(expected, actual.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void It_reports_a_symbolic_link_to_the_skip_handler()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("target.txt", "target");
        _ = temp.WriteFile("link.txt", "link");
        File.Delete(temp.FullPath("link.txt"));
        File.CreateSymbolicLink(temp.FullPath("link.txt"), temp.FullPath("target.txt"));

        var files = new List<string>();
        var skipped = new List<(string Path, WalkSkipReason Reason)>();
        new RawFileWalker().Walk(
            temp.Path,
            (relativePath, _, _, _) => files.Add(Encoding.UTF8.GetString(relativePath)),
            (relativePath, reason) => skipped.Add((Encoding.UTF8.GetString(relativePath), reason)));

        Assert.Contains("target.txt", files);
        Assert.DoesNotContain("link.txt", files);
        Assert.Contains(skipped, item => item.Path == "link.txt" && item.Reason == WalkSkipReason.SymbolicLink);
    }

    [Fact]
    public void It_reports_a_root_file_with_no_parent_descriptor()
    {
        using var temp = new TempDirectory();
        var file = temp.WriteFile("root.txt", "root");
        var descriptor = 0;
        var path = string.Empty;

        new RawFileWalker().Walk(
            file,
            (relativePath, directoryDescriptor, _, _) =>
            {
                descriptor = directoryDescriptor;
                path = Encoding.UTF8.GetString(relativePath);
            });

        Assert.Equal(-1, descriptor);
        Assert.Equal("root.txt", path);
    }

    [Fact]
    public void It_applies_the_maximum_depth()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("a.txt", "a");
        temp.WriteFile("sub/b.txt", "b");

        var files = new List<string>();
        new RawFileWalker(maximumDepth: 1).Walk(
            temp.Path,
            (relativePath, _, _, _) => files.Add(Encoding.UTF8.GetString(relativePath)));

        Assert.Equal(["a.txt"], files);

        var deeper = new List<string>();
        new RawFileWalker(maximumDepth: 2).Walk(
            temp.Path,
            (relativePath, _, _, _) => deeper.Add(Encoding.UTF8.GetString(relativePath)));

        Assert.Equal(["a.txt", "sub/b.txt"], deeper.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_missing_root_is_a_skip()
    {
        using var temp = new TempDirectory();
        var skipped = new List<(string Path, WalkSkipReason Reason)>();
        var missing = temp.FullPath("missing");

        new RawFileWalker().Walk(
            missing,
            (_, _, _, _) => throw new InvalidOperationException("no file expected"),
            (relativePath, reason) => skipped.Add((Encoding.UTF8.GetString(relativePath), reason)));

        Assert.Contains(skipped, item => item.Path == "missing" && item.Reason == WalkSkipReason.Unreadable);
    }

    private static IEnumerable<string> Files(IEnumerable<WalkEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.IsFile)
            {
                yield return entry.RelativePath;
            }
        }
    }
}
