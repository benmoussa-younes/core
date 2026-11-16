// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Serialization.Codecs.Csv;

/// <summary>
/// Infers the field delimiter of a CSV sample by consistency: the right
/// delimiter is the one that yields the same field count on the most records. A
/// wrong guess corrupts the whole sheet, so the detection is conservative and
/// falls back to <see cref="CsvDialect.Default"/> when no candidate separates
/// more than one field.
/// </summary>
/// <remarks>
/// Only the leading <see cref="MaximumSampleLength"/> bytes are read: detection
/// is a heuristic on a sample, so scanning a whole multi-megabyte file for it
/// would dominate the parse. The sample is scanned as bytes, never decoded to a
/// string, and a delimiter inside quotes is data, with a doubled quote an
/// escaped quote. The quote (<c>"</c>) and <see cref="CsvDialect.HasHeader"/>
/// are assumed.
/// </remarks>
public static class CsvDialectDetector
{
    /// <summary>The number of leading bytes used to infer the dialect.</summary>
    public const int MaximumSampleLength = 64 * 1024;

    /// <summary>The delimiters considered, most likely first.</summary>
    private static readonly byte[] Candidates = [(byte)',', (byte)';', (byte)'\t', (byte)'|'];

    /// <summary>Infers the dialect of a CSV sample.</summary>
    /// <param name="utf8">The sample bytes; it may be a whole file, of which only the leading sample is read.</param>
    /// <returns>The inferred dialect, or <see cref="CsvDialect.Default"/>.</returns>
    public static CsvDialect Detect(ReadOnlySpan<byte> utf8)
    {
        var sample = utf8.Length <= MaximumSampleLength ? utf8 : utf8[..MaximumSampleLength];

        var best = CsvDialect.Default;
        var bestScore = 0;
        var quote = (byte)CsvDialect.Default.Quote;

        foreach (var candidate in Candidates)
        {
            var score = Score(sample, candidate, quote);

            if (score > bestScore)
            {
                bestScore = score;
                best = new CsvDialect(delimiter: (char)candidate);
            }
        }

        return best;
    }

    /// <summary>
    /// Scores a candidate delimiter: the number of records whose field count
    /// equals the most common field count, or zero when the delimiter never
    /// separates more than one field.
    /// </summary>
    private static int Score(ReadOnlySpan<byte> sample, byte delimiter, byte quote)
    {
        var counts = new Dictionary<int, int>();
        var records = 0;
        var start = 0;

        while (start <= sample.Length)
        {
            var newline = sample[start..].IndexOf((byte)'\n');
            var line = newline < 0 ? sample[start..] : sample.Slice(start, newline);
            start = newline < 0 ? sample.Length + 1 : start + newline + 1;

            if (!IsWhiteSpace(line))
            {
                line = TrimTrailingCarriageReturn(line);
            }

            if (IsWhiteSpace(line))
            {
                continue;
            }

            var fields = CountFields(line, delimiter, quote);
            counts[fields] = counts.TryGetValue(fields, out var seen) ? seen + 1 : 1;
            records++;
        }

        if (records == 0)
        {
            return 0;
        }

        var modal = 0;
        var modalCount = 0;

        foreach (var pair in counts)
        {
            if (pair.Value > modalCount || (pair.Value == modalCount && pair.Key > modal))
            {
                modal = pair.Key;
                modalCount = pair.Value;
            }
        }

        return modal <= 1 ? 0 : modalCount;
    }

    /// <summary>
    /// Counts the fields of one physical record, treating the delimiter inside
    /// quotes as data and a doubled quote as an escaped quote.
    /// </summary>
    private static int CountFields(ReadOnlySpan<byte> line, byte delimiter, byte quote)
    {
        var fields = 1;
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var value = line[index];

            if (value == quote)
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == quote)
                {
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && value == delimiter)
            {
                fields++;
            }
        }

        return fields;
    }

    private static bool IsWhiteSpace(ReadOnlySpan<byte> line)
    {
        foreach (var value in line)
        {
            if (value is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\v' or (byte)'\f'))
            {
                return false;
            }
        }

        return true;
    }

    private static ReadOnlySpan<byte> TrimTrailingCarriageReturn(ReadOnlySpan<byte> line)
    {
        while (!line.IsEmpty && line[^1] == (byte)'\r')
        {
            line = line[..^1];
        }

        return line;
    }
}
