// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.IO.Mapped;
using Core.IO.Records.Index;

namespace Core.IO.Records.Tests;

public sealed class RecordSetTests
{
    [Fact]
    public async Task A_file_without_a_trailing_delimiter_has_a_trailing_record()
    {
        using var temp = new TempFile([1, 2, 10, 3, 10, 4]);
        using var file = temp.Open();

        var records = await Open(file);

        Assert.Equal(10, records.Delimiter);
        Assert.Equal(3L, records.Count);
        Assert.Equal([1, 2], records.Record(0).ToArray());
        Assert.Equal([3], records.Record(1).ToArray());
        Assert.Equal([4], records.Record(2).ToArray());
        Assert.Equal(0, records.Start(0));
        Assert.Equal(2, records.End(0));
        Assert.Equal(3, records.Start(1));
        Assert.Equal(4, records.End(1));
        Assert.Equal(5, records.Start(2));
        Assert.Equal(6, records.End(2));
    }

    [Fact]
    public async Task A_file_ending_with_the_delimiter_has_no_trailing_record()
    {
        using var temp = new TempFile([10, 1, 10]);
        using var file = temp.Open();

        var records = await Open(file);

        Assert.Equal(2L, records.Count);
        Assert.True(records.Record(0).IsEmpty);
        Assert.Equal([1], records.Record(1).ToArray());
    }

    [Fact]
    public async Task A_file_without_a_delimiter_is_one_record()
    {
        using var temp = new TempFile([1, 2, 3]);
        using var file = temp.Open();

        var records = await Open(file);

        Assert.Equal(1L, records.Count);
        Assert.Equal([1, 2, 3], records.Record(0).ToArray());
    }

    [Fact]
    public async Task An_empty_file_has_no_records()
    {
        using var temp = new TempFile([]);
        using var file = temp.Open();

        var records = await Open(file);

        Assert.Equal(0L, records.Count);
    }

    [Fact]
    public async Task Consecutive_delimiters_produce_an_empty_record()
    {
        using var temp = new TempFile([1, 10, 10, 2]);
        using var file = temp.Open();

        var records = await Open(file);

        Assert.Equal(3L, records.Count);
        Assert.Equal([1], records.Record(0).ToArray());
        Assert.True(records.Record(1).IsEmpty);
        Assert.Equal([2], records.Record(2).ToArray());
    }

    [Fact]
    public async Task An_index_outside_the_records_is_rejected()
    {
        using var temp = new TempFile([1, 2, 10]);
        using var file = temp.Open();

        var records = await Open(file);

        Assert.Throws<ArgumentOutOfRangeException>(() => records.Start(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => records.Start(records.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => records.End(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => records.End(records.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => records.Record(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => records.Record(records.Count));
    }

    [Fact]
    public async Task A_null_file_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => RecordSet.OpenAsync(null!, 10, 2, cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task A_non_positive_window_size_is_rejected()
    {
        using var temp = new TempFile([1, 2, 10]);
        using var file = temp.Open();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => RecordSet.OpenAsync(file, 10, 2, 0, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task A_cancelled_open_throws()
    {
        using var temp = new TempFile([1, 2, 10]);
        using var file = temp.Open();
        using var cancellation = new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RecordSet.OpenAsync(file, 10, 2, 3, cancellation.Token).AsTask());
    }

    private static ValueTask<RecordSet> Open(MappedFile file)
    {
        return RecordSet.OpenAsync(file, 10, 2, windowSize: 3, TestContext.Current.CancellationToken);
    }

    private sealed class TempFile : IDisposable
    {
        public TempFile(byte[] content)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "records-" + Guid.NewGuid().ToString("N"));

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
