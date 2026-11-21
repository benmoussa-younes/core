// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;

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

    /// <summary>Redacts every occurrence of the current user's home directory in a free-text message.</summary>
    /// <param name="text">The message, or <see langword="null"/>.</param>
    /// <returns>The message with each home path written <c>~</c>; null or empty is returned unchanged.</returns>
    /// <remarks>
    /// A log line is rarely a bare path: a stack trace embeds one after " in ". This
    /// replaces the prefix wherever it starts a path component, so an account name
    /// does not reach a log file.
    /// </remarks>
    public static string Scrub(string? text) =>
        Scrub(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>Redacts every occurrence of an explicit home prefix in a free-text message.</summary>
    /// <param name="text">The message, or <see langword="null"/>.</param>
    /// <param name="home">The home directory; an empty value leaves the text unchanged.</param>
    /// <returns>The message with each home path written <c>~</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="home"/> is <see langword="null"/>.</exception>
    public static string Scrub(string? text, string home)
    {
        ArgumentNullException.ThrowIfNull(home);

        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var prefix = home.TrimEnd('/');

        if (prefix.Length == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var index = 0;

        while (index < text.Length)
        {
            var found = text.IndexOf(prefix, index, StringComparison.Ordinal);

            if (found < 0)
            {
                builder.Append(text, index, text.Length - index);
                break;
            }

            builder.Append(text, index, found - index);
            var end = found + prefix.Length;

            // Only when the prefix ends a path component: /home/user must not
            // rewrite /home/user2, while a following quote, punctuation or an
            // escaped newline is a boundary and the account name must go.
            if (end == text.Length || !IsComponentCharacter(text[end]))
            {
                builder.Append('~');
                index = end;
                continue;
            }

            builder.Append(text, found, prefix.Length);
            index = end;
        }

        return builder.ToString();
    }

    private static bool IsComponentCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '-';
}
