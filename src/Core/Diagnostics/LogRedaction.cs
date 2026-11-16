// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Globalization;
using System.Text;

namespace Core.Diagnostics;

/// <summary>
/// Turns arbitrary text into one bounded log line: a carriage return, a line feed,
/// a tab or another control character is escaped instead of being written
/// verbatim, so untrusted input cannot forge another entry (CWE-117).
/// </summary>
public static class LogRedaction
{
    /// <summary>The default maximum length of the produced line, in characters.</summary>
    public const int DefaultMaximumLength = 4_096;

    /// <summary>
    /// Redacts one value for a single log line.
    /// </summary>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <param name="maximumLength">The maximum output length; must be positive.</param>
    /// <returns>The escaped, truncated value; never a carriage return or a line feed.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumLength"/> is not positive.</exception>
    public static string SingleLine(string? value, int maximumLength = DefaultMaximumLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 1);

        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Length <= maximumLength && !HasControlCharacter(value))
        {
            return value;
        }

        var builder = new StringBuilder(Math.Min(value.Length, maximumLength));

        foreach (var character in value)
        {
            if (builder.Length >= maximumLength)
            {
                break;
            }

            switch (character)
            {
                case '\r':
                    Append(builder, "\\r", maximumLength);
                    break;
                case '\n':
                    Append(builder, "\\n", maximumLength);
                    break;
                case '\t':
                    Append(builder, "\\t", maximumLength);
                    break;
                default:
                    if (IsUnsafe(character))
                    {
                        Append(builder, "\\u" + ((int)character).ToString("X4", CultureInfo.InvariantCulture), maximumLength);
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string text, int maximumLength)
    {
        if (builder.Length + text.Length <= maximumLength)
        {
            builder.Append(text);
        }
    }

    private static bool HasControlCharacter(string value)
    {
        foreach (var character in value)
        {
            if (IsUnsafe(character))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnsafe(char character) =>
        char.IsControl(character) ||
        character is '\u2028' or '\u2029' ||
        character is >= '\u202A' and <= '\u202E' ||
        character is >= '\u2066' and <= '\u2069';
}
