// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Core.IO.Mapped;
using Core.Results;
using Core.Serialization.Documents;
using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Codecs.Csv;

/// <summary>
/// Reads a <see cref="DataDocument"/> from CSV: the first record is the header,
/// each later record is a row, and the column kind is inferred from every data
/// value (all-boolean, all-integer, any decimal, a temporal shape, any number,
/// else text).
/// </summary>
/// <remarks>
/// CSV is a single table: the document has a root table and no sections. An
/// empty field is a null cell. Quoting follows RFC 4180 — a quoted field may hold
/// the delimiter, newlines and doubled quotes. The reader makes two passes over
/// the mapped bytes: the first classifies every field with
/// <see cref="Utf8Parser"/> without allocating a string, the second builds the
/// columnar table directly with a reused row buffer. A numeric field is never
/// decoded to a string, and repeated text is interned in a bounded pool, so a
/// large table does not hold two copies of its data.
/// </remarks>
public static class CsvDataReader
{
    private const int InterningCapacity = 8192;
    private const int DefaultWindowChunk = 1 << 16;
    private const int MaxRecordBytes = 16_777_216;

    /// <summary>The maximum number of cells (rows × columns) a CSV document may materialize.</summary>
    public const int MaxCells = 4_000_000;

    /// <summary>
    /// The maximum number of columns a CSV document may declare. A record that
    /// declares more fails with <c>serialization.too_many_columns</c> rather than
    /// allocating arrays proportional to an attacker-controlled field count.
    /// </summary>
    public const int MaxColumns = 65_536;

    /// <summary>Reads a document from UTF-8 CSV with the default dialect.</summary>
    /// <param name="utf8">The UTF-8 CSV bytes.</param>
    /// <returns>The document, or a failure with code <c>serialization.uneven_row</c>
    /// or <c>serialization.invalid_document</c>.</returns>
    public static Result<DataDocument> Read(ReadOnlySpan<byte> utf8) => Read(utf8, CsvDialect.Default);

    /// <summary>Reads a document from UTF-8 CSV with a dialect.</summary>
    /// <param name="utf8">The UTF-8 CSV bytes.</param>
    /// <param name="dialect">The dialect (delimiter, quote, header); must not be <see langword="null"/>.</param>
    /// <returns>The document, or a failure with code <c>serialization.uneven_row</c>
    /// or <c>serialization.invalid_document</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dialect"/> is <see langword="null"/>.</exception>
    public static Result<DataDocument> Read(ReadOnlySpan<byte> utf8, CsvDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF)
        {
            utf8 = utf8[3..];
        }

        var quote = dialect.HasQuote ? (byte)dialect.Quote : (byte)0;
        var reader = new CsvRecordReader(dialect);
        string[]? header = null;
        ColumnKindAccumulator[]? kinds = null;
        long cells = 0;

        while (reader.TryRead(utf8, out var fields))
        {
            if (header is null)
            {
                if (fields.Length > MaxColumns)
                {
                    return Result<DataDocument>.Failure(TooManyColumns());
                }

                if (dialect.HasHeader)
                {
                    header = new string[fields.Length];

                    for (var index = 0; index < fields.Length; index++)
                    {
                        header[index] = CsvRecordReader.Decode(utf8, fields[index], quote);
                    }

                    kinds = new ColumnKindAccumulator[header.Length];
                    continue;
                }

                // No header: name the columns and classify this first record too.
                header = GenerateHeader(fields.Length);
                kinds = new ColumnKindAccumulator[header.Length];
            }

            if (fields.Length != header.Length)
            {
                return Result<DataDocument>.Failure(UnEven(header.Length, fields.Length));
            }

            cells += fields.Length;

            if (cells > MaxCells)
            {
                return Result<DataDocument>.Failure(TooManyCells());
            }

            for (var index = 0; index < fields.Length; index++)
            {
                kinds![index].Add(Classify(utf8, fields[index], quote));
            }
        }

        if (header is null)
        {
            return Result<DataDocument>.Success(new DataDocument(new DataTable(string.Empty, DataSchema.Empty)));
        }

        var fieldsArray = BuildFields(header, kinds!);
        var created = CreateTable(fieldsArray);

        if (created.IsFailure)
        {
            return Result<DataDocument>.Failure(created.Error);
        }

        Build(utf8, created.Value, fieldsArray, dialect, quote);

        return Result<DataDocument>.Success(DataDocument.FromTable(created.Value));
    }

    private static DataField[] BuildFields(string[] header, ColumnKindAccumulator[] kinds)
    {
        var fields = new DataField[header.Length];

        for (var column = 0; column < header.Length; column++)
        {
            fields[column] = new DataField(header[column], kinds[column].Resolve());
        }

        return fields;
    }

    private static Result<DataTable> CreateTable(DataField[] fields)
    {
        try
        {
            return Result<DataTable>.Success(new DataTable(string.Empty, new DataSchema(fields)));
        }
        catch (ArgumentException exception)
        {
            return Result<DataTable>.Failure(new CoreError("serialization.invalid_document", exception.Message));
        }
    }

    private static void FillRow(
        ReadOnlySpan<byte> record,
        ReadOnlySpan<CsvRecordReader.CsvField> fields,
        DataField[] fieldsArray,
        DataValue[] row,
        Utf8StringPool pool,
        byte quote)
    {
        for (var column = 0; column < row.Length; column++)
        {
            row[column] = ReadValue(record, fields[column], fieldsArray[column].Kind, pool, quote);
        }
    }

    private static string[] GenerateHeader(int count)
    {
        var header = new string[count];

        for (var index = 0; index < count; index++)
        {
            header[index] = $"Column{index + 1}";
        }

        return header;
    }

    private static void Build(
        ReadOnlySpan<byte> utf8,
        DataTable table,
        DataField[] fields,
        CsvDialect dialect,
        byte quote)
    {
        var reader = new CsvRecordReader(dialect);
        var pool = new Utf8StringPool(InterningCapacity);
        var row = new DataValue[fields.Length];
        var first = dialect.HasHeader;

        while (reader.TryRead(utf8, out var record))
        {
            if (first)
            {
                first = false;
                continue;
            }

            FillRow(utf8, record, fields, row, pool, quote);
            table.AddRow(row);
        }
    }

    /// <summary>Reads a document from a memory-mapped CSV file, default dialect.</summary>
    /// <param name="file">The mapping to read; must not be <see langword="null"/>.</param>
    /// <returns>The document, or a failure from <see cref="Read(ReadOnlySpan{byte})"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is <see langword="null"/>.</exception>
    public static Result<DataDocument> Read(MappedFile file) => Read(file, CsvDialect.Default);

    /// <summary>Reads a document from a memory-mapped CSV file with a dialect.</summary>
    /// <param name="file">The mapping to read; must not be <see langword="null"/>.</param>
    /// <param name="dialect">The dialect (delimiter, quote, header); must not be <see langword="null"/>.</param>
    /// <returns>The document, or a failure from <see cref="Read(ReadOnlySpan{byte})"/>.</returns>
    /// <remarks>
    /// A file that fits a single span is read zero-copy over that span; a larger file
    /// is read in windows with a carry buffer, so the whole file never needs one
    /// contiguous span.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> or <paramref name="dialect"/> is <see langword="null"/>.</exception>
    public static Result<DataDocument> Read(MappedFile file, CsvDialect dialect) => ReadMapped(file, dialect, windowChunk: 0);

    /// <summary>
    /// Reads a mapped file, choosing the zero-copy span path (when the file fits and
    /// <paramref name="windowChunk"/> is zero) or the windowed path.
    /// </summary>
    /// <param name="file">The mapping to read; must not be <see langword="null"/>.</param>
    /// <param name="dialect">The dialect; must not be <see langword="null"/>.</param>
    /// <param name="windowChunk">The window size, or zero to use the span path when it fits.</param>
    /// <returns>The document, or a failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> or <paramref name="dialect"/> is <see langword="null"/>.</exception>
    internal static Result<DataDocument> ReadMapped(MappedFile file, CsvDialect dialect, int windowChunk) =>
        ReadMapped(file, dialect, windowChunk, MaxRecordBytes);

    internal static Result<DataDocument> ReadMapped(MappedFile file, CsvDialect dialect, int windowChunk, int maxRecordBytes)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(dialect);

        if (windowChunk == 0 && file.Length <= int.MaxValue)
        {
            return Read(file.Slice(0, (int)file.Length), dialect);
        }

        // The windowed path skips a leading BOM, like the span path.
        var start = HasBom(file) ? 3 : 0;
        return ReadWindowed(file, dialect, windowChunk == 0 ? DefaultWindowChunk : windowChunk, start, maxRecordBytes);
    }

    private static bool HasBom(MappedFile file)
    {
        if (file.Length < 3)
        {
            return false;
        }

        var head = file.Slice(0, 3);
        return head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
    }

    private static Result<DataDocument> ReadWindowed(MappedFile file, CsvDialect dialect, int windowChunk, long start, int maxRecordBytes)
    {
        var quote = dialect.HasQuote ? (byte)dialect.Quote : (byte)0;
        string[]? header = null;
        ColumnKindAccumulator[]? kinds = null;
        long cells = 0;
        CoreError? error = null;

        var scanError = Scan(file, dialect, windowChunk, start, maxRecordBytes, (record, fields) =>
        {
            if (header is null)
            {
                if (fields.Length > MaxColumns)
                {
                    error = TooManyColumns();
                    return false;
                }

                if (dialect.HasHeader)
                {
                    header = new string[fields.Length];

                    for (var index = 0; index < fields.Length; index++)
                    {
                        header[index] = CsvRecordReader.Decode(record, fields[index], quote);
                    }

                    kinds = new ColumnKindAccumulator[header.Length];
                    return true;
                }

                header = GenerateHeader(fields.Length);
                kinds = new ColumnKindAccumulator[header.Length];
            }

            if (fields.Length != header.Length)
            {
                error = UnEven(header.Length, fields.Length);
                return false;
            }

            cells += fields.Length;

            if (cells > MaxCells)
            {
                error = TooManyCells();
                return false;
            }

            for (var index = 0; index < fields.Length; index++)
            {
                kinds![index].Add(Classify(record, fields[index], quote));
            }

            return true;
        });

        if (error is not null)
        {
            return Result<DataDocument>.Failure(error);
        }

        if (scanError is not null)
        {
            return Result<DataDocument>.Failure(scanError);
        }

        if (header is null)
        {
            return Result<DataDocument>.Success(new DataDocument(new DataTable(string.Empty, DataSchema.Empty)));
        }

        var fieldsArray = BuildFields(header, kinds!);
        var created = CreateTable(fieldsArray);

        if (created.IsFailure)
        {
            return Result<DataDocument>.Failure(created.Error);
        }

        var table = created.Value;
        var pool = new Utf8StringPool(InterningCapacity);
        var row = new DataValue[fieldsArray.Length];
        var first = dialect.HasHeader;

        // The first pass already proved every record fits the cap, so the second
        // pass cannot exceed it; the return value is not an error here.
        _ = Scan(file, dialect, windowChunk, start, maxRecordBytes, (record, fields) =>
        {
            if (first)
            {
                first = false;
                return true;
            }

            FillRow(record, fields, fieldsArray, row, pool, quote);
            table.AddRow(row);
            return true;
        });

        return Result<DataDocument>.Success(DataDocument.FromTable(table));
    }

    // Walks the mapping in windows, keeping the tail of an unterminated record as a
    // carry buffer, so a record (or a quoted field) may span windows. The consumer
    // returns false to stop the scan.
    private static CoreError? Scan(
        MappedFile file,
        CsvDialect dialect,
        int windowChunk,
        long start,
        int maxRecordBytes,
        RecordConsumer onRecord)
    {
        var reader = new CsvRecordReader(dialect);
        var buffer = new byte[Math.Max(windowChunk, 256)];
        var length = 0;
        long offset = start;

        while (true)
        {
            var atEof = offset >= file.Length;

            if (reader.TryReadRecord(buffer.AsSpan(0, length), atEof, out var fields, out var consumed, out var complete)
                && complete)
            {
                if (!onRecord(buffer.AsSpan(0, consumed), fields))
                {
                    return null;
                }

                var remaining = length - consumed;
                Array.Copy(buffer, consumed, buffer, 0, remaining);
                length = remaining;
                continue;
            }

            if (atEof)
            {
                return null;
            }

            var take = (int)Math.Min(windowChunk, file.Length - offset);

            // A record (an unterminated quoted field) must not grow the carry buffer
            // without bound from an attacker-controlled input.
            if (length + (long)take > maxRecordBytes)
            {
                return TooLarge();
            }

            if (length + take > buffer.Length)
            {
                var grown = new byte[Math.Max(buffer.Length * 2, length + take)];
                Array.Copy(buffer, grown, length);
                buffer = grown;
            }

            file.Slice(offset, take).CopyTo(buffer.AsSpan(length));
            length += take;
            offset += take;
        }
    }

    private delegate bool RecordConsumer(ReadOnlySpan<byte> record, ReadOnlySpan<CsvRecordReader.CsvField> fields);


    private static DataKind Classify(ReadOnlySpan<byte> utf8, CsvRecordReader.CsvField field, byte quote)
    {
        if (field.Length == 0)
        {
            return DataKind.Null;
        }

        if (field.Quoted)
        {
            return ClassifyText(CsvRecordReader.Decode(utf8, field, quote));
        }

        var span = utf8.Slice(field.Start, field.Length);

        if (IsBoolean(span, out _))
        {
            return DataKind.Boolean;
        }

        // Numeric-looking text with a leading zero stays text: a zip code or an
        // account number must not silently become a number.
        var leadingZero = HasLeadingZero(span);

        if (!leadingZero && Utf8Parser.TryParse(span, out long _, out var consumed) && consumed == span.Length)
        {
            return DataKind.Integer;
        }

        if (!leadingZero && !HasExponent(span) && Utf8Parser.TryParse(span, out decimal _, out consumed) && consumed == span.Length)
        {
            return DataKind.Decimal;
        }

        if (TryClassifyTemporal(span, out var temporal))
        {
            return temporal;
        }

        return !leadingZero && Utf8Parser.TryParse(span, out double number, out consumed) && consumed == span.Length && double.IsFinite(number)
            ? DataKind.Number
            : DataKind.Text;
    }

    private static DataKind ClassifyText(string text)
    {
        if (text.Length == 0)
        {
            return DataKind.Null;
        }

        if (bool.TryParse(text, out _))
        {
            return DataKind.Boolean;
        }

        var leadingZero = HasLeadingZero(text);

        if (!leadingZero && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return DataKind.Integer;
        }

        if (!leadingZero && !text.Contains('e') && !text.Contains('E') && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            return DataKind.Decimal;
        }

        if (TryClassifyTemporal(text, out var temporal))
        {
            return temporal;
        }

        return !leadingZero && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? DataKind.Number
            : DataKind.Text;
    }

    // A token is numeric only when the integer part of its value has no leading
    // zero. After an optional sign, the integer part is the digits before the
    // first '.', 'e' or 'E'; it has a leading zero exactly when the first two
    // tokens are a '0' followed by anything but those separators. "0", "0.5" and
    // "-0" are numbers, "00", "-01" and "01e3" are text, so a leading-zero
    // identifier survives a round trip. The token is never empty here: the
    // callers check that first.
    private static bool HasLeadingZero(ReadOnlySpan<byte> span)
    {
        var start = span[0] is (byte)'+' or (byte)'-' ? 1 : 0;

        return start + 1 < span.Length
            && span[start] == (byte)'0'
            && span[start + 1] is not ((byte)'.' or (byte)'e' or (byte)'E');
    }

    private static bool HasLeadingZero(string text)
    {
        var start = text[0] is '+' or '-' ? 1 : 0;

        return start + 1 < text.Length
            && text[start] == '0'
            && text[start + 1] is not ('.' or 'e' or 'E');
    }

    // A decimal field never carries an exponent: an exponent form is a double, so
    // the exact-decimal kind stays a plain money-like number.
    private static bool HasExponent(ReadOnlySpan<byte> span)
    {
        foreach (var value in span)
        {
            if (value is (byte)'e' or (byte)'E')
            {
                return true;
            }
        }

        return false;
    }

    // A temporal value is recognised by its shape, not by guessing: the bytes must
    // be digits and separators only, then an exact/round-trip parse decides the kind.
    // This keeps ordinary text out of the parsers without allocating a string.
    private static bool TryClassifyTemporal(ReadOnlySpan<byte> span, out DataKind kind)
    {
        kind = DataKind.Text;

        for (var index = 0; index < span.Length; index++)
        {
            var value = span[index];

            if (value is >= (byte)'0' and <= (byte)'9'
                or (byte)'-' or (byte)'/' or (byte)':' or (byte)'.' or (byte)'+' or (byte)' '
                or (byte)'T' or (byte)'Z' or (byte)'t' or (byte)'z')
            {
                continue;
            }

            return false;
        }

        return TryClassifyTemporal(Encoding.UTF8.GetString(span), out kind);
    }

    private static bool TryClassifyTemporal(string text, out DataKind kind)
    {
        kind = DataKind.Text;

        var hasTime = text.Contains(':');
        var hasDate = text.Contains('-') || text.Contains('/');

        if (hasTime && hasDate)
        {
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            {
                kind = DataKind.Timestamp;
                return true;
            }

            return false;
        }

        if (hasTime)
        {
            if (TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                kind = DataKind.Time;
                return true;
            }

            return false;
        }

        if (hasDate
            && DateOnly.TryParseExact(text, ["yyyy-MM-dd", "yyyy/MM/dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            kind = DataKind.Date;
            return true;
        }

        return false;
    }

    private static DataValue ReadValue(
        ReadOnlySpan<byte> utf8,
        CsvRecordReader.CsvField field,
        DataKind kind,
        Utf8StringPool pool,
        byte quote)
    {
        if (field.Length == 0)
        {
            return DataValue.Null;
        }

        if (field.Quoted)
        {
            var text = CsvRecordReader.Decode(utf8, field, quote);

            return text.Length == 0 ? DataValue.Null : ReadTextValue(text, kind);
        }

        var span = utf8.Slice(field.Start, field.Length);
        DataValue value;

        switch (kind)
        {
            case DataKind.Boolean:
                _ = IsBoolean(span, out var boolean);
                value = DataValue.From(boolean);
                break;
            case DataKind.Integer:
                _ = Utf8Parser.TryParse(span, out long integer, out _);
                value = DataValue.From(integer);
                break;
            case DataKind.Number:
                _ = Utf8Parser.TryParse(span, out double number, out _);
                value = DataValue.From(number);
                break;
            case DataKind.Decimal:
                _ = Utf8Parser.TryParse(span, out decimal decimalValue, out _);
                value = DataValue.From(decimalValue);
                break;
            case DataKind.Date:
            case DataKind.Time:
            case DataKind.Timestamp:
                value = ReadTextValue(CsvRecordReader.Decode(utf8, field, quote), kind);
                break;
            default:
                value = DataValue.From(pool.Get(span));
                break;
        }

        return value;
    }

    private static DataValue ReadTextValue(string text, DataKind kind)
    {
        return kind switch
        {
            DataKind.Boolean => DataValue.From(bool.Parse(text)),
            DataKind.Integer => DataValue.From(long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)),
            DataKind.Number => DataValue.From(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)),
            DataKind.Decimal => DataValue.From(decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture)),
            DataKind.Date => DataValue.From(DateOnly.ParseExact(text, ["yyyy-MM-dd", "yyyy/MM/dd"], CultureInfo.InvariantCulture, DateTimeStyles.None)),
            DataKind.Time => DataValue.From(TimeOnly.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.None)),
            DataKind.Timestamp => DataValue.From(DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
            _ => DataValue.From(text),
        };
    }

    private static bool IsBoolean(ReadOnlySpan<byte> span, out bool value)
    {
        if (span.Length == 4 && EqualsIgnoreCase(span, "true"u8))
        {
            value = true;
            return true;
        }

        if (span.Length == 5 && EqualsIgnoreCase(span, "false"u8))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static bool EqualsIgnoreCase(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        for (var index = 0; index < left.Length; index++)
        {
            var lower = left[index] is >= (byte)'A' and <= (byte)'Z' ? (byte)(left[index] + 32) : left[index];

            if (lower != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static CoreError TooManyCells() =>
        new("serialization.too_large", $"The CSV document exceeds {MaxCells} cells.");

    private static CoreError TooManyColumns() =>
        new("serialization.too_many_columns", $"The CSV document declares more than {MaxColumns} columns.");

    private static CoreError TooLarge() =>
        new("serialization.too_large", $"The CSV record is larger than {MaxRecordBytes} bytes.");

    private static CoreError UnEven(int expected, int actual) =>
        new(
            "serialization.uneven_row",
            $"A CSV record of {actual} fields does not match the {expected}-column header.");
}
