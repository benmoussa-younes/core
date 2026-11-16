// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers;
using System.Globalization;
using Core.Results;
using Core.Serialization.Documents;
using Core.Serialization.Values;

namespace Core.Serialization.Codecs.Csv;

/// <summary>
/// Writes a <see cref="DataDocument"/> as CSV: the header is the column names and
/// each row is a record, in the given <see cref="CsvDialect"/>. A field is quoted
/// when it holds the delimiter, the quote or a newline; the quote is doubled.
/// </summary>
/// <remarks>
/// CSV is a single table, so a document with sections is refused rather than
/// losing data, and a column of nested values has no CSV form. The dialect selects
/// the delimiter, the quote, the line ending and whether a byte-order mark is
/// written, so a file reopens exactly as it was saved. A temporal value is written
/// in the canonical CSV form (<c>yyyy-MM-dd</c>, <c>HH:mm:ss</c> and a naive
/// <c>yyyy-MM-ddTHH:mm:ss</c>, with the fractional part only when non-zero), so a
/// timestamp that carried an offset, a <c>Z</c> or a non-UTC
/// <see cref="DateTime.Kind"/> is normalised to that form on write.
/// </remarks>
public static class CsvDataWriter
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>Writes a document to a buffer with the default dialect.</summary>
    /// <param name="document">The document; must not be <see langword="null"/>.</param>
    /// <param name="writer">The destination buffer; must not be <see langword="null"/>.</param>
    /// <returns>A successful result whose value is always <see langword="true"/>,
    /// or a failure with code <c>serialization.invalid_document</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="writer"/> is <see langword="null"/>.</exception>
    public static Result<bool> Write(DataDocument document, IBufferWriter<byte> writer) =>
        Write(document, writer, CsvDialect.Default);

    /// <summary>Writes a document to a buffer with a dialect.</summary>
    /// <param name="document">The document; must not be <see langword="null"/>.</param>
    /// <param name="writer">The destination buffer; must not be <see langword="null"/>.</param>
    /// <param name="dialect">The dialect; must not be <see langword="null"/>.</param>
    /// <returns>A successful result whose value is always <see langword="true"/>,
    /// or a failure with code <c>serialization.invalid_document</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/>, <paramref name="writer"/> or <paramref name="dialect"/> is <see langword="null"/>.</exception>
    public static Result<bool> Write(DataDocument document, IBufferWriter<byte> writer, CsvDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(dialect);

        if (document.Sections.Count > 0)
        {
            return Result<bool>.Failure(
                new CoreError("serialization.invalid_document", "CSV holds a single table; a document with sections cannot be written."));
        }

        var table = document.Root;

        for (var column = 0; column < table.ColumnCount; column++)
        {
            if (table.Column(column).Kind == DataKind.Node)
            {
                return Result<bool>.Failure(
                    new CoreError("serialization.invalid_document", "CSV cannot hold a column of nested values."));
            }
        }

        // A table with no columns has no CSV form; write nothing so an empty
        // document round-trips to an empty file.
        if (table.ColumnCount == 0)
        {
            return Result<bool>.Success(true);
        }

        if (dialect.WriteBom)
        {
            var bom = writer.GetSpan(Bom.Length);
            Bom.CopyTo(bom);
            writer.Advance(Bom.Length);
        }

        var delimiter = (byte)dialect.Delimiter;

        if (dialect.HasHeader)
        {
            for (var column = 0; column < table.ColumnCount; column++)
            {
                if (column > 0)
                {
                    Utf8Buffer.WriteByte(writer, delimiter);
                }

                WriteField(writer, table.Schema[column].Name, dialect);
            }

            Utf8Buffer.Write(writer, dialect.LineEnding);
        }

        for (var row = 0; row < table.RowCount; row++)
        {
            for (var column = 0; column < table.ColumnCount; column++)
            {
                if (column > 0)
                {
                    Utf8Buffer.WriteByte(writer, delimiter);
                }

                var value = table[row, column];

                if (value.IsNull)
                {
                    WriteField(writer, string.Empty, dialect);
                }
                else if (value.Kind is DataKind.Date or DataKind.Time or DataKind.Timestamp)
                {
                    WriteField(writer, ToCsvTemporal(value), dialect);
                }
                else
                {
                    WriteField(writer, value.ToInvariantText(), dialect);
                }
            }

            Utf8Buffer.Write(writer, dialect.LineEnding);
        }

        return Result<bool>.Success(true);
    }

    /// <summary>Writes a document to a new UTF-8 byte array with the default dialect.</summary>
    /// <param name="document">The document; must not be <see langword="null"/>.</param>
    /// <returns>The UTF-8 bytes, or a failure from <see cref="Write(DataDocument, IBufferWriter{byte})"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static Result<byte[]> WriteToUtf8Bytes(DataDocument document) =>
        WriteToUtf8Bytes(document, CsvDialect.Default);

    /// <summary>Writes a document to a new UTF-8 byte array with a dialect.</summary>
    /// <param name="document">The document; must not be <see langword="null"/>.</param>
    /// <param name="dialect">The dialect; must not be <see langword="null"/>.</param>
    /// <returns>The UTF-8 bytes, or a failure from <see cref="Write(DataDocument, IBufferWriter{byte}, CsvDialect)"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="dialect"/> is <see langword="null"/>.</exception>
    public static Result<byte[]> WriteToUtf8Bytes(DataDocument document, CsvDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(dialect);

        return Utf8Buffer.ToUtf8Bytes((document, dialect), static (state, writer) => Write(state.document, writer, state.dialect));
    }

    // A temporal value is written in the consumer's canonical CSV form rather
    // than the JSON-oriented ToInvariantText: a date is yyyy-MM-dd, a time is
    // HH:mm:ss and a timestamp is a naive yyyy-MM-ddTHH:mm:ss, each with the
    // fractional part only when it is non-zero (up to seven digits, trailing
    // zeros trimmed). A timestamp's offset, Z or Kind suffix is dropped, so a
    // non-canonical temporal input is normalised on write. The caller only calls
    // this for the three temporal kinds.
    private static string ToCsvTemporal(DataValue value)
    {
        if (value.Kind == DataKind.Date)
        {
            _ = value.TryGetDate(out var date);
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (value.Kind == DataKind.Time)
        {
            _ = value.TryGetTime(out var time);
            return Fractional(time.ToString("HH:mm:ss", CultureInfo.InvariantCulture), time.Ticks);
        }

        _ = value.TryGetTimestamp(out var timestamp);
        return Fractional(timestamp.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), timestamp.Ticks);
    }

    private static string Fractional(string wholeSeconds, long ticks)
    {
        var fraction = ticks % TimeSpan.TicksPerSecond;

        return fraction == 0
            ? wholeSeconds
            : string.Concat(wholeSeconds, ".", fraction.ToString("D7", CultureInfo.InvariantCulture).TrimEnd('0'));
    }

    private static void WriteField(IBufferWriter<byte> writer, string text, CsvDialect dialect)
    {
        if (!dialect.HasQuote || !NeedsQuoting(text, dialect))
        {
            Utf8Buffer.Write(writer, text);
            return;
        }

        var quote = (byte)dialect.Quote;
        Utf8Buffer.WriteByte(writer, quote);
        Utf8Buffer.Write(writer, text.Replace(new string(dialect.Quote, 1), new string(dialect.Quote, 2), StringComparison.Ordinal));
        Utf8Buffer.WriteByte(writer, quote);
    }

    private static bool NeedsQuoting(string text, CsvDialect dialect)
    {
        foreach (var character in text)
        {
            if (character == dialect.Delimiter || character == dialect.Quote || character == '\n' || character == '\r')
            {
                return true;
            }
        }

        return false;
    }
}
