// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Text;

namespace Core.Serialization.Codecs.Csv;

/// <summary>
/// Splits CSV bytes into records of fields, handling RFC 4180 quoting: a quoted
/// field may contain the delimiter and newlines, and a doubled quote is one
/// literal quote. The reader is lenient — it never throws on malformed input; a
/// quoted field without its closing quote runs to the end of the record.
/// </summary>
/// <remarks>
/// The reader owns a reusable field buffer, so parsing a large document does not
/// allocate per record. It does not own the bytes; pass the same buffer to
/// <see cref="TryRead"/> and <see cref="Decode"/>. The delimiter and quote come
/// from the <see cref="CsvDialect"/>.
/// </remarks>
internal sealed class CsvRecordReader
{
    private readonly List<CsvField> _fields = [];
    private readonly byte _delimiter;
    private readonly byte _quote;
    private readonly bool _hasQuote;
    private int _position;
    private bool _terminated;

    /// <summary>Initializes a reader for a dialect.</summary>
    /// <param name="dialect">The dialect whose separators to honour.</param>
    internal CsvRecordReader(CsvDialect dialect)
    {
        _delimiter = (byte)dialect.Delimiter;
        _hasQuote = dialect.HasQuote;
        _quote = _hasQuote ? (byte)dialect.Quote : (byte)0;
    }

    /// <summary>One field: a byte range, with quotes if the field was quoted.</summary>
    /// <param name="Start">The field's start offset.</param>
    /// <param name="Length">The field's length, including the surrounding quotes when quoted.</param>
    /// <param name="Quoted">Whether the field was quoted.</param>
    internal readonly record struct CsvField(int Start, int Length, bool Quoted);

    /// <summary>Reads the next record.</summary>
    /// <param name="data">The CSV bytes.</param>
    /// <param name="fields">The fields of the record, valid until the next call.</param>
    /// <returns><see langword="true"/> when a record was read.</returns>
    internal bool TryRead(ReadOnlySpan<byte> data, out ReadOnlySpan<CsvField> fields)
    {
        _fields.Clear();

        if (_position >= data.Length)
        {
            fields = default;
            return false;
        }

        while (true)
        {
            _fields.Add(ReadField(data));

            if (_position >= data.Length)
            {
                break;
            }

            if (data[_position] == _delimiter)
            {
                _position++;

                // A delimiter at the very end is a trailing empty field.
                if (_position >= data.Length)
                {
                    _fields.Add(new CsvField(_position, 0, false));
                    break;
                }

                continue;
            }

            var terminator = data[_position];
            _position++;

            if (terminator == (byte)'\r' && _position < data.Length && data[_position] == (byte)'\n')
            {
                _position++;
            }

            _terminated = true;
            break;
        }

        fields = CollectionsMarshal.AsSpan(_fields);
        return true;
    }

    /// <summary>
    /// Reads one record from the start of <paramref name="data"/>, for the windowed
    /// reader: it reports how many bytes the record consumed and whether the record
    /// is complete, so a caller can keep the tail as a carry buffer.
    /// </summary>
    /// <param name="data">The bytes available so far.</param>
    /// <param name="atEof">Whether no more bytes will follow.</param>
    /// <param name="fields">The fields of the record, valid until the next call.</param>
    /// <param name="consumed">The number of bytes the record used.</param>
    /// <param name="complete">Whether the record is finished; when false, read more.</param>
    /// <returns><see langword="true"/> when at least one field was read.</returns>
    internal bool TryReadRecord(
        ReadOnlySpan<byte> data,
        bool atEof,
        out ReadOnlySpan<CsvField> fields,
        out int consumed,
        out bool complete)
    {
        _position = 0;
        _terminated = false;

        var read = TryRead(data, out fields);
        consumed = _position;

        // A record is complete when it hit a terminator, or the input ended (the
        // lenient last record). A trailing '\r' mid-stream is incomplete because its
        // '\n' may be in the next window.
        complete = read && (_terminated || atEof);

        if (complete && !atEof && consumed > 0 && data[consumed - 1] == (byte)'\r')
        {
            complete = false;
        }

        return read;
    }

    /// <summary>Decodes a field to a string, unescaping doubled quotes.</summary>
    /// <param name="data">The CSV bytes.</param>
    /// <param name="field">The field.</param>
    /// <param name="quote">The quote byte, or zero when quoting is disabled.</param>
    /// <returns>The decoded text.</returns>
    internal static string Decode(ReadOnlySpan<byte> data, CsvField field, byte quote)
    {
        var span = data.Slice(field.Start, field.Length);

        if (!field.Quoted)
        {
            return Encoding.UTF8.GetString(span);
        }

        if (span.Length >= 2 && span[0] == quote && span[^1] == quote)
        {
            span = span[1..^1];
        }
        else if (span.Length >= 1 && span[0] == quote)
        {
            span = span[1..];
        }

        return span.Contains(quote)
            ? Encoding.UTF8.GetString(span).Replace(new string((char)quote, 2), new string((char)quote, 1), StringComparison.Ordinal)
            : Encoding.UTF8.GetString(span);
    }

    private CsvField ReadField(ReadOnlySpan<byte> data)
    {
        if (_hasQuote && data[_position] == _quote)
        {
            var start = _position;
            _position++;

            while (_position < data.Length)
            {
                if (data[_position] == _quote)
                {
                    if (_position + 1 < data.Length && data[_position + 1] == _quote)
                    {
                        _position += 2;
                        continue;
                    }

                    _position++;
                    break;
                }

                _position++;
            }

            var end = _position;

            // Lenient: anything between the closing quote and the delimiter is
            // consumed and dropped, so a stray character does not corrupt the row.
            while (_position < data.Length &&
                data[_position] != _delimiter &&
                data[_position] != (byte)'\n' &&
                data[_position] != (byte)'\r')
            {
                _position++;
            }

            return new CsvField(start, end - start, true);
        }

        var fieldStart = _position;

        while (_position < data.Length &&
            data[_position] != _delimiter &&
            data[_position] != (byte)'\n' &&
            data[_position] != (byte)'\r')
        {
            _position++;
        }

        return new CsvField(fieldStart, _position - fieldStart, false);
    }
}
