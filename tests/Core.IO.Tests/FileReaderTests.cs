// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform.Linux;
using Core.IO.Files;
using Core.Platform;

namespace Core.IO.Tests;

public sealed class FileReaderTests
{
    private static readonly IFileSystem FileSystem = new LinuxFileSystemAdapter();

    [Fact]
    public void OpenReadsPinnedMetadata()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("abc.txt", "abc");
        var info = new FileInfo(path);
        var opened = FileReader.Open(FileSystem, path);

        Assert.True(opened.IsSuccess);

        using var reader = opened.Value;

        Assert.Equal(info.Length, reader.Metadata.Size);
        Assert.Equal(FileType.RegularFile, reader.Metadata.Type);
        Assert.True(reader.Metadata.Inode > 0);
        Assert.False(reader.Handle.IsInvalid);
        Assert.Equal(
            new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
            reader.Metadata.MtimeNanoseconds / 1_000_000_000L);

        Assert.True(LinuxFileSystem.TryStat(path, out var status));
        Assert.Equal(status.Device, reader.Metadata.Device);
        Assert.Equal(status.Inode, reader.Metadata.Inode);
    }

    [Fact]
    public void OpenFailsForAMissingFile()
    {
        using var temp = new TempDirectory();
        var opened = FileReader.Open(FileSystem, temp.FullPath("missing"));

        Assert.True(opened.IsFailure);
        Assert.Equal("io.unreadable", opened.Error.Code);
    }

    [Fact]
    public void OpenFailsForADirectory()
    {
        using var temp = new TempDirectory();
        var directory = temp.CreateDirectory("dir");

        Assert.True(FileReader.Open(FileSystem, directory).IsFailure);
    }

    [Fact]
    public void OpenFailsForAnUnauthorizedFile()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("secret.txt", "x");
        File.SetUnixFileMode(path, UnixFileMode.None);

        try
        {
            Assert.True(FileReader.Open(FileSystem, path).IsFailure);
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void OpenNoFollow_reads_a_regular_file_and_refuses_a_symbolic_link()
    {
        using var temp = new TempDirectory();
        var target = temp.WriteFile("target.txt", "abc");
        var link = temp.CreateFileSymlink("link.txt", target);

        var regular = FileReader.OpenNoFollow(FileSystem, target);
        Assert.True(regular.IsSuccess);
        regular.Value.Dispose();

        var refused = FileReader.OpenNoFollow(FileSystem, link);
        Assert.True(refused.IsFailure);
        Assert.Equal("io.unreadable", refused.Error.Code);
    }

    [Fact]
    public void OpenWithAPreReadStatusSkipsThePrefilter()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("abc.txt", "abc");

        Assert.True(FileSystem.TryStat(path, out var status));

        var opened = FileReader.Open(FileSystem, path, status);

        Assert.True(opened.IsSuccess);
        opened.Value.Dispose();
    }
}
