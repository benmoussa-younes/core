// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Services;
using Core.Platform.Linux;

namespace Core.Mvp.Tests;

// Regression cases for the settings-store findings: the permission guarantees
// JsonSettingsStore<T> documents, and the platform-port bypass in Clear.
public sealed class SettingsStoreReproTests : IDisposable
{
    private const UnixFileMode OwnerOnly =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode WorldReadable =
        UnixFileMode.UserRead | UnixFileMode.UserWrite |
        UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const UnixFileMode GroupReadableDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), string.Concat("core-store-repro-", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private JsonAppearanceStore Store(string path) =>
        new(path, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());

    // The store refuses a group- or world-writable state directory, as the host
    // does; the process umask here makes a bare CreateDirectory group-writable,
    // so the fixture states the mode it means.
    private void CreateStateDirectory()
    {
        Directory.CreateDirectory(_directory);
        File.SetUnixFileMode(
            _directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public void SaveLeavesAnExistingWorldReadableFileOwnerOnly()
    {
        CreateStateDirectory();
        var path = Path.Combine(_directory, "preferences.json");
        File.WriteAllText(path, "{}");
        File.SetUnixFileMode(path, WorldReadable);

        Store(path).Save(AppearanceSettings.Default);

        Assert.Equal(OwnerOnly, File.GetUnixFileMode(path));
    }

    [Fact]
    public void SavePreservesAPreExistingGroupReadableStateDirectory()
    {
        var state = Path.Combine(_directory, "state");
        Directory.CreateDirectory(state);
        File.SetUnixFileMode(state, GroupReadableDirectory);

        Store(Path.Combine(state, "preferences.json")).Save(AppearanceSettings.Default);

        Assert.Equal(GroupReadableDirectory, File.GetUnixFileMode(state));
    }

    [Fact]
    public void ClearDoesNotUnlinkASymbolicLinkAtTheSettingsPath()
    {
        // WHY: Load refuses a final link and Save refuses to replace one, so Clear
        // must not follow the opposite policy and act on an entry the store never
        // wrote.
        CreateStateDirectory();
        var target = Path.Combine(_directory, "target.json");
        File.WriteAllText(target, "{}");

        var path = Path.Combine(_directory, "preferences.json");
        File.CreateSymbolicLink(path, target);

        Store(path).Clear();

        Assert.NotNull(new FileInfo(path).LinkTarget);
        Assert.True(File.Exists(target));
        Assert.Equal("{}", File.ReadAllText(target));
    }

    [Fact]
    public void AStorePathWithAnEmbeddedNulIsRejectedAtConstruction()
    {
        // A path with an interior NUL cannot name a file: the platform encoding
        // truncates it at the first NUL while File.Delete throws ArgumentException,
        // which the Clear catch filter does not cover. The boundary rejects it.
        var path = string.Concat(Path.Combine(_directory, "preferences.json"), "\0", "/../../etc/shadow");

        Assert.Throws<ArgumentException>(() => Store(path));
    }
}
