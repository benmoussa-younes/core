// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.IO.Mapped;
using Core.IO.Records.Scan;

namespace Core.IO.Records.Tests;

public sealed class MappedFileScanTests
{
    // Newlines at indices 2, 4, 7 and 10.
    private static readonly byte[] Sample = [1, 2, 10, 3, 10, 4, 5, 10, 6, 7, 10];

    [Fact]
    public void Sequential_counts_every_occurrence_across_windows()
    {
        using var temp = new TempFile(Sample);
        using var file = temp.Open();

        Assert.Equal(4, MappedFileScan.CountSequential(file, 10, windowSize: 3));
    }

    [Fact]
    public void The_default_window_size_is_used()
    {
        using var temp = new TempFile(Sample);
        using var file = temp.Open();

        Assert.Equal(4, MappedFileScan.CountSequential(file, 10));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task Parallel_matches_sequential(int threads)
    {
        using var temp = new TempFile(Sample);
        using var file = temp.Open();

        var sequential = MappedFileScan.CountSequential(file, 10, windowSize: 3);
        var parallel = await MappedFileScan.CountAsync(
            file,
            10,
            threads,
            windowSize: 3,
            TestContext.Current.CancellationToken);

        Assert.Equal(sequential, parallel);
    }

    [Fact]
    public void Adjacent_window_boundaries_are_not_double_counted()
    {
        using var temp = new TempFile([10, 1, 10, 2, 10]);
        using var file = temp.Open();

        Assert.Equal(3, MappedFileScan.CountSequential(file, 10, windowSize: 2));
    }

    [Fact]
    public async Task An_empty_file_counts_zero()
    {
        using var temp = new TempFile([]);
        using var file = temp.Open();

        Assert.Equal(0, MappedFileScan.CountSequential(file, 10, windowSize: 4));
        Assert.Equal(
            0,
            await MappedFileScan.CountAsync(file, 10, 2, 4, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void An_absent_byte_counts_zero()
    {
        using var temp = new TempFile(Sample);
        using var file = temp.Open();

        Assert.Equal(0, MappedFileScan.CountSequential(file, 99, windowSize: 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_window_size_is_rejected(int windowSize)
    {
        using var temp = new TempFile(Sample);
        using var file = temp.Open();

        Assert.Throws<ArgumentOutOfRangeException>(() => MappedFileScan.CountSequential(file, 10, windowSize));
    }

    [Fact]
    public async Task A_non_positive_window_size_is_rejected_async()
    {
        using var temp = new TempFile(Sample);
        using var file = temp.Open();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => MappedFileScan.CountAsync(file, 10, 2, 0, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public void A_null_file_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => MappedFileScan.CountSequential(null!, 10));
    }

    [Fact]
    public async Task A_null_file_is_rejected_async()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => MappedFileScan.CountAsync(null!, 10, 2, cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1025)]
    public async Task An_out_of_range_thread_count_is_rejected(int threads)
    {
        using var temp = new TempFile(Sample);
        using var file = temp.Open();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => MappedFileScan.CountAsync(file, 10, threads, 3, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task A_cancelled_scan_throws()
    {
        using var temp = new TempFile(Sample);
        using var file = temp.Open();
        using var cancellation = new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => MappedFileScan.CountAsync(file, 10, 2, 3, cancellation.Token).AsTask());
    }

    private sealed class TempFile : IDisposable
    {
        public TempFile(byte[] content)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "core-scan-" + Guid.NewGuid().ToString("N"));

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
