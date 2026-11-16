// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Primitives;

/// <summary>
/// Shortens a path for a user-facing message: a path below the user's home is
/// written <c>~/…</c>, so a UI or a log line does not expose the account name.
/// The full path stays available to the caller that owns it.
/// </summary>
public static class PathRedaction
{
    /// <summary>Redacts the current user's home prefix.</summary>
    /// <param name="path">The path, or <see langword="null"/>.</param>
    /// <returns>The redacted path; a null or empty path is returned unchanged.</returns>
    public static string ForMessage(string? path) =>
        ForMessage(path, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>Redacts an explicit home prefix.</summary>
    /// <param name="path">The path.</param>
    /// <param name="home">The home directory; an empty value leaves the path unchanged.</param>
    /// <returns>The redacted path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="home"/> is <see langword="null"/>.</exception>
    public static string ForMessage(string? path, string home)
    {
        ArgumentNullException.ThrowIfNull(home);

        if (string.IsNullOrEmpty(path))
        {
            return path ?? string.Empty;
        }

        var prefix = home.TrimEnd('/');

        if (prefix.Length == 0)
        {
            return path;
        }

        if (string.Equals(path, prefix, StringComparison.Ordinal))
        {
            return "~";
        }

        return path.Length > prefix.Length &&
            path.StartsWith(prefix, StringComparison.Ordinal) &&
            path[prefix.Length] == '/'
                ? string.Concat("~", path.AsSpan(prefix.Length))
                : path;
    }
}
