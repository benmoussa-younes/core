// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.IO.Mapped;
using Core.IO.Records.Index;

namespace Core.IO.Records.Tests;

public sealed class MappedFileIndexTests
{
    // Newlines at indices 2, 4, 7 and 10.
    private static readonly byte[] Sample = [1, 2, 10, 3, 10, 4, 5, 10, 6, 7, 10];

    [Fact]
    public async Task It_indexes_every_delimiter_in_order()
    {
        using var temp = new TempFile(Sample);
        using var file = temp.Open();

        var offsets = await MappedFileIndex.BuildAsync(
            file,
            10,
            2,
            windowSize: 3,
            TestContext.Current.CancellationToken);

        Assert.Equal(new long[] { 2, 4, 7, 10 }, offsets);
    }

    [Fact]
    public async Task An_index_beyond_the_maximum_is_refused()
    {
        using var temp = new TempFile(Sample);
        using var file = temp.Open();

        await Assert.ThrowsAsync<InvalidOperationException>(() => MappedFileIndex.BuildAsync(
            file,
            10,
            threads: 1,
            maxOffsets: 2,
            windowSize: 3,
            TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(4, 5)]
    [InlineData(8, 1024)]
    public async Task The_index_does_not_depend_on_threads_or_window_size(int threads, int windowSize)
    {
        using var temp = new TempFile(Sample);
        using var file = temp.Open();

        var offsets = await MappedFileIndex.BuildAsync(
            file,
            10,
            threads,
            windowSize,
            TestContext.Current.CancellationToken);

        Assert.Equal(new long[] { 2, 4, 7, 10 }, offsets);
    }

    [Fact]
    public async Task An_empty_file_has_no_offsets()
    {
        using var temp = new TempFile([]);
        using var file = temp.Open();

        var offsets = await MappedFileIndex.BuildAsync(
            file,
            10,
            2,
            4,
            TestContext.Current.CancellationToken);

        Assert.Empty(offsets);
    }

    [Fact]
    public async Task An_absent_delimiter_has_no_offsets()
    {
        using var temp = new TempFile(Sample);
        using var file = temp.Open();

        var offsets = await MappedFileIndex.BuildAsync(
            file,
            99,
            2,
            3,
            TestContext.Current.CancellationToken);

        Assert.Empty(offsets);
    }

    [Fact]
    public async Task Delimiters_on_window_boundaries_are_indexed_once()
    {
        using var temp = new TempFile([10, 1, 10, 2, 10]);
        using var file = temp.Open();

        var offsets = await MappedFileIndex.BuildAsync(
            file,
            10,
            2,
            windowSize: 2,
            TestContext.Current.CancellationToken);

        Assert.Equal(new long[] { 0, 2, 4 }, offsets);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_non_positive_window_size_is_rejected(int windowSize)
    {
        using var temp = new TempFile(Sample);
        using var file = temp.Open();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => MappedFileIndex.BuildAsync(file, 10, 2, windowSize, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task A_null_file_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => MappedFileIndex.BuildAsync(null!, 10, 2, cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1025)]
    public async Task An_out_of_range_thread_count_is_rejected(int threads)
    {
        using var temp = new TempFile(Sample);
        using var file = temp.Open();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => MappedFileIndex.BuildAsync(file, 10, threads, 3, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task A_cancelled_build_throws()
    {
        using var temp = new TempFile(Sample);
        using var file = temp.Open();
        using var cancellation = new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => MappedFileIndex.BuildAsync(file, 10, 2, 3, cancellation.Token).AsTask());
    }

    [Theory]
    [InlineData(1, 4096)]
    [InlineData(4, 1024)]
    [InlineData(8, 8192)]
    public async Task It_matches_a_sequential_scan_on_multi_megabyte_random_data(int threads, int windowSize)
    {
        var random = new Random(20260929);
        var data = new byte[3 * 1024 * 1024];

        for (var index = 0; index < data.Length; index++)
        {
            data[index] = (byte)random.Next(256);
        }

        // A newline at an irregular spacing, so delimiters land on window and
        // thread boundaries.
        for (var index = 0; index < data.Length; index += random.Next(1, 200))
        {
            data[index] = 10;
        }

        var reference = new List<long>();

        for (var index = 0; index < data.Length; index++)
        {
            if (data[index] == 10)
            {
                reference.Add(index);
            }
        }

        using var temp = new TempFile(data);
        using var file = temp.Open();

        var offsets = await MappedFileIndex.BuildAsync(
            file,
            10,
            threads,
            windowSize,
            TestContext.Current.CancellationToken);

        Assert.Equal(reference, offsets);
    }

    private sealed class TempFile : IDisposable
    {
        public TempFile(byte[] content)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "core-index-" + Guid.NewGuid().ToString("N"));

            File.WriteAllBytes(Path, content);
        }

        public string Path { get; }

        public MappedFile Open()
        {
            var result = MappedFile.Open(Path);

            Assert.True(result.IsSuccess);
            return result.Value;
        }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
