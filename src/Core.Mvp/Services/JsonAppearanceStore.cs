// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform;

namespace Core.Mvp.Services;

/// <summary>
/// The default <see cref="IAppearanceStore"/>: one JSON file through
/// <see cref="JsonSettingsStore{T}"/>. An unreadable or malformed file loads as no
/// settings, so a corrupt preferences file never blocks startup.
/// </summary>
public sealed class JsonAppearanceStore : IAppearanceStore
{
    private readonly JsonSettingsStore<AppearanceSettings> _store;

    /// <summary>Initializes a new instance of the <see cref="JsonAppearanceStore"/> class.</summary>
    /// <param name="path">The settings file path.</param>
    /// <param name="atomicFiles">The platform atomic-writer factory.</param>
    /// <param name="fileSystem">The platform file system, used for a bounded no-follow read.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="atomicFiles"/> or <paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    public JsonAppearanceStore(string path, IAtomicFileWriterFactory atomicFiles, IFileSystem fileSystem)
    {
        _store = new JsonSettingsStore<AppearanceSettings>(
            path,
            AppearanceJsonContext.Default.AppearanceSettings,
            atomicFiles,
            fileSystem);
    }

    /// <summary>Gets the settings file path.</summary>
    public string Path => _store.Path;

    /// <inheritdoc />
    public AppearanceSettings? Load() => _store.Load();

    /// <inheritdoc />
    public void Save(AppearanceSettings settings) => _store.Save(settings);

    /// <inheritdoc />
    public void Clear() => _store.Clear();
}
