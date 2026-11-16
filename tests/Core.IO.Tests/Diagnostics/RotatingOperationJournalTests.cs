// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform.Linux;

namespace Core.IO.Tests;

public sealed class RotatingOperationJournalTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), string.Concat("core-journal-", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void It_writes_one_timestamped_single_line_entry()
    {
        var path = Path.Combine(_directory, "journal.log");
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 10, 31, 12, 34, 56, 789, TimeSpan.Zero));

        using (var journal = new RotatingOperationJournal(path, timeProvider: time))
        {
            journal.Record("tunnel.up", "wg0\r\nforged");
        }

        var lines = File.ReadAllLines(path);
        Assert.Single(lines);
        Assert.Equal("2026-10-31T12:34:56.789Z [tunnel.up] wg0\\r\\nforged", lines[0]);
        Assert.Equal(path, new RotatingOperationJournal(path).Path);
    }

    [Fact]
    public void It_creates_the_directory_and_is_owner_only()
    {
        var path = Path.Combine(_directory, "nested", "journal.log");

        using (var journal = new RotatingOperationJournal(path))
        {
            journal.Record("state.save", "ok");
        }

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void A_write_failure_is_swallowed_and_a_disposed_journal_ignores_entries()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "journal.log");
        Directory.CreateDirectory(path);

        var journal = new RotatingOperationJournal(path);
        journal.Record("tunnel.up", "cannot write to a directory");
        journal.Dispose();
        journal.Dispose();
        journal.Record("state.save", "ignored");

        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void It_validates_its_arguments()
    {
        var path = Path.Combine(_directory, "journal.log");

        using var journal = new RotatingOperationJournal(path);

        Assert.Throws<ArgumentNullException>(() => journal.Record(null!, "message"));
        Assert.Throws<ArgumentNullException>(() => journal.Record("operation", null!));
        Assert.Throws<ArgumentException>(() => new RotatingOperationJournal(string.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingOperationJournal(path, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingOperationJournal(path, 1, 0));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
