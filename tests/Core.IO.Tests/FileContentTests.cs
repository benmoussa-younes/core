// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;
using Core.Platform;
using Core.Platform.Linux;
using Microsoft.Win32.SafeHandles;
using Core.IO.Files;


namespace Core.IO.Tests;

public sealed class FileContentTests
{
    private static readonly IFileSystem FileSystem = new LinuxFileSystemAdapter();

    [Fact]
    public void ReadsAFileIntoTheSink()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("abc.txt", "abc");
        var bytes = new List<byte>();

        var result = FileContent.Read(FileSystem, path, new byte[16], chunk => bytes.AddRange(chunk.ToArray()));

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Size);
        Assert.Equal("abc", Encoding.ASCII.GetString(bytes.ToArray()));
    }

    [Fact]
    public void ReadsAcrossBufferBoundaries()
    {
        using var temp = new TempDirectory();
        var content = new byte[35];
        var path = temp.WriteBytes("large", content);
        var bytes = new List<byte>();

        var result = FileContent.Read(FileSystem, path, new byte[16], chunk => bytes.AddRange(chunk.ToArray()));

        Assert.True(result.IsSuccess);
        Assert.Equal(content.Length, result.Value.Size);
        Assert.Equal(content, bytes.ToArray());
    }

    [Fact]
    public void ZeroByteFileNeverCallsTheSink()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("empty", string.Empty);

        var result = FileContent.Read(FileSystem, path, new byte[16], _ => throw new InvalidOperationException("sink called"));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Size);
    }

    [Fact]
    public async Task ZeroByteFifoIsUnreadableWithoutBlocking()
    {
        using var temp = new TempDirectory();
        var path = temp.FullPath("pipe");
        RawFileSystem.MakeFifo(path, 0x1A4);

        var read = Task.Run(() => FileContent.Read(FileSystem, path, new byte[16], _ => { }));
        var completed = await Task.WhenAny(
            read,
            Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.True(completed == read, "Reading a zero-byte FIFO blocked.");
        Assert.Equal("io.unreadable", (await read).Error.Code);
    }

    [Fact]
    public void MissingFileIsUnreadable()
    {
        using var temp = new TempDirectory();

        var result = FileContent.Read(FileSystem, temp.FullPath("missing"), new byte[16], _ => { });

        Assert.True(result.IsFailure);
        Assert.Equal("io.unreadable", result.Error.Code);
    }

    [Fact]
    public void UnauthorizedFileIsUnreadable()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("secret.txt", "x");
        File.SetUnixFileMode(path, UnixFileMode.None);

        try
        {
            Assert.True(FileContent.Read(FileSystem, path, new byte[16], _ => { }).IsFailure);
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void DirectoryIsUnreadable()
    {
        using var temp = new TempDirectory();
        var directory = temp.CreateDirectory("dir");

        var result = FileContent.Read(FileSystem, directory, new byte[16], _ => { });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void AZeroSizeDirectoryIsUnreadable()
    {
        // A directory on a file system that reports st_size == 0 (procfs, btrfs)
        // must not pass the zero-size fast path as a successful empty read.
        var status = new FileStatus(FileType.Directory, 0, 0, 0, 0, 0, 0, 0);

        var result = FileContent.Read<OperatingSystemFileChunkReader>(FileSystem, "proc-like", new byte[16], _ => { }, status);

        Assert.Equal("io.unreadable", result.Error.Code);
    }

    [Fact]
    public void AnOpenFailureAfterTheProbeIsUnreadable()
    {
        // The path passed the probe but the open fails (it vanished, or a special
        // file refuses): that is io.unreadable, not a crash.
        var status = new FileStatus(FileType.RegularFile, 3, 0, 0, 0, 0, 0, 0);

        var result = FileContent.Read<OperatingSystemFileChunkReader>(FileSystem, "missing-after-probe", new byte[16], _ => { }, status);

        Assert.Equal("io.unreadable", result.Error.Code);
    }

    [Fact]
    public void NullAndEmptyBuffersAreRejected()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("abc.txt", "abc");

        Assert.Throws<ArgumentNullException>(() => FileContent.Read(FileSystem, path, null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => FileContent.Read(FileSystem, path, new byte[16], null!));
        Assert.Throws<ArgumentException>(() => FileContent.Read(FileSystem, path, [], _ => { }));
    }

    [Fact]
    public void AShrunkenFileIsUnreadableNotATruncatedSuccess()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("abc.txt", "abc");
        var pinned = new FileStatus(FileType.RegularFile, size: 100, mtimeNanoseconds: 0, device: 0, inode: 0, ownerUserId: 0, ownerGroupId: 0, mode: 0);

        var result = FileContent.Read<OperatingSystemFileChunkReader>(FileSystem, path, new byte[16], _ => { }, pinned);

        Assert.True(result.IsFailure);
        Assert.Equal("io.unreadable", result.Error.Code);
    }

    [Fact]
    public void AShortChunkReadIsUnreadableNotSuccess()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("attribute", "0123456789abcdef");
        Assert.True(FileSystem.TryStat(path, out var status));

        var result = FileContent.Read<AShortChunkReader>(FileSystem, path, new byte[8], _ => { }, status);

        Assert.True(result.IsFailure);
        Assert.Equal("io.unreadable", result.Error.Code);
    }

    [Fact]
    public void AZeroChunkReadBeforeThePinnedSizeIsUnreadable()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("attribute", "0123456789abcdef");
        Assert.True(FileSystem.TryStat(path, out var status));

        var result = FileContent.Read<AZeroChunkReader>(FileSystem, path, new byte[8], _ => { }, status);

        Assert.True(result.IsFailure);
        Assert.Equal("io.unreadable", result.Error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFailedChunkReadIsUnreadableNotAnException(bool unauthorized)
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("attribute", "payload");
        Assert.True(FileSystem.TryStat(path, out var status));

        var result = unauthorized
            ? FileContent.Read<UnauthorizedChunkReader>(FileSystem, path, new byte[16], _ => { }, status)
            : FileContent.Read<ThrowingChunkReader>(FileSystem, path, new byte[16], _ => { }, status);

        Assert.True(result.IsFailure);
        Assert.Equal("io.unreadable", result.Error.Code);
    }

    [Fact]
    public void AFailureAfterAPartialReadIsUnreadable()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("attribute", "0123456789abcdef");
        Assert.True(FileSystem.TryStat(path, out var status));
        var bytes = new List<byte>();

        var result = FileContent.Read<AFailureOnTheSecondChunkReader>(FileSystem, 
            path,
            new byte[8],
            chunk => bytes.AddRange(chunk.ToArray()),
            status);

        Assert.True(result.IsFailure);
        Assert.Equal("io.unreadable", result.Error.Code);
    }

    private readonly struct ThrowingChunkReader : IFileChunkReader
    {
        public static int Read(SafeFileHandle handle, Span<byte> buffer, long offset)
        {
            throw new IOException("Operation not permitted");
        }
    }

    private readonly struct UnauthorizedChunkReader : IFileChunkReader
    {
        public static int Read(SafeFileHandle handle, Span<byte> buffer, long offset)
        {
            throw new UnauthorizedAccessException("Access to the path is denied.");
        }
    }

    private readonly struct AFailureOnTheSecondChunkReader : IFileChunkReader
    {
        public static int Read(SafeFileHandle handle, Span<byte> buffer, long offset)
        {
            return offset == 0 ? RandomAccess.Read(handle, buffer, offset) : throw new IOException("Input/output error");
        }
    }

    private readonly struct AShortChunkReader : IFileChunkReader
    {
        public static int Read(SafeFileHandle handle, Span<byte> buffer, long offset)
        {
            // Fewer bytes than the pinned 8-byte chunk: never success.
            return 2;
        }
    }

    private readonly struct AZeroChunkReader : IFileChunkReader
    {
        public static int Read(SafeFileHandle handle, Span<byte> buffer, long offset)
        {
            // Zero before the pinned size is reached: never success.
            return 0;
        }
    }
}
