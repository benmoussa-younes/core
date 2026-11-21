// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.UI.Platform;

// WHY: a portal or GVFS can return a remote URI. smb://server/share has IsFile
// false while LocalPath still reads /share, so returning it would name a local
// directory the operator never picked; file://server/share has IsFile true but is
// a UNC authority, whose LocalPath is \\server\share on Linux; a relative URI
// throws from LocalPath. The rule is therefore "absolute, file, not UNC".
internal static class StorageItemPaths
{
    // A file path is only local when the URI has no authority: file:///home/x.
    public static string? File(Uri path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return IsLocal(path) ? path.LocalPath : null;
    }

    // A folder URI carries a trailing separator; a caller combining it with a child
    // name should not get a double separator. The root stays "/".
    public static string? Folder(Uri path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!IsLocal(path))
        {
            return null;
        }

        var local = path.LocalPath;

        return string.IsNullOrEmpty(local) ? null : Path.TrimEndingDirectorySeparator(local);
    }

    private static bool IsLocal(Uri path) =>
        path is { IsAbsoluteUri: true, IsFile: true, IsUnc: false };
}
