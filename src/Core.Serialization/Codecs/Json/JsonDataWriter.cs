// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Core.Results;
using Core.Serialization.Documents;
using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Codecs.Json;

/// <summary>
/// Writes a <see cref="DataDocument"/> as JSON, over the BCL
/// <see cref="Utf8JsonWriter"/> so nothing reflects and nothing allocates a
/// UTF-16 intermediate.
/// </summary>
/// <remarks>
/// The mapping is canonical: a document without sections is the root table — an
/// object when it has one row, an array of row objects otherwise. A document with
/// sections is an object whose root fields (at most one row) are followed by one
/// property per section, each an array of row objects. A null cell is JSON
/// <see langword="null"/> and a <see cref="DataKind.Node"/> cell is its nested
/// JSON. Dates, times and timestamps are written as ISO-8601 strings, because
/// JSON has no date type; reading them back yields text.
/// </remarks>
public static class JsonDataWriter
{
    /// <summary>Writes a document to a buffer.</summary>
    /// <param name="document">The document; must not be <see langword="null"/>.</param>
    /// <param name="writer">The destination buffer; must not be <see langword="null"/>.</param>
    /// <returns>A successful result whose value is always <see langword="true"/>,
    /// or a failure with code <c>serialization.invalid_document</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="writer"/> is <see langword="null"/>.</exception>
    public static Result<bool> Write(DataDocument document, IBufferWriter<byte> writer) =>
        Write(document, writer, JsonNumberFormat.Default);

    /// <summary>Writes a document to a buffer, rendering number cells with a caller-supplied format.</summary>
    /// <param name="document">The document; must not be <see langword="null"/>.</param>
    /// <param name="writer">The destination buffer; must not be <see langword="null"/>.</param>
    /// <param name="numberFormat">How to render <see cref="DataKind.Number"/> cells.</param>
    /// <returns>A successful result whose value is always <see langword="true"/>,
    /// or a failure with code <c>serialization.invalid_document</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="writer"/> is <see langword="null"/>.</exception>
    public static Result<bool> Write(DataDocument document, IBufferWriter<byte> writer, JsonNumberFormat numberFormat) =>
        Write(document, writer, numberFormat, JsonEscaping.Default);

    /// <summary>Writes a document to a buffer, rendering number cells with a caller-supplied format and escaping strings with a caller-supplied mode.</summary>
    /// <param name="document">The document; must not be <see langword="null"/>.</param>
    /// <param name="writer">The destination buffer; must not be <see langword="null"/>.</param>
    /// <param name="numberFormat">How to render <see cref="DataKind.Number"/> cells.</param>
    /// <param name="escaping">How to escape text cells and property names; see <see cref="JsonEscaping"/>.</param>
    /// <returns>A successful result whose value is always <see langword="true"/>,
    /// or a failure with code <c>serialization.invalid_document</c> or
    /// <c>serialization.depth_exceeded</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/>, <paramref name="writer"/> or <paramref name="escaping"/> is <see langword="null"/>.</exception>
    public static Result<bool> Write(DataDocument document, IBufferWriter<byte> writer, JsonNumberFormat numberFormat, JavaScriptEncoder escaping)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(escaping);

        if (document.Sections.Count > 0 && document.Root.RowCount > 1)
        {
            return Result<bool>.Failure(
                new CoreError(
                    CoreErrorCodes.SerializationInvalidDocument,
                    "A JSON document with sections must have at most one root row."));
        }

        return Run(writer, escaping, json => WriteDocument(json, document, numberFormat), "document");
    }

    /// <summary>Writes a document to a new UTF-8 byte array.</summary>
    /// <param name="document">The document; must not be <see langword="null"/>.</param>
    /// <returns>The UTF-8 bytes, or a failure from <see cref="Write(DataDocument, IBufferWriter{byte})"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static Result<byte[]> WriteToUtf8Bytes(DataDocument document) =>
        WriteToUtf8Bytes(document, JsonNumberFormat.Default);

    /// <summary>Writes a document to a new UTF-8 byte array, rendering number cells with a caller-supplied format.</summary>
    /// <param name="document">The document; must not be <see langword="null"/>.</param>
    /// <param name="numberFormat">How to render <see cref="DataKind.Number"/> cells.</param>
    /// <returns>The UTF-8 bytes, or a failure from <see cref="Write(DataDocument, IBufferWriter{byte}, JsonNumberFormat)"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static Result<byte[]> WriteToUtf8Bytes(DataDocument document, JsonNumberFormat numberFormat) =>
        WriteToUtf8Bytes(document, numberFormat, JsonEscaping.Default);

    /// <summary>Writes a document to a new UTF-8 byte array, choosing both the number rendering and the string escaping.</summary>
    /// <param name="document">The document; must not be <see langword="null"/>.</param>
    /// <param name="numberFormat">How to render <see cref="DataKind.Number"/> cells.</param>
    /// <param name="escaping">How to escape text cells and property names; see <see cref="JsonEscaping"/>.</param>
    /// <returns>The UTF-8 bytes, or a failure from <see cref="Write(DataDocument, IBufferWriter{byte}, JsonNumberFormat, JavaScriptEncoder)"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="escaping"/> is <see langword="null"/>.</exception>
    public static Result<byte[]> WriteToUtf8Bytes(DataDocument document, JsonNumberFormat numberFormat, JavaScriptEncoder escaping)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(escaping);

        return Utf8Buffer.ToUtf8Bytes((document, numberFormat, escaping), static (state, buffer) =>
            Write(state.document, buffer, state.numberFormat, state.escaping));
    }

    /// <summary>
    /// Writes a bare <see cref="DataNode"/> to a buffer — the value itself, with no
    /// table wrapping, so a root list is <c>[]</c>, a root scalar is <c>"x"</c> and a
    /// root map is <c>{…}</c>. Use this instead of a <see cref="DataDocument"/> when
    /// the JSON value is not a one-row table.
    /// </summary>
    /// <param name="node">The node; must not be <see langword="null"/>.</param>
    /// <param name="writer">The destination buffer; must not be <see langword="null"/>.</param>
    /// <returns>A successful result whose value is always <see langword="true"/>,
    /// or a failure with code <c>serialization.invalid_document</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> or <paramref name="writer"/> is <see langword="null"/>.</exception>
    public static Result<bool> WriteNode(DataNode node, IBufferWriter<byte> writer) =>
        WriteNode(node, writer, JsonNumberFormat.Default);

    /// <summary>Writes a bare node to a buffer, rendering number cells with a caller-supplied format.</summary>
    /// <param name="node">The node; must not be <see langword="null"/>.</param>
    /// <param name="writer">The destination buffer; must not be <see langword="null"/>.</param>
    /// <param name="numberFormat">How to render <see cref="DataKind.Number"/> cells.</param>
    /// <returns>A successful result whose value is always <see langword="true"/>,
    /// or a failure with code <c>serialization.invalid_document</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> or <paramref name="writer"/> is <see langword="null"/>.</exception>
    public static Result<bool> WriteNode(DataNode node, IBufferWriter<byte> writer, JsonNumberFormat numberFormat) =>
        WriteNode(node, writer, numberFormat, JsonEscaping.Default);

    /// <summary>Writes a bare node to a buffer, choosing both the number rendering and the string escaping.</summary>
    /// <param name="node">The node; must not be <see langword="null"/>.</param>
    /// <param name="writer">The destination buffer; must not be <see langword="null"/>.</param>
    /// <param name="numberFormat">How to render <see cref="DataKind.Number"/> cells.</param>
    /// <param name="escaping">How to escape text cells and property names; see <see cref="JsonEscaping"/>.</param>
    /// <returns>A successful result whose value is always <see langword="true"/>,
    /// or a failure with code <c>serialization.invalid_document</c> or
    /// <c>serialization.depth_exceeded</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/>, <paramref name="writer"/> or <paramref name="escaping"/> is <see langword="null"/>.</exception>
    public static Result<bool> WriteNode(DataNode node, IBufferWriter<byte> writer, JsonNumberFormat numberFormat, JavaScriptEncoder escaping)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(escaping);

        return Run(writer, escaping, json => WriteNodeValue(json, node, numberFormat), "node");
    }

    /// <summary>Writes a bare node to a new UTF-8 byte array.</summary>
    /// <param name="node">The node; must not be <see langword="null"/>.</param>
    /// <returns>The UTF-8 bytes, or a failure from <see cref="WriteNode(DataNode, IBufferWriter{byte})"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    public static Result<byte[]> WriteNodeToUtf8Bytes(DataNode node) =>
        WriteNodeToUtf8Bytes(node, JsonNumberFormat.Default);

    /// <summary>Writes a bare node to a new UTF-8 byte array, rendering number cells with a caller-supplied format.</summary>
    /// <param name="node">The node; must not be <see langword="null"/>.</param>
    /// <param name="numberFormat">How to render <see cref="DataKind.Number"/> cells.</param>
    /// <returns>The UTF-8 bytes, or a failure from <see cref="WriteNode(DataNode, IBufferWriter{byte})"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    public static Result<byte[]> WriteNodeToUtf8Bytes(DataNode node, JsonNumberFormat numberFormat) =>
        WriteNodeToUtf8Bytes(node, numberFormat, JsonEscaping.Default);

    /// <summary>Writes a bare node to a new UTF-8 byte array, choosing both the number rendering and the string escaping.</summary>
    /// <param name="node">The node; must not be <see langword="null"/>.</param>
    /// <param name="numberFormat">How to render <see cref="DataKind.Number"/> cells.</param>
    /// <param name="escaping">How to escape text cells and property names; see <see cref="JsonEscaping"/>.</param>
    /// <returns>The UTF-8 bytes, or a failure from <see cref="WriteNode(DataNode, IBufferWriter{byte}, JsonNumberFormat, JavaScriptEncoder)"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> or <paramref name="escaping"/> is <see langword="null"/>.</exception>
    public static Result<byte[]> WriteNodeToUtf8Bytes(DataNode node, JsonNumberFormat numberFormat, JavaScriptEncoder escaping)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(escaping);

        return Utf8Buffer.ToUtf8Bytes((node, numberFormat, escaping), static (state, buffer) =>
            WriteNode(state.node, buffer, state.numberFormat, state.escaping));
    }

    /// <summary>
    /// Runs a writer into a buffer, mapping the two failures a <see cref="Utf8JsonWriter"/>
    /// can raise: a value with no JSON form (a non-finite number, a lone surrogate —
    /// <see cref="ArgumentException"/>) and nesting past the BCL's hard depth limit
    /// (<see cref="InvalidOperationException"/>). Neither escapes as an exception.
    /// </summary>
    private static Result<bool> Run(IBufferWriter<byte> writer, JavaScriptEncoder escaping, Action<Utf8JsonWriter> write, string subject)
    {
        try
        {
            using (var json = new Utf8JsonWriter(writer, new JsonWriterOptions { SkipValidation = true, Encoder = escaping }))
            {
                write(json);
                json.Flush();
            }
        }
        catch (ArgumentException exception)
        {
            return Result<bool>.Failure(
                new CoreError(CoreErrorCodes.SerializationInvalidDocument, $"The {subject} has a value JSON cannot write: {exception.Message}"));
        }
        catch (InvalidOperationException exception)
        {
            // Utf8JsonWriter refuses to start a container past its hard depth limit
            // (1000) with an InvalidOperationException, not a JsonException.
            return Result<bool>.Failure(
                new CoreError(CoreErrorCodes.SerializationDepthExceeded, $"The {subject} nests deeper than JSON allows: {exception.Message}"));
        }

        return Result<bool>.Success(true);
    }

    private static void WriteDocument(Utf8JsonWriter json, DataDocument document, JsonNumberFormat numberFormat)
    {
        if (document.Sections.Count == 0)
        {
            if (document.Root.ColumnCount == 0)
            {
                json.WriteStartObject();
                json.WriteEndObject();
                return;
            }

            if (document.Root.RowCount == 1)
            {
                WriteRowObject(json, document.Root, 0, numberFormat);
            }
            else
            {
                WriteTableArray(json, document.Root, numberFormat);
            }

            return;
        }

        json.WriteStartObject();

        if (document.Root.RowCount == 1)
        {
            WriteRowProperties(json, document.Root, 0, numberFormat);
        }

        foreach (var section in document.Sections)
        {
            json.WritePropertyName(section.Name);
            WriteTableArray(json, section, numberFormat);
        }

        json.WriteEndObject();
    }

    private static void WriteTableArray(Utf8JsonWriter json, DataTable table, JsonNumberFormat numberFormat)
    {
        json.WriteStartArray();

        for (var row = 0; row < table.RowCount; row++)
        {
            WriteRowObject(json, table, row, numberFormat);
        }

        json.WriteEndArray();
    }

    private static void WriteRowObject(Utf8JsonWriter json, DataTable table, int row, JsonNumberFormat numberFormat)
    {
        json.WriteStartObject();
        WriteRowProperties(json, table, row, numberFormat);
        json.WriteEndObject();
    }

    private static void WriteRowProperties(Utf8JsonWriter json, DataTable table, int row, JsonNumberFormat numberFormat)
    {
        for (var column = 0; column < table.ColumnCount; column++)
        {
            json.WritePropertyName(table.Schema[column].Name);
            WriteCell(json, table[row, column], numberFormat);
        }
    }

    private static void WriteCell(Utf8JsonWriter json, DataValue value, JsonNumberFormat numberFormat)
    {
        switch (value.Kind)
        {
            case DataKind.Null:
                json.WriteNullValue();
                break;
            case DataKind.Boolean:
                value.TryGetBoolean(out var boolean);
                json.WriteBooleanValue(boolean);
                break;
            case DataKind.Integer:
                value.TryGetInteger(out var integer);
                json.WriteNumberValue(integer);
                break;
            case DataKind.Number:
                value.TryGetNumber(out var number);

                // A caller-supplied format renders the number itself (canonical text:
                // shortest round-trip digits, an explicit .0, a chosen exponent form);
                // otherwise the BCL rendering is kept, byte for byte.
                var rendered = numberFormat.Render(number);
                if (rendered is null)
                {
                    json.WriteNumberValue(number);
                }
                else
                {
                    // A non-finite number has no JSON form regardless of the format, and
                    // WriteRawValue rejects malformed text with a JsonException, so both
                    // are turned into the same ArgumentException the callers catch.
                    try
                    {
                        json.WriteRawValue(rendered, skipInputValidation: false);
                    }
                    catch (Exception exception) when (exception is JsonException or ArgumentException)
                    {
                        throw new ArgumentException(
                            $"The number format produced '{rendered}', which is not a JSON number.", exception);
                    }
                }

                break;
            case DataKind.Decimal:
                value.TryGetDecimal(out var decimalValue);
                json.WriteNumberValue(decimalValue);
                break;
            case DataKind.Text:
                value.TryGetText(out var text);
                json.WriteStringValue(text);
                break;
            case DataKind.Date:
                value.TryGetDate(out var date);
                json.WriteStringValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                break;
            case DataKind.Time:
                value.TryGetTime(out var time);
                json.WriteStringValue(time.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
                break;
            case DataKind.Timestamp:
                value.TryGetTimestamp(out var timestamp);
                json.WriteStringValue(timestamp.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DataKind.Node:
                value.TryGetNode(out var node);
                WriteNodeValue(json, node!, numberFormat);
                break;
        }
    }

    private static void WriteNodeValue(Utf8JsonWriter json, DataNode node, JsonNumberFormat numberFormat)
    {
        switch (node.Kind)
        {
            case DataNodeKind.Scalar:
                WriteCell(json, node.Value, numberFormat);
                break;
            case DataNodeKind.List:
                json.WriteStartArray();

                foreach (var item in node.Items)
                {
                    WriteNodeValue(json, item, numberFormat);
                }

                json.WriteEndArray();
                break;
            case DataNodeKind.Map:
                json.WriteStartObject();

                foreach (var property in node.Properties)
                {
                    json.WritePropertyName(property.Name);
                    WriteNodeValue(json, property.Value, numberFormat);
                }

                json.WriteEndObject();
                break;
        }
    }
}
