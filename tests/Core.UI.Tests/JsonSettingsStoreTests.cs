// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.UI.Chrome;
using Core.Platform.Linux;
using Core.Mvp.Models;
using Core.Mvp.Services;

namespace Core.UI.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), string.Concat("core-settings-", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void It_round_trips_a_value_and_creates_the_directory()
    {
        var path = Path.Combine(_directory, "nested", "settings.json");
        var store = new JsonSettingsStore<WindowPlacement>(path, WindowPlacementJsonContext.Default.WindowPlacement, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());
        var placement = new WindowPlacement(1, 2, 800, 600, Maximized: true);

        store.Save(placement);

        Assert.Equal(path, store.Path);
        Assert.True(File.Exists(path));
        Assert.Equal(placement, store.Load());
    }

    [Fact]
    public void A_missing_or_corrupt_file_loads_as_nothing()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonSettingsStore<WindowPlacement>(path, WindowPlacementJsonContext.Default.WindowPlacement, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());

        Assert.Null(store.Load());

        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "{ not json");
        Assert.Null(store.Load());
    }

    [Fact]
    public void Clear_forgets_the_value()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonSettingsStore<WindowPlacement>(path, WindowPlacementJsonContext.Default.WindowPlacement, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());
        store.Save(new WindowPlacement(0, 0, 100, 100));

        store.Clear();

        Assert.Null(store.Load());
        store.Clear();
        Assert.Null(store.Load());
    }

    [Fact]
    public void A_symbolic_link_is_not_followed()
    {
        Directory.CreateDirectory(_directory);
        var target = Path.Combine(_directory, "target.json");
        File.WriteAllText(target, "{}");
        var link = Path.Combine(_directory, "link.json");
        File.CreateSymbolicLink(link, target);

        var store = new JsonSettingsStore<WindowPlacement>(
            link,
            WindowPlacementJsonContext.Default.WindowPlacement,
            new LinuxAtomicFileWriterFactory(),
            new LinuxFileSystemAdapter());

        Assert.Null(store.Load());
    }

    [Fact]
    public void An_oversized_file_is_not_read()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "huge.json");

        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(JsonSettingsStore<WindowPlacement>.MaxFileBytes + 1);
        }

        var store = new JsonSettingsStore<WindowPlacement>(
            path,
            WindowPlacementJsonContext.Default.WindowPlacement,
            new LinuxAtomicFileWriterFactory(),
            new LinuxFileSystemAdapter());

        Assert.Null(store.Load());
    }

    [Fact]
    public void An_unreadable_file_loads_as_nothing()
    {
        var path = Path.Combine(_directory, "settings.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "{}");
        File.SetUnixFileMode(path, UnixFileMode.None);

        try
        {
            var store = new JsonSettingsStore<WindowPlacement>(path, WindowPlacementJsonContext.Default.WindowPlacement, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());

            Assert.Null(store.Load());
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void A_saved_file_is_owner_only()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonSettingsStore<WindowPlacement>(path, WindowPlacementJsonContext.Default.WindowPlacement, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());

        store.Save(new WindowPlacement(1, 2, 800, 600));

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(path));
    }

    [Fact]
    public void Clear_is_best_effort_when_the_path_is_a_directory()
    {
        var path = Path.Combine(_directory, "settings.json");
        Directory.CreateDirectory(path);
        var store = new JsonSettingsStore<WindowPlacement>(path, WindowPlacementJsonContext.Default.WindowPlacement, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());

        store.Clear();

        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void It_validates_its_arguments()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonSettingsStore<WindowPlacement>(path, WindowPlacementJsonContext.Default.WindowPlacement, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());

        Assert.Throws<ArgumentException>(() => new JsonSettingsStore<WindowPlacement>(string.Empty, WindowPlacementJsonContext.Default.WindowPlacement, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter()));
        Assert.Throws<ArgumentNullException>(() => new JsonSettingsStore<WindowPlacement>(path, null!, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter()));
        Assert.Throws<ArgumentNullException>(() => store.Save(null!));
    }
}
