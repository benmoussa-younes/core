// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Text;

/// <summary>
/// Detects a text's line-ending style. A uniform file has one style; a mixed file
/// takes the majority, which is the documented normalisation on save. A tie and a
/// text with no line break both answer <see cref="LineEnding.LineFeed"/>.
/// </summary>
public static class LineEndingDetector
{
    /// <summary>Detects the line-ending style of a text.</summary>
    /// <param name="text">The text, in its original form.</param>
    /// <returns>The majority style; line feed when there is no line break or a tie.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static LineEnding Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var carriageReturnLineFeed = 0;
        var lineFeed = 0;
        var carriageReturn = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (character == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    carriageReturnLineFeed++;
                    index++;
                }
                else
                {
                    carriageReturn++;
                }
            }
            else if (character == '\n')
            {
                lineFeed++;
            }
        }

        if (carriageReturnLineFeed > lineFeed && carriageReturnLineFeed >= carriageReturn)
        {
            return LineEnding.CarriageReturnLineFeed;
        }

        return carriageReturn > lineFeed && carriageReturn > carriageReturnLineFeed
            ? LineEnding.CarriageReturn
            : LineEnding.LineFeed;
    }
}
