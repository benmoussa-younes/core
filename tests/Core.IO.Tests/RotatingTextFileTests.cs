// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.IO.Files;
using Core.Platform.Linux;

namespace Core.IO.Tests;

public sealed class RotatingTextFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), string.Concat("core-log-", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_fifo_log_path_is_refused_without_blocking()
    {
        using var temp = new TempDirectory();
        var path = temp.FullPath("application.log");
        RawFileSystem.MakeFifo(path, 0x1A4);

        using var file = new RotatingTextFile(path);

        Assert.Throws<IOException>(() => file.WriteLine("x"));
    }

    [Fact]
    public void It_appends_lines()
    {
        var path = Path.Combine(_directory, "app.log");

        using (var file = new RotatingTextFile(path))
        {
            Assert.Equal(path, file.Path);
            file.WriteLine("first");
            file.WriteLine("second");
        }

        Assert.Equal(["first", "second"], File.ReadAllLines(path));
    }

    [Fact]
    public void It_rotates_when_it_reaches_the_maximum()
    {
        var path = Path.Combine(_directory, "app.log");

        using (var file = new RotatingTextFile(path, maxBytes: 10))
        {
            file.WriteLine("aaaaa");
            file.WriteLine("bbbbb");
        }

        Assert.True(File.Exists(string.Concat(path, ".1")));
        Assert.Equal(["aaaaa", "bbbbb"], File.ReadAllLines(string.Concat(path, ".1")));
    }

    [Fact]
    public void It_creates_the_directory_and_is_owner_only()
    {
        var path = Path.Combine(_directory, "nested", "app.log");
        using var file = new RotatingTextFile(path);

        file.WriteLine("x");

        Assert.True(File.Exists(path));
        var mode = File.GetUnixFileMode(path);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    [Fact]
    public void It_refuses_a_symbolic_link_path()
    {
        Directory.CreateDirectory(_directory);
        var target = Path.Combine(_directory, "target.log");
        var link = Path.Combine(_directory, "link.log");
        File.WriteAllText(target, string.Empty);
        File.CreateSymbolicLink(link, target);

        using var file = new RotatingTextFile(link);

        Assert.Throws<IOException>(() => file.WriteLine("x"));
        Assert.False(File.Exists(target) && File.ReadAllText(target).Length > 0);
    }

    [Fact]
    public void Dispose_without_writing_is_a_noop()
    {
        var path = Path.Combine(_directory, "app.log");

        new RotatingTextFile(path).Dispose();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void It_validates_and_rejects_use_after_dispose()
    {
        var path = Path.Combine(_directory, "app.log");
        var file = new RotatingTextFile(path);

        Assert.Throws<ArgumentNullException>(() => file.WriteLine(null!));

        file.Dispose();
        file.Dispose();

        Assert.Throws<ObjectDisposedException>(() => file.WriteLine("x"));
        Assert.Throws<ArgumentException>(() => new RotatingTextFile(string.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingTextFile(path, 0));
    }
}
