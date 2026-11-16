// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Core.UI.Platform;
using Core.Mvp.Services;

namespace Core.UI.Tests;

/// <summary>
/// The adapter is tested with real <see cref="IStorageFile"/> instances from the
/// headless storage provider (Avalonia's storage interfaces are not implementable
/// by user code) and injected delegates that simulate the picker selection.
/// </summary>
[Collection("headless")]
public sealed class StorageProviderFilePickerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), string.Concat("core-picker-", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Open_maps_the_options_and_returns_the_local_paths()
    {
        var (paths, captured) = await Headless.Dispatch(() =>
        {
            var window = Show();
            var a = StorageFileFor(window, "a.conf");
            var b = StorageFileFor(window, "b.conf");

            FilePickerOpenOptions? captured = null;
            var picker = new StorageProviderFilePicker(
                options =>
                {
                    captured = options;
                    return Task.FromResult<IReadOnlyList<IStorageFile>>([a, b]);
                },
                _ => Task.FromResult<IStorageFile?>(null));

            var paths = picker
                .OpenFilesAsync(new FilePickerOptions("Open profile", AllowMultiple: true, Filters: [new FileTypeFilter("Conf", ["*.conf"])]))
                .AsTask()
                .GetAwaiter()
                .GetResult();
            window.Close();

            return (paths, captured);
        });

        Assert.Equal(2, paths.Count);
        Assert.EndsWith("a.conf", paths[0], StringComparison.Ordinal);
        Assert.EndsWith("b.conf", paths[1], StringComparison.Ordinal);
        Assert.NotNull(captured);
        Assert.Equal("Open profile", captured.Title);
        Assert.True(captured.AllowMultiple);
        var filter = Assert.Single(captured.FileTypeFilter!);
        Assert.Equal("Conf", filter.Name);
        Assert.Equal(["*.conf"], filter.Patterns!);
    }

    [Fact]
    public async Task Open_returns_empty_and_maps_no_filters_to_null()
    {
        var captured = await Headless.Dispatch(() =>
        {
            var picker = Empty();
            var paths = picker.OpenFilesAsync(new FilePickerOptions("Open")).AsTask().GetAwaiter().GetResult();

            Assert.Empty(paths);
            return paths;
        });

        Assert.Empty(captured);
    }

    [Fact]
    public async Task Save_maps_the_options_and_returns_the_path()
    {
        var (path, captured) = await Headless.Dispatch(() =>
        {
            var window = Show();
            var file = StorageFileFor(window, "out.conf");

            FilePickerSaveOptions? captured = null;
            var picker = new StorageProviderFilePicker(
                _ => Task.FromResult<IReadOnlyList<IStorageFile>>([]),
                options =>
                {
                    captured = options;
                    return Task.FromResult<IStorageFile?>(file);
                });

            var path = picker
                .SaveFileAsync(new SaveFilePickerOptions("Save", "out.conf", [new FileTypeFilter("Conf", ["*.conf"])]))
                .AsTask()
                .GetAwaiter()
                .GetResult();
            window.Close();

            return (path, captured);
        });

        Assert.NotNull(path);
        Assert.EndsWith("out.conf", path, StringComparison.Ordinal);
        Assert.NotNull(captured);
        Assert.Equal("Save", captured.Title);
        Assert.Equal("out.conf", captured.SuggestedFileName);
        Assert.Single(captured.FileTypeChoices!);
    }

    [Fact]
    public async Task Save_returns_null_when_cancelled()
    {
        var result = await Headless.Dispatch(() =>
            Empty().SaveFileAsync(new SaveFilePickerOptions("Save")).AsTask().GetAwaiter().GetResult());

        Assert.Null(result);
    }

    [Fact]
    public async Task Folder_maps_the_options_and_returns_the_path()
    {
        var (path, captured) = await Headless.Dispatch(() =>
        {
            Directory.CreateDirectory(_directory);
            var window = Show();
            var folder = window.StorageProvider
                .TryGetFolderFromPathAsync(new Uri(_directory))
                .GetAwaiter()
                .GetResult()!;

            FolderPickerOpenOptions? captured = null;
            var picker = new StorageProviderFilePicker(
                _ => Task.FromResult<IReadOnlyList<IStorageFile>>([]),
                _ => Task.FromResult<IStorageFile?>(null),
                options =>
                {
                    captured = options;
                    return Task.FromResult<IReadOnlyList<IStorageFolder>>([folder]);
                });

            var path = picker.PickFolderAsync(new FolderPickerOptions("Choose a folder"))
                .AsTask()
                .GetAwaiter()
                .GetResult();
            window.Close();

            return (path, captured);
        });

        Assert.Equal(_directory, path);
        Assert.NotNull(captured);
        Assert.Equal("Choose a folder", captured.Title);
        Assert.False(captured.AllowMultiple);
    }

    [Fact]
    public async Task Folder_returns_null_when_cancelled()
    {
        var result = await Headless.Dispatch(() =>
            Empty().PickFolderAsync(new FolderPickerOptions("Choose")).AsTask().GetAwaiter().GetResult());

        Assert.Null(result);
    }

    [Fact]
    public async Task It_validates_and_honours_cancellation()
    {
        var picker = Empty();

        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(() => picker.OpenFilesAsync(null!, token).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => picker.SaveFileAsync(null!, token).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => picker.PickFolderAsync(null!, token).AsTask());
        Assert.Throws<ArgumentNullException>(() => new StorageProviderFilePicker((IStorageProvider)null!));
        Assert.Throws<ArgumentNullException>(() => new StorageProviderFilePicker(null!, null!));
        Assert.Throws<ArgumentNullException>(() => new StorageProviderFilePicker(null!, null!, null!));

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => picker.OpenFilesAsync(new FilePickerOptions("Open"), canceled.Token).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => picker.SaveFileAsync(new SaveFilePickerOptions("Save"), canceled.Token).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => picker.PickFolderAsync(new FolderPickerOptions("Choose"), canceled.Token).AsTask());
    }

    private static StorageProviderFilePicker Empty() =>
        new(
            _ => Task.FromResult<IReadOnlyList<IStorageFile>>([]),
            _ => Task.FromResult<IStorageFile?>(null));

    private static Window Show()
    {
        var window = new Window();
        window.Show();
        return window;
    }

    private IStorageFile StorageFileFor(Window window, string name)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        System.IO.File.WriteAllText(path, string.Empty);

        return window.StorageProvider.TryGetFileFromPathAsync(new Uri(path)).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException($"The headless storage provider returned no file for '{path}'.");
    }
}
