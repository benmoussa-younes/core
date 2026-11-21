// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.UI.Platform;

/// <summary>
/// Maps a picked storage URI to a local path, or to <see langword="null"/> when the
/// URI does not name one.
/// </summary>
/// <remarks>
/// A portal or GVFS can return a remote URI: <c>smb://server/share</c> has
/// <see cref="Uri.IsFile"/> false while <see cref="Uri.LocalPath"/> still reads
/// <c>/share</c>, so returning it would name a local directory the operator never
/// picked. A relative URI throws from <see cref="Uri.LocalPath"/>.
/// </remarks>
internal static class StorageItemPaths
{
    /// <summary>Gets the local path of a picked file, or <see langword="null"/>.</summary>
    /// <param name="path">The URI the picker returned.</param>
    /// <returns>The local path, or <see langword="null"/> for a remote or relative URI.</returns>
    public static string? File(Uri path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path is { IsAbsoluteUri: true, IsFile: true } ? path.LocalPath : null;
    }

    /// <summary>Gets the local path of a picked folder, without its trailing separator.</summary>
    /// <param name="path">The URI the picker returned.</param>
    /// <returns>The local path, or <see langword="null"/> for a remote or relative URI.</returns>
    public static string? Folder(Uri path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path is not { IsAbsoluteUri: true, IsFile: true })
        {
            return null;
        }

        var local = path.LocalPath;

        // A folder URI carries a trailing separator; a caller combining it with a
        // child name should not get a double separator. The root stays "/".
        return string.IsNullOrEmpty(local) ? null : Path.TrimEndingDirectorySeparator(local);
    }
}
