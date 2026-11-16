// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Diagnostics;
using Core.Platform.Linux;
using Core.IO.Files;

namespace Core.IO.Tests;

public sealed class RotatingFileLoggerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), string.Concat("core-logger-", Guid.NewGuid().ToString("N")));

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
        var path = Path.Combine(_directory, "application.log");
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 10, 31, 12, 34, 56, 789, TimeSpan.Zero));

        using (var logger = new RotatingFileLogger(path, timeProvider: time))
        {
            logger.Log(LogLevel.Warning, "disk\r\nforged");
        }

        var lines = File.ReadAllLines(path);
        Assert.Single(lines);
        Assert.Equal("2026-10-31T12:34:56.789Z [Warning] disk\\r\\nforged", lines[0]);
    }

    [Fact]
    public void It_creates_the_directory_and_is_owner_only()
    {
        var path = Path.Combine(_directory, "nested", "application.log");

        using (var logger = new RotatingFileLogger(path))
        {
            logger.Log(LogLevel.Information, "x");
        }

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void It_rotates_and_keeps_the_requested_archives()
    {
        var path = Path.Combine(_directory, "application.log");
        var time = new FixedTimeProvider(DateTimeOffset.UnixEpoch);

        using (var logger = new RotatingFileLogger(path, maxBytes: 1, retainedFiles: 2, timeProvider: time))
        {
            logger.Log(LogLevel.Information, "one");
            logger.Log(LogLevel.Information, "two");
            logger.Log(LogLevel.Information, "three");
        }

        Assert.True(File.Exists(string.Concat(path, ".1")));
        Assert.True(File.Exists(string.Concat(path, ".2")));
        Assert.Contains("three", File.ReadAllText(string.Concat(path, ".1")), StringComparison.Ordinal);
        Assert.Contains("two", File.ReadAllText(string.Concat(path, ".2")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_write_failure_is_swallowed()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "application.log");
        Directory.CreateDirectory(path);

        using var logger = new RotatingFileLogger(path);

        logger.Log(LogLevel.Error, "this cannot be written to a directory");
    }

    [Fact]
    public void A_disposed_logger_ignores_entries()
    {
        var path = Path.Combine(_directory, "application.log");
        var logger = new RotatingFileLogger(path);

        logger.Dispose();
        logger.Dispose();
        logger.Log(LogLevel.Information, "ignored");

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void It_validates_its_arguments()
    {
        var path = Path.Combine(_directory, "application.log");

        using var logger = new RotatingFileLogger(path);
        Assert.Equal(path, logger.Path);
        Assert.Equal(5, logger.RetainedFiles);

        Assert.Throws<ArgumentNullException>(() => logger.Log(LogLevel.Information, null!));
        Assert.Throws<ArgumentException>(() => new RotatingFileLogger(string.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingTextFile(path, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingTextFile(path, 1) { RetainedFiles = 0 });
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
