// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform.Linux;
using Core.Mvp.Services;

namespace Core.Mvp.Tests;

public sealed class JsonAppearanceStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), string.Concat("core-appearance-", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_missing_file_loads_as_no_settings()
    {
        var store = new JsonAppearanceStore(Path.Combine(_directory, "preferences.json"), new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());

        Assert.Null(store.Load());
    }

    [Fact]
    public void It_round_trips_the_settings_and_creates_the_directory()
    {
        var path = Path.Combine(_directory, "nested", "preferences.json");
        var store = new JsonAppearanceStore(path, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());
        var settings = new AppearanceSettings(ThemePreference.Dark, "AccentBlue", true);

        store.Save(settings);

        Assert.True(File.Exists(path));
        Assert.Equal(settings, store.Load());
    }

    [Fact]
    public void A_corrupt_file_loads_as_no_settings()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "preferences.json");
        File.WriteAllText(path, "{ not json");
        var store = new JsonAppearanceStore(path, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());

        Assert.Null(store.Load());
    }

    [Fact]
    public void Clear_forgets_the_settings()
    {
        var path = Path.Combine(_directory, "preferences.json");
        var store = new JsonAppearanceStore(path, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());
        store.Save(new AppearanceSettings());

        store.Clear();

        Assert.Null(store.Load());
        store.Clear();
        Assert.Null(store.Load());
    }

    [Fact]
    public void It_exposes_its_path_and_validates_it()
    {
        var path = Path.Combine(_directory, "preferences.json");
        var store = new JsonAppearanceStore(path, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());

        Assert.Equal(path, store.Path);
        Assert.Throws<ArgumentException>(() => new JsonAppearanceStore(string.Empty, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter()));
        Assert.Throws<ArgumentNullException>(() => store.Save(null!));
    }
}
