// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Core.Platform;

namespace Core.Mvp.Services;

/// <summary>
/// A source-generated JSON settings file: one value, replaced through a temporary
/// file so a crash leaves the previous settings or the new ones, never a prefix.
/// </summary>
/// <remarks>
/// The file is best-effort: an unreadable or malformed file loads as no settings,
/// so a corrupt preferences or placement file never blocks startup. On Linux the
/// temporary file is created owner-only (0600) before the rename, and the
/// directory is created owner-only (0700). The caller supplies the
/// source-generated <see cref="JsonTypeInfo{T}"/>, so the store stays trim-safe
/// and AOT-friendly.
/// </remarks>
/// <typeparam name="T">The settings type.</typeparam>
public sealed class JsonSettingsStore<T>
    where T : class
{
    private readonly string _path;
    private readonly JsonTypeInfo<T> _typeInfo;
    private readonly IAtomicFileWriterFactory _atomicFiles;
    private readonly IFileSystem _fileSystem;

    /// <summary>The maximum settings file size a load will read.</summary>
    public const long MaxFileBytes = 16 * 1024 * 1024;

    /// <summary>Initializes a new instance of the <see cref="JsonSettingsStore{T}"/> class.</summary>
    /// <param name="path">The settings file path.</param>
    /// <param name="typeInfo">The source-generated JSON type information.</param>
    /// <param name="atomicFiles">The platform atomic-writer factory.</param>
    /// <param name="fileSystem">The platform file system, used for a bounded no-follow read.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/>, <paramref name="atomicFiles"/> or <paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    public JsonSettingsStore(
        string path,
        JsonTypeInfo<T> typeInfo,
        IAtomicFileWriterFactory atomicFiles,
        IFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(atomicFiles);
        ArgumentNullException.ThrowIfNull(fileSystem);

        _path = path;
        _typeInfo = typeInfo;
        _atomicFiles = atomicFiles;
        _fileSystem = fileSystem;
    }

    /// <summary>Gets the settings file path.</summary>
    public string Path => _path;

    /// <summary>Loads the saved settings.</summary>
    /// <returns>The settings, or <see langword="null"/> when none are saved or the file is unreadable.</returns>
    public T? Load()
    {
        // OpenNoFollow + the size bound keep a shared config directory from turning
        // a settings path into an arbitrary-file read or a FIFO/device hang.
        if (!_fileSystem.TryStat(_path, out var status) ||
            status.Type != FileType.RegularFile ||
            status.Size < 0 ||
            status.Size > MaxFileBytes)
        {
            return null;
        }

        if (!_fileSystem.TryOpenRead(_path, noFollow: true, out var handle) || handle is null)
        {
            return null;
        }

        using (handle)
        {
            try
            {
                if (!_fileSystem.TryStatDescriptor(handle, out status) ||
                    status.Type != FileType.RegularFile ||
                    status.Size < 0 ||
                    status.Size > MaxFileBytes)
                {
                    return null;
                }

                var bytes = new byte[(int)status.Size];
                var offset = 0;

                while (offset < bytes.Length)
                {
                    var read = RandomAccess.Read(handle, bytes.AsSpan(offset), offset);

                    if (read <= 0)
                    {
                        return null;
                    }

                    offset += read;
                }

                return JsonSerializer.Deserialize(bytes, _typeInfo);
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Best effort: malformed, vanished or unreadable settings start empty.
                return null;
            }
        }
    }

    /// <summary>Saves the settings, replacing any previous ones.</summary>
    /// <param name="settings">The settings to save.</param>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is <see langword="null"/>.</exception>
    public void Save(T settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (System.IO.Path.GetDirectoryName(_path) is { Length: > 0 } directory)
        {
            _atomicFiles.EnsureDirectory(directory, ownerOnly: true);
        }

        var created = _atomicFiles.Create(_path, new AtomicFileOptions { OwnerOnly = true });

        if (created.TryGetError(out var error))
        {
            throw new IOException(error.Message);
        }

        using var writer = created.Value;
        JsonSerializer.Serialize(writer.Stream, settings, _typeInfo);
        var committed = writer.Commit();

        if (committed.IsFailure)
        {
            throw new IOException(committed.Error.Message);
        }
    }

    /// <summary>Forgets the saved settings.</summary>
    public void Clear()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Clearing is best effort: a file that cannot be removed simply
            // reapplies next launch.
        }
    }
}
