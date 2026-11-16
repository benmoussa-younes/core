// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Globalization;

namespace Core.Units;

internal static class QuantityFormatter
{
    internal static string ToHumanString(long value, ulong[] multipliers, string[] symbols)
    {
        var negative = value < 0;
        var magnitude = negative ? (ulong)(-(value + 1)) + 1UL : (ulong)value;

        var unit = 0;
        for (var candidate = multipliers.Length - 1; candidate >= 0; candidate--)
        {
            if (magnitude >= multipliers[candidate])
            {
                unit = candidate;
                break;
            }
        }

        var multiplier = multipliers[unit];
        var whole = magnitude / multiplier;
        var remainder = magnitude % multiplier;
        var tenths = (int)((remainder * 10 + (multiplier / 2)) / multiplier);
        if (tenths >= 10)
        {
            tenths -= 10;
            whole++;
        }

        if (whole >= 1000UL && unit < multipliers.Length - 1)
        {
            unit++;
            whole /= 1000UL;
        }

        var sign = negative ? "-" : string.Empty;
        return tenths == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{sign}{whole} {symbols[unit]}")
            : string.Create(CultureInfo.InvariantCulture, $"{sign}{whole}.{tenths} {symbols[unit]}");
    }
}
