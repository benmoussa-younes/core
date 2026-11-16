// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text.RegularExpressions;

namespace Core.Security;

/// <summary>
/// Removes the common secret shapes from a diagnostic line before it is written
/// to a crash log or the console: <c>password=...</c>, <c>token: ...</c>,
/// <c>authorization=...</c> and the user-info part of a URI.
/// </summary>
/// <remarks>
/// This is a defence-in-depth scrub, not a parser: a custom secret format that
/// does not contain one of these keys is still the caller's responsibility. The
/// regexes have a timeout so a long hostile line cannot stall the crash path.
/// </remarks>
public static class SecretRedaction
{
    private static readonly Regex KeyValue = new(
        @"\b(password|passwd|pwd|token|secret|authorization|api[_-]?key|access[_-]?key|private[_-]?key)\b\s*[:=]\s*[^\r\n,;]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex UriUserInfo = new(
        @"\b([a-z][a-z0-9+.-]*://)[^/\s@]+@",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>Scrubs the common secret shapes from one line.</summary>
    /// <param name="value">The line, or <see langword="null"/>.</param>
    /// <returns>The scrubbed line; null and empty stay empty.</returns>
    public static string Scrub(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        try
        {
            var withoutKeyValues = KeyValue.Replace(value, "$1=[redacted]");
            return UriUserInfo.Replace(withoutKeyValues, "$1[redacted]@");
        }
        catch (RegexMatchTimeoutException)
        {
            // A crash path must never throw because a regex was slow; drop the
            // whole line rather than write a possible secret.
            return "[redacted]";
        }
    }
}
