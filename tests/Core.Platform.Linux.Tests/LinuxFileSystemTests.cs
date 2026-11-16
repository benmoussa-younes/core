// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;
using Core.Platform.Linux;


namespace Core.Platform.Linux.Tests;

public sealed class LinuxFileSystemTests
{
    [Fact]
    public void TryStatReadsPinnedSizeMtimeAndType()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("abc.txt", "abc");
        var info = new FileInfo(path);

        Assert.True(LinuxFileSystem.TryStat(path, out var status));
        Assert.Equal(LinuxFileType.RegularFile, status.Type);
        Assert.Equal(info.Length, status.Size);
        Assert.Equal(
            new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
            status.MtimeNanoseconds / 1_000_000_000L);
        Assert.True(status.Inode > 0);
    }

    [Fact]
    public void TryStatAcceptsARawNullTerminatedPath()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("raw.txt", "raw");

        Assert.True(LinuxFileSystem.TryStat(Encoding.UTF8.GetBytes(path + "\0"), out var status));
        Assert.Equal(LinuxFileType.RegularFile, status.Type);
    }

    [Fact]
    public void TryStatFailsOnAMissingPathAndReportsErrno()
    {
        using var temp = new TempDirectory();

        Assert.False(LinuxFileSystem.TryStat(temp.FullPath("missing"), out _));
        Assert.Equal(LinuxErrno.NoEntry, LinuxFileSystem.LastError);
    }

    [Fact]
    public void TryStatAtFollowsOrRefusesTheFinalLink()
    {
        using var temp = new TempDirectory();
        var file = temp.WriteFile("target.txt", "x");
        temp.CreateFileSymlink("link.txt", file);
        temp.CreateDirectory("sub");
        using var handle = LinuxDirectoryHandle.Open(temp.Path).Value;

        Assert.True(LinuxFileSystem.TryStatAt(handle, "link.txt\0"u8, followSymbolicLinks: false, out var link));
        Assert.Equal(LinuxFileType.SymbolicLink, link.Type);

        Assert.True(LinuxFileSystem.TryStatAt(handle, "link.txt\0"u8, followSymbolicLinks: true, out var followed));
        Assert.Equal(LinuxFileType.RegularFile, followed.Type);

        Assert.True(LinuxFileSystem.TryStatAt(handle, "sub\0"u8, followSymbolicLinks: false, out var directory));
        Assert.Equal(LinuxFileType.Directory, directory.Type);

        Assert.False(LinuxFileSystem.TryStatAt(handle, "missing\0"u8, followSymbolicLinks: false, out _));
    }

    [Fact]
    public void TryStatDescriptorReadsTheOpenDirectory()
    {
        using var temp = new TempDirectory();
        using var handle = LinuxDirectoryHandle.Open(temp.Path).Value;

        Assert.True(LinuxFileSystem.TryStatDescriptor(handle.FileDescriptor, out var status));
        Assert.Equal(LinuxFileType.Directory, status.Type);
        Assert.False(LinuxFileSystem.TryStatDescriptor(-1, out _));
    }

    [Fact]
    public void TryAccessReportsReadability()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("readable.txt", "x");

        Assert.True(LinuxFileSystem.TryAccess(path, LinuxAccessMode.Read));
        Assert.True(LinuxFileSystem.TryAccess(Encoding.UTF8.GetBytes(path + "\0"), LinuxAccessMode.Read));
        Assert.False(LinuxFileSystem.TryAccess(temp.FullPath("missing"), LinuxAccessMode.Read));
    }

    [Fact]
    public void TryAccessRefusesAnUnauthorizedPath()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("secret.txt", "x");
        File.SetUnixFileMode(path, UnixFileMode.None);

        try
        {
            Assert.False(LinuxFileSystem.TryAccess(path, LinuxAccessMode.Read));
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void TryReadAtReadsARangeWithoutMovingThePosition()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("data", "abcdef");
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read);
        var descriptor = (int)handle.DangerousGetHandle();
        Span<byte> buffer = stackalloc byte[8];

        Assert.Equal(6, LinuxFileSystem.TryReadAt(descriptor, buffer, 0));
        Assert.Equal("abcdef", Encoding.ASCII.GetString(buffer[..6]));

        Assert.Equal(4, LinuxFileSystem.TryReadAt(descriptor, buffer, 2));
        Assert.Equal("cdef", Encoding.ASCII.GetString(buffer[..4]));

        Assert.Equal(0, LinuxFileSystem.TryReadAt(descriptor, Span<byte>.Empty, 0));
        Assert.Equal(-1, LinuxFileSystem.TryReadAt(-1, buffer, 0));
    }

    [Fact]
    public void TryWriteWritesToADescriptor()
    {
        using var temp = new TempDirectory();
        var path = temp.FullPath("out.bin");
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        var descriptor = (int)stream.SafeFileHandle.DangerousGetHandle();

        Assert.Equal(3, LinuxFileSystem.TryWrite(descriptor, "abc"u8));
        Assert.Equal(0, LinuxFileSystem.TryWrite(descriptor, ReadOnlySpan<byte>.Empty));
        Assert.Equal(-1, LinuxFileSystem.TryWrite(-1, "x"u8));

        stream.Flush();
        Assert.Equal("abc", File.ReadAllText(path));
    }

    [Fact]
    public void TryGetFileStatusFlagsReadsTheAccessMode()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("data", "x");

        using var read = File.OpenHandle(path, FileMode.Open, FileAccess.Read);

        Assert.True(LinuxFileSystem.TryGetFileStatusFlags((int)read.DangerousGetHandle(), out var readFlags));
        Assert.Equal(0, readFlags & 3);

        using var write = File.OpenHandle(path, FileMode.Open, FileAccess.Write);

        Assert.True(LinuxFileSystem.TryGetFileStatusFlags((int)write.DangerousGetHandle(), out var writeFlags));
        Assert.Equal(1, writeFlags & 3);

        Assert.False(LinuxFileSystem.TryGetFileStatusFlags(-1, out _));
    }

    [Fact]
    public void TryAdviseAcceptsSequentialAdvice()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("data", "abcdef");
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read);

        Assert.True(LinuxFileSystem.TryAdvise((int)handle.DangerousGetHandle(), 0, 0, LinuxFileAdvice.Sequential));
    }

    [Fact]
    public void TrySyncDirectoryFlushesARealDirectory()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("data", "abcdef");

        Assert.True(LinuxFileSystem.TrySyncDirectory(temp.Path));
    }

    [Fact]
    public void TrySyncDirectoryReportsAMissingDirectory()
    {
        using var temp = new TempDirectory();

        Assert.False(LinuxFileSystem.TrySyncDirectory(temp.FullPath("missing")));
    }

    [Fact]
    public void TrySyncDirectoryReportsAFilePath()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("file.txt", "x");

        // Opening a regular file with O_DIRECTORY fails with ENOTDIR.
        Assert.False(LinuxFileSystem.TrySyncDirectory(path));
    }

    [Fact]
    public void TrySyncDescriptorFlushesAnOpenDirectory()
    {
        using var temp = new TempDirectory();
        using var handle = LinuxDirectoryHandle.Open(temp.Path).Value;

        Assert.True(LinuxFileSystem.TrySyncDescriptor(handle.FileDescriptor));
        Assert.False(LinuxFileSystem.TrySyncDescriptor(-1));
    }

    [Fact]
    public void FStatAtFallbackReadsTheSameStatus()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("fallback.txt", "fallback");

        Assert.True(LinuxFileSystem.TryStatWithFallback(path, out var status));
        Assert.Equal(LinuxFileType.RegularFile, status.Type);
        Assert.Equal(8, status.Size);
    }

    [Fact]
    public void FStatAtFallbackReportsAMissingPath()
    {
        using var temp = new TempDirectory();

        Assert.False(LinuxFileSystem.TryStatWithFallback(temp.FullPath("missing"), out _));
    }

    [Fact]
    public void ForcingTheFallbackLeavesTheProcessWideAvailabilityUnchanged()
    {
        var before = LinuxFileSystem.StatxAvailable;

        Assert.False(LinuxFileSystem.TryStatWithFallback("/definitely-missing", out _));

        Assert.Equal(before, LinuxFileSystem.StatxAvailable);
    }

    [Fact]
    public void TryStatDescriptorRejectsAnInvalidDescriptor()
    {
        Assert.False(LinuxFileSystem.TryStatDescriptor(-1, out _));
    }
}
