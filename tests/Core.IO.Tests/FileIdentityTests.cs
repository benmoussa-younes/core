// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;
using Core.IO.Files;
using Core.Platform.Linux;
using Core.IO.Walking;

namespace Core.IO.Tests;

public sealed class FileIdentityTests
{
    private static readonly LinuxFileSystemAdapter FileSystem = new();

    [Fact]
    public void The_walk_reports_the_file_identity()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("a.txt", "abc");
        var walker = new DirectoryWalker(new WalkOptions([], [], maximumDepth: 0, followSymbolicLinks: false), FileSystem);

        var file = walker.Walk(temp.Path).Single(entry => entry.IsFile);

        Assert.True(file.Identity.IsKnown);
        Assert.True(FileReader.Open(FileSystem, file.FullPath, file.Identity).IsSuccess);
    }
    [Fact]
    public void A_matching_identity_reads()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("a.txt", "abc");
        var identity = IdentityOf(path);
        var bytes = new List<byte>();

        var result = FileContent.Read(FileSystem, path, new byte[16], chunk => bytes.AddRange(chunk.ToArray()), identity);

        Assert.True(result.IsSuccess);
        Assert.Equal("abc", Encoding.ASCII.GetString(bytes.ToArray()));
    }

    [Fact]
    public void A_mismatched_identity_is_refused()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("a.txt", "abc");
        var identity = IdentityOf(path);
        var wrong = new FileIdentity(identity.Device, identity.Inode + 1);

        var read = FileContent.Read(FileSystem, path, new byte[16], _ => { }, wrong);
        Assert.Equal("io.unreadable", read.Error.Code);

        var opened = FileReader.Open(FileSystem, path, wrong);
        Assert.Equal("io.unreadable", opened.Error.Code);
    }

    [Fact]
    public void An_unknown_identity_skips_the_check()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("a.txt", "abc");

        Assert.True(FileContent.Read(FileSystem, path, new byte[16], _ => { }, FileIdentity.Unknown).IsSuccess);
        Assert.True(FileReader.Open(FileSystem, path, FileIdentity.Unknown).IsSuccess);
    }

    [Fact]
    public void A_zero_size_regular_file_is_still_verified()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("empty.txt", string.Empty);
        var identity = IdentityOf(path);
        var wrong = new FileIdentity(identity.Device, identity.Inode + 1);

        Assert.True(FileContent.Read(FileSystem, path, new byte[16], _ => { }, identity).IsSuccess);
        Assert.Equal("io.unreadable", FileContent.Read(FileSystem, path, new byte[16], _ => { }, wrong).Error.Code);
    }

    [Fact]
    public void A_swapped_path_is_refused()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("d");
        var path = temp.WriteFile("d/entry.txt", "one");
        var walked = IdentityOf(path);

        // Replace the file with a different one: a new inode, so the walked
        // identity no longer describes the bytes at the path.
        var other = temp.WriteFile("d/other.txt", "two");
        File.Move(other, path, overwrite: true);

        Assert.NotEqual(walked.Inode, IdentityOf(path).Inode);

        var result = FileContent.Read(FileSystem, path, new byte[16], _ => { }, walked);

        Assert.True(result.IsFailure);
        Assert.Equal("io.unreadable", result.Error.Code);
    }

    [Fact]
    public void A_directory_is_still_refused()
    {
        using var temp = new TempDirectory();
        var directory = temp.CreateDirectory("dir");

        var result = FileContent.Read(FileSystem, directory, new byte[16], _ => { }, FileIdentity.Unknown);

        Assert.Equal("io.unreadable", result.Error.Code);
    }

    [Fact]
    public void A_missing_path_with_an_identity_is_unreadable()
    {
        using var temp = new TempDirectory();

        Assert.Equal(
            "io.unreadable",
            FileReader.Open(FileSystem, temp.FullPath("missing"), FileIdentity.Unknown).Error.Code);
        Assert.Equal(
            "io.unreadable",
            FileContent.Read(FileSystem, temp.FullPath("missing"), new byte[16], _ => { }, FileIdentity.Unknown).Error.Code);
    }

    [Fact]
    public void An_unreadable_path_with_an_identity_is_unreadable()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("locked.txt", "x");
        File.SetUnixFileMode(path, UnixFileMode.None);

        try
        {
            Assert.Equal("io.unreadable", FileReader.Open(FileSystem, path, FileIdentity.Unknown).Error.Code);
            Assert.Equal("io.unreadable", FileContent.Read(FileSystem, path, new byte[16], _ => { }, FileIdentity.Unknown).Error.Code);
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void An_empty_read_buffer_is_rejected_with_an_identity()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("a.txt", "abc");

        Assert.Throws<ArgumentException>(
            () => FileContent.Read(FileSystem, path, [], _ => { }, FileIdentity.Unknown));
    }

    [Fact]
    public async Task A_fifo_is_refused_without_blocking()
    {
        using var temp = new TempDirectory();
        var path = temp.FullPath("pipe");
        RawFileSystem.MakeFifo(path, 0x1A4);

        var read = Task.Run(() => FileContent.Read(FileSystem, path, new byte[16], _ => { }, FileIdentity.Unknown));
        var completed = await Task.WhenAny(
            read,
            Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.True(completed == read, "Reading a FIFO with an identity blocked.");
        Assert.Equal("io.unreadable", (await read).Error.Code);
    }

    private static FileIdentity IdentityOf(string path)
    {
        Assert.True(LinuxFileSystem.TryStat(path, out var status));

        return new FileIdentity(status.Device, status.Inode);
    }
}
