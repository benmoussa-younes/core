// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform.Linux;

namespace Core.IO.Tests;

public sealed class CrashLogTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), string.Concat("core-crash-", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void It_writes_a_timestamped_header_and_the_exception()
    {
        var path = Path.Combine(_directory, "crash.log");
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 10, 31, 12, 34, 56, 789, TimeSpan.Zero));

        using (var log = new CrashLog(path, timeProvider: time))
        {
            Assert.Equal(path, log.Path);
            log.Write(new InvalidOperationException("boom"), "state.save");
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal("2026-10-31T12:34:56.789Z [crash] state.save", lines[0]);
        Assert.Contains("boom", string.Join('\n', lines), StringComparison.Ordinal);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void It_scrubs_common_secret_shapes()
    {
        var path = Path.Combine(_directory, "crash.log");

        using (var log = new CrashLog(path))
        {
            log.Write(new InvalidOperationException("password=hunter2"), "token: abc123");
        }

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void It_redacts_each_written_line()
    {
        var path = Path.Combine(_directory, "crash.log");

        using (var log = new CrashLog(path, redact: line => line.Replace("secret", "[redacted]", StringComparison.Ordinal)))
        {
            log.Write(new InvalidOperationException("secret token"));
        }

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void It_bounds_the_written_stack_lines()
    {
        var path = Path.Combine(_directory, "crash.log");

        using (var log = new CrashLog(path, maximumLines: 1))
        {
            log.Write(new InvalidOperationException("boom"));
        }

        Assert.Equal(2, File.ReadAllLines(path).Length);
    }

    [Fact]
    public void A_write_failure_is_swallowed_and_a_disposed_log_ignores_entries()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "crash.log");
        Directory.CreateDirectory(path);

        var log = new CrashLog(path);
        log.Write(new InvalidOperationException("boom"));
        log.Dispose();
        log.Dispose();
        log.Write(new InvalidOperationException("ignored"));

        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void It_validates_its_arguments()
    {
        var path = Path.Combine(_directory, "crash.log");

        using var log = new CrashLog(path);

        Assert.Throws<ArgumentNullException>(() => log.Write(null!));
        Assert.Throws<ArgumentException>(() => new CrashLog(string.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CrashLog(path, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CrashLog(path, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CrashLog(path, 1, 1, 0));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
