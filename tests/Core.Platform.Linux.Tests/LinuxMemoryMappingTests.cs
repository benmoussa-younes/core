// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform.Linux;

namespace Core.Platform.Linux.Tests;

public sealed class LinuxMemoryMappingTests
{
    [Fact]
    public void It_maps_the_content()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("data.bin", [1, 2, 3, 4, 5]);

        var result = LinuxMemoryMapping.Open(path);
        Assert.True(result.IsSuccess);

        using var mapping = result.Value;
        Assert.Equal(5L, mapping.Length);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, mapping.Slice(0, 5).ToArray());
        Assert.Equal(new byte[] { 3, 4 }, mapping.Slice(2, 2).ToArray());
    }

    [Fact]
    public void An_empty_file_maps_to_empty_slices()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("empty.bin", []);

        using var mapping = LinuxMemoryMapping.Open(path).Value;

        Assert.Equal(0L, mapping.Length);
        Assert.True(mapping.Slice(0, 0).IsEmpty);
    }

    [Fact]
    public void A_missing_file_is_unreadable()
    {
        using var temp = new TempDirectory();

        var result = LinuxMemoryMapping.Open(temp.FullPath("missing.bin"));

        Assert.True(result.IsFailure);
        Assert.Equal("io.unreadable", result.Error.Code);
    }

    [Fact]
    public void A_null_path_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => LinuxMemoryMapping.Open(null!));
    }

    [Fact]
    public void A_slice_outside_the_file_is_rejected()
    {
        using var temp = new TempDirectory();
        using var mapping = LinuxMemoryMapping.Open(temp.WriteBytes("data.bin", [1, 2, 3])).Value;

        Assert.Throws<ArgumentOutOfRangeException>(() => mapping.Slice(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => mapping.Slice(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => mapping.Slice(3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => mapping.Slice(0, -1));
    }

    [Fact]
    public void Dispose_is_idempotent_and_invalidates_slices()
    {
        using var temp = new TempDirectory();
        var mapping = LinuxMemoryMapping.Open(temp.WriteBytes("data.bin", [1, 2, 3])).Value;

        mapping.Dispose();
        mapping.Dispose();

        Assert.Throws<ObjectDisposedException>(() => mapping.Slice(0, 1));
    }

    [Fact]
    public void A_bad_descriptor_fails_to_map()
    {
        var result = LinuxMemoryMapping.TryMap(-1, 4096, "bad-descriptor");

        Assert.True(result.IsFailure);
        Assert.Equal("io.unreadable", result.Error.Code);
    }

    [Fact]
    public void A_zero_length_descriptor_maps_empty()
    {
        var result = LinuxMemoryMapping.TryMap(-1, 0, "empty");

        Assert.True(result.IsSuccess);

        using var mapping = result.Value;
        Assert.Equal(0L, mapping.Length);
    }

    [Fact]
    public void TryOpen_opens_a_file_and_reports_a_missing_one()
    {
        using var temp = new TempDirectory();

        Assert.True(LinuxFileSystem.TryOpen(temp.WriteBytes("data.bin", [1, 2, 3]), out var descriptor));
        Assert.True(descriptor >= 0);
        _ = Interop.Close(descriptor);

        Assert.False(LinuxFileSystem.TryOpen(temp.FullPath("missing.bin"), out var missing));
        Assert.Equal(-1, missing);
    }

    [Fact]
    public void TryOpen_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => LinuxFileSystem.TryOpen(null!, out _));
    }

    [Fact]
    public void Mapping_a_bad_descriptor_fails_at_stat()
    {
        var result = LinuxMemoryMapping.MapDescriptor(-1, "bad-descriptor");

        Assert.True(result.IsFailure);
        Assert.Equal("io.unreadable", result.Error.Code);
    }

    [Fact]
    public void It_accepts_access_advice()
    {
        using var temp = new TempDirectory();
        using var mapping = LinuxMemoryMapping.Open(temp.WriteBytes("data.bin", [1, 2, 3])).Value;

        Assert.True(mapping.Advise(LinuxMemoryAdvice.Sequential));
        Assert.True(mapping.Advise(LinuxMemoryAdvice.WillNeed));
    }

    [Fact]
    public void An_empty_mapping_has_nothing_to_advise()
    {
        using var temp = new TempDirectory();
        using var mapping = LinuxMemoryMapping.Open(temp.WriteBytes("empty.bin", [])).Value;

        Assert.True(mapping.Advise(LinuxMemoryAdvice.Sequential));
    }

    [Fact]
    public void Advice_after_dispose_is_rejected()
    {
        using var temp = new TempDirectory();
        var mapping = LinuxMemoryMapping.Open(temp.WriteBytes("data.bin", [1, 2, 3])).Value;

        mapping.Dispose();

        Assert.Throws<ObjectDisposedException>(() => mapping.Advise(LinuxMemoryAdvice.Normal));
    }

    [Fact]
    public void Advising_an_unbacked_address_fails()
    {
        unsafe
        {
            Assert.False(LinuxMemoryMapping.TryAdviseAddress((void*)1, 4096, (int)LinuxMemoryAdvice.Normal));
        }
    }
}
