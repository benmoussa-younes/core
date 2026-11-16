// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform.Linux;
using Core.IO.Mapped;

namespace Core.IO.Tests;

/// <summary>
/// The raw <c>mmap</c> mapping must behave exactly like the BCL-backed
/// <see cref="MappedFile"/>: same length, same bytes, same failure.
/// </summary>
public sealed class MemoryMappingParityTests
{
    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 7 })]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })]
    public void The_raw_mapping_matches_MappedFile(byte[] content)
    {
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("data.bin", content);

        using var managed = MappedFile.Open(path).Value;
        using var raw = LinuxMemoryMapping.Open(path).Value;

        Assert.Equal(managed.Length, raw.Length);

        if (managed.Length == 0)
        {
            return;
        }

        Assert.Equal(
            managed.Slice(0, (int)managed.Length).ToArray(),
            raw.Slice(0, (int)raw.Length).ToArray());
        Assert.Equal(
            managed.Slice(1, (int)managed.Length - 1).ToArray(),
            raw.Slice(1, (int)raw.Length - 1).ToArray());
    }

    [Fact]
    public async Task A_fifo_is_refused_without_blocking()
    {
        using var temp = new TempDirectory();
        var path = temp.FullPath("pipe");
        RawFileSystem.MakeFifo(path, 0x1A4);

        var opened = Task.Run(() => LinuxMemoryMapping.Open(path));
        var completed = await Task.WhenAny(
            opened,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.True(completed == opened, "Mapping a FIFO blocked.");
        Assert.Equal("io.unreadable", (await opened).Error.Code);
    }

    [Fact]
    public void Both_refuse_a_missing_file_the_same_way()
    {
        using var temp = new TempDirectory();
        var path = temp.FullPath("missing.bin");

        Assert.Equal("io.unreadable", MappedFile.Open(path).Error.Code);
        Assert.Equal("io.unreadable", LinuxMemoryMapping.Open(path).Error.Code);
    }
}
