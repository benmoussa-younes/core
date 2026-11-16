// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform.Linux;

namespace Core.Platform.Linux.Tests;

public sealed class LinuxDirectoryHandleTests
{
    [Fact]
    public void OpenReturnsAnOpenDescriptor()
    {
        using var temp = new TempDirectory();
        var opened = LinuxDirectoryHandle.Open(temp.Path);

        Assert.True(opened.IsSuccess);

        using var handle = opened.Value;
        Assert.True(handle.FileDescriptor >= 0);
        Assert.True(handle.TryGetStatus(out var status));
        Assert.Equal(LinuxFileType.Directory, status.Type);
        Assert.True(status.Inode > 0);
    }

    [Fact]
    public void OpenFailsForAMissingDirectory()
    {
        using var temp = new TempDirectory();
        var opened = LinuxDirectoryHandle.Open(temp.FullPath("missing"));

        Assert.True(opened.IsFailure);
        Assert.Equal("platform.directory_open_failed", opened.Error.Code);
    }

    [Fact]
    public void OpenFailsForARegularFile()
    {
        using var temp = new TempDirectory();
        var file = temp.WriteFile("file.txt", "x");

        Assert.True(LinuxDirectoryHandle.Open(file).IsFailure);
    }

    [Fact]
    public void TryOpenAtOpensAChildDirectory()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("sub");
        using var handle = LinuxDirectoryHandle.Open(temp.Path).Value;

        Assert.True(handle.TryOpenAt("sub\0"u8, followSymbolicLinks: false, out var child));

        using (child)
        {
            Assert.True(child!.TryGetStatus(out var status));
            Assert.Equal(LinuxFileType.Directory, status.Type);
        }
    }

    [Fact]
    public void TryOpenAtRefusesAFileAndAnEmptyName()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("file.txt", "x");
        using var handle = LinuxDirectoryHandle.Open(temp.Path).Value;

        Assert.False(handle.TryOpenAt("file.txt\0"u8, followSymbolicLinks: false, out var child));
        Assert.Null(child);
        Assert.False(handle.TryOpenAt([], followSymbolicLinks: false, out _));
    }

    [Fact]
    public void TryOpenAtHonoursTheFollowFlagForASymbolicLink()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("real");
        temp.CreateDirectorySymlink("alias", temp.FullPath("real"));
        using var handle = LinuxDirectoryHandle.Open(temp.Path).Value;

        Assert.True(handle.TryOpenAt("alias\0"u8, followSymbolicLinks: true, out var followed));
        followed!.Dispose();
        Assert.False(handle.TryOpenAt("alias\0"u8, followSymbolicLinks: false, out _));
    }

    [Fact]
    public void EnumerateReadsTheDirectoryEntriesAndThenEnds()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("a.txt", "a");
        temp.CreateDirectory("sub");
        using var handle = LinuxDirectoryHandle.Open(temp.Path).Value;
        using var reader = handle.Enumerate();
        var names = new List<string>();

        while (reader.TryRead(out var entry))
        {
            names.Add(entry.Name);
        }

        Assert.Contains("a.txt", names);
        Assert.Contains("sub", names);
        Assert.False(reader.TryRead(out _));
    }

    [Fact]
    public void ReaderThrowsWhenTheDescriptorIsClosed()
    {
        using var temp = new TempDirectory();
        var handle = LinuxDirectoryHandle.Open(temp.Path).Value;
        using var reader = handle.Enumerate();
        handle.Dispose();

        // Force the descriptor number to be reused by another directory: the
        // reader must refuse a disposed handle deterministically instead of
        // calling getdents64 on a descriptor that now belongs to someone else.
        using var reopened = LinuxDirectoryHandle.Open(temp.Path).Value;

        Assert.Throws<IOException>(() => reader.TryRead(out _));
    }

    [Fact]
    public void TryReadAfterTheReaderIsDisposedThrows()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("a.txt", "a");
        temp.CreateDirectory("sub");
        using var handle = LinuxDirectoryHandle.Open(temp.Path).Value;
        var reader = handle.Enumerate();

        // Read one entry so the pooled buffer still holds more records than
        // _offset; that is the path that built a span over the freed buffer.
        Assert.True(reader.TryRead(out _));
        reader.Dispose();

        Assert.Throws<ObjectDisposedException>(() => reader.TryRead(out _));
        reader.Dispose();
    }

    [Fact]
    public void UsingADisposedHandleThrows()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("sub");
        var handle = LinuxDirectoryHandle.Open(temp.Path).Value;
        handle.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = handle.FileDescriptor);
        Assert.Throws<ObjectDisposedException>(() => handle.TryGetStatus(out _));
        Assert.Throws<ObjectDisposedException>(
            () => handle.TryOpenAt("sub\0"u8, followSymbolicLinks: false, out _));
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        using var temp = new TempDirectory();
        var handle = LinuxDirectoryHandle.Open(temp.Path).Value;
        handle.Dispose();
        handle.Dispose();
    }

    [Fact]
    public void DirectoryEntryExposesItsFields()
    {
        var entry = new LinuxDirectoryEntry("name", LinuxFileType.Socket, isNameValidUtf8: false);

        Assert.Equal("name", entry.Name);
        Assert.Equal(LinuxFileType.Socket, entry.Type);
        Assert.False(entry.IsNameValidUtf8);
    }

    [Fact]
    public void FileStatusExposesItsFields()
    {
        var status = new LinuxFileStatus(LinuxFileType.Fifo, 12, 34, 56, 78, 90, 91, 92);

        Assert.Equal(LinuxFileType.Fifo, status.Type);
        Assert.Equal(12, status.Size);
        Assert.Equal(34, status.MtimeNanoseconds);
        Assert.Equal(56ul, status.Device);
        Assert.Equal(78ul, status.Inode);
        Assert.Equal(90u, status.OwnerUserId);
        Assert.Equal(91u, status.OwnerGroupId);
        Assert.Equal(92u, status.Mode);
    }
}
