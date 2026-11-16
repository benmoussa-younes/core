// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.IO.Mapped;
using Core.Platform.Linux;

namespace Core.IO.Tests;

public sealed class MappedFileTests
{
    [Fact]
    public void It_maps_the_content_and_reads_slices()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("data.bin", [1, 2, 3, 4, 5]);

        var result = MappedFile.Open(path);
        Assert.True(result.IsSuccess);

        using var file = result.Value;
        Assert.Equal(5, file.Length);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, file.Slice(0, 5).ToArray());
        Assert.Equal(new byte[] { 2, 3 }, file.Slice(1, 2).ToArray());
        Assert.Equal(new byte[] { 5 }, file.Slice(4, 1).ToArray());
    }

    [Fact]
    public void It_maps_the_content_with_the_linux_factory()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("data.bin", [1, 2, 3, 4, 5]);

        var result = MappedFile.Open(path, new LinuxMemoryMappingFactory());
        Assert.True(result.IsSuccess);

        using var file = result.Value;
        Assert.Equal(5, file.Length);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, file.Slice(0, 5).ToArray());
        Assert.Equal(new byte[] { 5 }, file.Slice(4, 1).ToArray());
    }

    [Fact]
    public void An_empty_file_maps_to_empty_slices()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("empty.bin", []);

        var result = MappedFile.Open(path);
        Assert.True(result.IsSuccess);

        using var file = result.Value;
        Assert.Equal(0, file.Length);
        Assert.True(file.Slice(0, 0).IsEmpty);
    }

    [Fact]
    public void A_slice_outside_the_file_is_rejected()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("data.bin", [1, 2, 3]);

        using var file = MappedFile.Open(path).Value;

        Assert.Throws<ArgumentOutOfRangeException>(() => file.Slice(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.Slice(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.Slice(3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.Slice(0, -1));
    }

    [Fact]
    public void A_missing_file_is_unreadable()
    {
        using var temp = new TempDirectory();

        var result = MappedFile.Open(temp.FullPath("missing.bin"));

        Assert.True(result.IsFailure);
        Assert.Equal("io.unreadable", result.Error.Code);
    }

    [Fact]
    public void A_size_zero_directory_is_unreadable()
    {
        // A /proc directory reports st_size == 0: the size fast path must not
        // call it a successful empty mapping.
        var result = MappedFile.Open("/proc/self");

        Assert.True(result.IsFailure);
        Assert.Equal("io.unreadable", result.Error.Code);
    }

    [Fact]
    public void A_directory_is_unreadable()
    {
        using var temp = new TempDirectory();

        var result = MappedFile.Open(temp.Path);

        Assert.True(result.IsFailure);
        Assert.Equal("io.unreadable", result.Error.Code);
    }

    [Fact]
    public void An_unreadable_file_is_unreadable()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("locked.bin", [1, 2, 3]);
        File.SetUnixFileMode(path, UnixFileMode.None);

        try
        {
            var result = MappedFile.Open(path);
            Assert.Equal("io.unreadable", result.Error.Code);
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void A_blank_path_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => MappedFile.Open(string.Empty));
        Assert.Throws<ArgumentException>(() => MappedFile.Open(" "));
        Assert.Throws<ArgumentNullException>(() => MappedFile.Open(null!));
    }

    [Fact]
    public void Dispose_is_idempotent_and_invalidates_slices()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("data.bin", [1, 2, 3]);
        var file = MappedFile.Open(path).Value;

        file.Dispose();
        file.Dispose();

        Assert.Throws<ObjectDisposedException>(() => file.Slice(0, 1));
    }
}
