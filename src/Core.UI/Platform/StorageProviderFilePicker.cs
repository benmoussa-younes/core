// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Platform.Storage;
using Core.Mvp.Services;

using Core.UI.Hosting;

namespace Core.UI.Platform;

/// <summary>
/// The <see cref="IFilePicker"/> over Avalonia's <see cref="IStorageProvider"/>:
/// it maps the headless options to the platform pickers and returns the local
/// paths, so a presenter never names an Avalonia type.
/// </summary>
public sealed class StorageProviderFilePicker : IFilePicker, IFolderPicker
{
    private readonly Func<FilePickerOpenOptions, Task<IReadOnlyList<IStorageFile>>> _open;
    private readonly Func<FilePickerSaveOptions, Task<IStorageFile?>> _save;
    private readonly Func<FolderPickerOpenOptions, Task<IReadOnlyList<IStorageFolder>>> _openFolder;

    /// <summary>Initializes a new instance of the <see cref="StorageProviderFilePicker"/> class.</summary>
    /// <param name="provider">The top level's storage provider.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <see langword="null"/>.</exception>
    public StorageProviderFilePicker(IStorageProvider provider)
        : this(
            Required(provider).OpenFilePickerAsync,
            Required(provider).SaveFilePickerAsync,
            Required(provider).OpenFolderPickerAsync)
    {
    }

    internal StorageProviderFilePicker(
        Func<FilePickerOpenOptions, Task<IReadOnlyList<IStorageFile>>> open,
        Func<FilePickerSaveOptions, Task<IStorageFile?>> save)
        : this(open, save, _ => Task.FromResult<IReadOnlyList<IStorageFolder>>([]))
    {
    }

    internal StorageProviderFilePicker(
        Func<FilePickerOpenOptions, Task<IReadOnlyList<IStorageFile>>> open,
        Func<FilePickerSaveOptions, Task<IStorageFile?>> save,
        Func<FolderPickerOpenOptions, Task<IReadOnlyList<IStorageFolder>>> openFolder)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(openFolder);

        _open = open;
        _save = save;
        _openFolder = openFolder;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public async ValueTask<IReadOnlyList<string>> OpenFilesAsync(
        FilePickerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        UiServices.Dispatcher.VerifyAccess();

        var picked = await _open(new FilePickerOpenOptions
        {
            Title = options.Title,
            AllowMultiple = options.AllowMultiple,
            FileTypeFilter = MapFilters(options.Filters),
        }).ConfigureAwait(true);

        var paths = new List<string>(picked.Count);

        foreach (var file in picked)
        {
            if (LocalPath(file) is { } path)
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public async ValueTask<string?> SaveFileAsync(
        SaveFilePickerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        UiServices.Dispatcher.VerifyAccess();

        var file = await _save(new FilePickerSaveOptions
        {
            Title = options.Title,
            SuggestedFileName = options.SuggestedFileName,
            FileTypeChoices = MapFilters(options.Filters),
        }).ConfigureAwait(true);

        return file is null ? null : LocalPath(file);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public async ValueTask<string?> PickFolderAsync(
        FolderPickerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        UiServices.Dispatcher.VerifyAccess();

        var folders = await _openFolder(new FolderPickerOpenOptions
        {
            Title = options.Title,
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return folders.Count == 0 ? null : LocalFolderPath(folders[0]);
    }

    private static IStorageProvider Required(IStorageProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return provider;
    }

    private static List<FilePickerFileType>? MapFilters(IReadOnlyList<FileTypeFilter>? filters)
    {
        if (filters is null || filters.Count == 0)
        {
            return null;
        }

        var mapped = new List<FilePickerFileType>(filters.Count);

        foreach (var filter in filters)
        {
            ArgumentNullException.ThrowIfNull(filter);
            ArgumentNullException.ThrowIfNull(filter.Patterns);
            mapped.Add(new FilePickerFileType(filter.Name) { Patterns = [.. filter.Patterns] });
        }

        return mapped;
    }

    private static string? LocalPath(IStorageItem item) => StorageItemPaths.File(item.Path);

    private static string? LocalFolderPath(IStorageItem item) => StorageItemPaths.Folder(item.Path);
}
