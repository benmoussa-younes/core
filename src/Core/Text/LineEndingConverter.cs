// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Text;

/// <summary>
/// Converts between the line-feed text an editor works on and the file's own
/// line-ending style, so an unedited document is written back byte-identically.
/// </summary>
public static class LineEndingConverter
{
    /// <summary>Rewrites every line break as a line feed.</summary>
    /// <param name="text">The text, in its original form.</param>
    /// <returns>The text with line-feed line breaks.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static string ToLineFeed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Contains('\r')
            ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            : text;
    }

    /// <summary>Rewrites line feeds as the given style's line break.</summary>
    /// <param name="text">The text with line-feed line breaks.</param>
    /// <param name="lineEnding">The style to restore.</param>
    /// <returns>The text with the requested line break.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static string FromLineFeed(string text, LineEnding lineEnding)
    {
        ArgumentNullException.ThrowIfNull(text);

        return lineEnding switch
        {
            LineEnding.CarriageReturnLineFeed => text.Replace("\n", "\r\n", StringComparison.Ordinal),
            LineEnding.CarriageReturn => text.Replace('\n', '\r'),
            _ => text,
        };
    }
}
