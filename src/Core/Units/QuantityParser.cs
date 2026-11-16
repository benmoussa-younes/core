// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Globalization;

namespace Core.Units;

internal static class QuantityParser
{
    internal static bool TrySplit(ReadOnlySpan<char> text, out ReadOnlySpan<char> number, out ReadOnlySpan<char> suffix)
    {
        number = default;
        suffix = default;
        text = text.Trim();
        if (text.IsEmpty)
        {
            return false;
        }

        var index = 0;
        if (text[index] == '+' || text[index] == '-')
        {
            index++;
        }

        var digits = 0;
        var hasDecimalPoint = false;
        while (index < text.Length)
        {
            var character = text[index];
            if (char.IsAsciiDigit(character))
            {
                digits++;
                index++;
            }
            else if (character == '.')
            {
                if (hasDecimalPoint)
                {
                    return false;
                }

                hasDecimalPoint = true;
                index++;
            }
            else
            {
                break;
            }
        }

        if (digits == 0 || text[index - 1] == '.')
        {
            return false;
        }

        number = text[..index];
        suffix = text[index..].Trim();
        return true;
    }

    internal static bool TryScale(ReadOnlySpan<char> number, long multiplier, out long value)
    {
        value = 0;
        if (number.IndexOf('.') < 0)
        {
            if (!long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
            {
                return false;
            }

            if (integer > long.MaxValue / multiplier || integer < long.MinValue / multiplier)
            {
                return false;
            }

            value = checked(integer * multiplier);
            return true;
        }

        if (!decimal.TryParse(
            number,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var magnitude))
        {
            return false;
        }

        if (magnitude > (decimal)long.MaxValue / multiplier || magnitude < (decimal)long.MinValue / multiplier)
        {
            return false;
        }

        var scaled = magnitude * multiplier;
        if (scaled != decimal.Truncate(scaled))
        {
            return false;
        }

        value = (long)scaled;
        return true;
    }
}
