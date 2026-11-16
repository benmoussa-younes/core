// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Core.Results;
using Core.Serialization.Documents;
using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Codecs.Json;

/// <summary>
/// Reads a <see cref="DataDocument"/> from JSON, over the BCL
/// <see cref="Utf8JsonReader"/>. It first builds a <see cref="DataNode"/> tree —
/// JSON is for configuration and state, not the bulk path — then projects it onto
/// the canonical mapping of <see cref="JsonDataWriter"/>.
/// </summary>
/// <remarks>
/// A top-level object becomes a one-row root table; a property whose value is an
/// array of objects becomes a section table named after the property. A top-level
/// array of objects becomes the root table, one row per object. Anything else
/// (a scalar, a nested object, an array of scalars) is carried on the root as a
/// <see cref="DataKind.Node"/> cell. A column's kind is inferred from its
/// non-null values: all-boolean is boolean, all-integer integer, any number is a
/// number, everything else is text, and a column that contains a nested value is
/// a node.
/// </remarks>
public static class JsonDataReader
{
    /// <summary>
    /// The deepest nesting <see cref="ReadNode(ReadOnlySpan{byte}, int)"/> accepts.
    /// The reader walks a node tree recursively, so an unbounded caller-supplied
    /// depth would let a crafted document exhaust the process stack. A document
    /// that nests deeper must be streamed instead.
    /// </summary>
    public const int MaximumSupportedDepth = 1_024;

    /// <summary>
    /// The maximum number of columns a top-level object array may declare.
    /// </summary>
    public const int MaxColumns = 65_536;

    /// <summary>
    /// The maximum number of cells (<c>rows × columns</c>) a top-level object
    /// array may materialize. It bounds the amplification from a tiny document
    /// that repeats many keys in many rows.
    /// </summary>
    public const int MaxCells = 4_000_000;

    /// <summary>Reads a document from UTF-8 JSON.</summary>
    /// <param name="utf8">The UTF-8 JSON bytes.</param>
    /// <returns>The document, or a failure with code <c>serialization.syntax</c>.</returns>
    public static Result<DataDocument> Read(ReadOnlySpan<byte> utf8)
    {
        if (TryReadObjectArray(utf8, out var streamed))
        {
            return streamed;
        }

        try
        {
            var reader = new Utf8JsonReader(utf8);

            // Read() throws on empty input, on a malformed value and on trailing
            // content after the first value, so its result is not inspected: the
            // catch below turns any of those into a syntax failure.
            _ = reader.Read();
            var root = ReadNode(ref reader, depth: 1, maximumDepth: int.MaxValue);
            _ = reader.Read();

            return Result<DataDocument>.Success(ToDocument(root));
        }
        catch (JsonException exception)
        {
            return Result<DataDocument>.Failure(Syntax(exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            // GetString() throws InvalidOperationException (not JsonException) on a
            // lone surrogate: "not valid UTF-16 JSON text". Untrusted input must
            // never escape as an exception, so it is a syntax failure like any other.
            return Result<DataDocument>.Failure(Syntax(exception.Message));
        }
    }

    /// <summary>
    /// Reads a bare <see cref="DataNode"/> from UTF-8 JSON — the value itself,
    /// with no table projection. A top-level array is a list node, a top-level
    /// object is a map node and a top-level scalar is a scalar node, so a root
    /// <c>[]</c>/<c>{}</c>/<c>"x"</c> round-trips through
    /// <see cref="JsonDataWriter.WriteNode(DataNode, System.Buffers.IBufferWriter{byte})"/>. A number that does not fit an
    /// <see cref="long"/> or a finite <see cref="double"/> is carried verbatim as
    /// text (see <see cref="ReadNumber"/>).
    /// </summary>
    /// <remarks>
    /// The nesting depth a caller accepts is its own policy within the supported
    /// ceiling: the BCL default (<c>64</c>) is far below what a data tree
    /// legitimately reaches, and a reader that silently stopped there would report
    /// a depth failure for a document that merely nests. The ceiling exists because
    /// the walk is recursive; a deeper document must be streamed.
    /// </remarks>
    /// <param name="utf8">The UTF-8 JSON bytes.</param>
    /// <param name="maximumDepth">The deepest nesting accepted, at most <see cref="MaximumSupportedDepth"/>.</param>
    /// <returns>The node, or a failure with code <c>serialization.syntax</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumDepth"/> is not positive or exceeds <see cref="MaximumSupportedDepth"/>.</exception>
    public static Result<DataNode> ReadNode(ReadOnlySpan<byte> utf8, int maximumDepth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDepth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumDepth, MaximumSupportedDepth);

        try
        {
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = maximumDepth == int.MaxValue ? int.MaxValue : maximumDepth + 1 });

            // Read() throws on empty input, on a malformed value and on trailing
            // content after the first value, so its result is not inspected: the
            // catch below turns any of those into a syntax failure.
            _ = reader.Read();
            var node = ReadNode(ref reader, depth: 1, maximumDepth);
            _ = reader.Read();

            return Result<DataNode>.Success(node);
        }
        catch (JsonDepthExceededException)
        {
            // Our own guard is the binding constraint (the BCL limit is one deeper),
            // so the failure is reported precisely as depth rather than as syntax.
            return Result<DataNode>.Failure(TooDeep(maximumDepth));
        }
        catch (JsonException exception)
        {
            return Result<DataNode>.Failure(Syntax(exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            // A lone surrogate makes GetString() throw InvalidOperationException
            // rather than JsonException; it is still invalid input, not a crash.
            return Result<DataNode>.Failure(Syntax(exception.Message));
        }
    }

    /// <summary>
    /// Reads a top-level array of objects straight into a table, without building
    /// a <see cref="DataNode"/> tree: the first pass collects the column names and
    /// infers each kind, the second fills the rows. It returns
    /// <see langword="false"/> for any other top-level shape (or a malformed
    /// document), and the caller falls back to the tree path.
    /// </summary>
    private static bool TryReadObjectArray(ReadOnlySpan<byte> utf8, out Result<DataDocument> result)
    {
        result = default;

        try
        {
            var reader = new Utf8JsonReader(utf8);

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            {
                return false;
            }

            var columnNames = new List<string>();
            var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            var kinds = new List<ColumnKindAccumulator>();
            var sawObject = false;
            var rowCount = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    return false;
                }

                sawObject = true;
                rowCount++;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    var name = reader.GetString()!;
                    reader.Read();

                    if (!indexByName.TryGetValue(name, out var column))
                    {
                        if (columnNames.Count == MaxColumns)
                        {
                            result = Result<DataDocument>.Failure(
                                new CoreError(CoreErrorCodes.SerializationTooManyColumns, $"The JSON document declares more than {MaxColumns} columns."));
                            return true;
                        }

                        column = columnNames.Count;
                        indexByName.Add(name, column);
                        columnNames.Add(name);
                        kinds.Add(new ColumnKindAccumulator());
                    }

                    // A List<T> indexer returns a copy, so mutate the accumulator
                    // through a span to update the stored struct.
                    CollectionsMarshal.AsSpan(kinds)[column].Add(ClassifyToken(ref reader));
                }
            }

            if (!sawObject || reader.Read())
            {
                return false;
            }

            if (rowCount > 0 && columnNames.Count > MaxCells / rowCount)
            {
                result = Result<DataDocument>.Failure(
                    new CoreError(CoreErrorCodes.SerializationTooLarge, $"The JSON object array exceeds {MaxCells} cells."));
                return true;
            }

            var fields = new DataField[columnNames.Count];

            for (var column = 0; column < fields.Length; column++)
            {
                fields[column] = new DataField(columnNames[column], kinds[column].Resolve());
            }

            var table = new DataTable(string.Empty, new DataSchema(fields));
            var rows = new Utf8JsonReader(utf8);
            _ = rows.Read();
            var row = new DataValue[fields.Length];

            while (rows.Read())
            {
                if (rows.TokenType == JsonTokenType.EndArray)
                {
                    break;
                }

                Array.Clear(row);

                while (rows.Read())
                {
                    if (rows.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    var name = rows.GetString()!;
                    rows.Read();
                    var value = ReadNatural(ref rows);
                    var column = indexByName[name];
                    CoerceValue(ref value, fields[column].Kind);
                    row[column] = value;
                }

                table.AddRow(row);
            }

            result = Result<DataDocument>.Success(DataDocument.FromTable(table));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // Same lone-surrogate gap as the tree path (GetString()); the caller
            // falls back to the tree read, which reports the syntax failure.
            return false;
        }
    }

    private static DataKind ClassifyToken(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return DataKind.Text;
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out _))
                {
                    return DataKind.Integer;
                }

                return reader.TryGetDouble(out var number) && double.IsFinite(number) ? DataKind.Number : DataKind.Text;
            case JsonTokenType.True:
            case JsonTokenType.False:
                return DataKind.Boolean;
            case JsonTokenType.Null:
                return DataKind.Null;
            default:
                reader.Skip();
                return DataKind.Node;
        }
    }

    private static DataValue ReadNatural(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return DataValue.From(reader.GetString()!);
            case JsonTokenType.Number:
                return ReadNumber(ref reader);
            case JsonTokenType.True:
                return DataValue.From(true);
            case JsonTokenType.False:
                return DataValue.From(false);
            case JsonTokenType.Null:
                return DataValue.Null;
            default:
                return DataValue.FromNode(ReadNode(ref reader, depth: 1, maximumDepth: int.MaxValue));
        }
    }

    private static void CoerceValue(ref DataValue value, DataKind kind)
    {
        if (value.IsNull)
        {
            return;
        }

        switch (kind)
        {
            case DataKind.Node:
                if (value.Kind != DataKind.Node)
                {
                    value = DataValue.FromNode(DataNode.Scalar(value));
                }

                break;
            case DataKind.Text:
                if (value.Kind != DataKind.Text)
                {
                    value = DataValue.From(value.ToInvariantText());
                }

                break;
            default:
                break;
        }
    }

    private static DataNode ReadNode(ref Utf8JsonReader reader, int depth, int maximumDepth)
    {
        DataNode? node = null;

        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                Guard(depth, maximumDepth);
                var properties = new List<DataProperty>();

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = reader.GetString()!;
                    reader.Read();
                    properties.Add(new DataProperty(name, ReadNode(ref reader, depth + 1, maximumDepth)));
                }

                node = DataNode.Map(properties);
                break;

            case JsonTokenType.StartArray:
                Guard(depth, maximumDepth);
                var items = new List<DataNode>();

                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    items.Add(ReadNode(ref reader, depth + 1, maximumDepth));
                }

                node = DataNode.List(items);
                break;

            case JsonTokenType.String:
                node = DataNode.Scalar(DataValue.From(reader.GetString()!));
                break;

            case JsonTokenType.Number:
                node = DataNode.Scalar(ReadNumber(ref reader));
                break;

            case JsonTokenType.True:
                node = DataNode.Scalar(DataValue.From(true));
                break;

            case JsonTokenType.False:
                node = DataNode.Scalar(DataValue.From(false));
                break;

            case JsonTokenType.Null:
                node = DataNode.Scalar(DataValue.Null);
                break;
        }

        // Read() guarantees a value token here; the fallback only keeps the
        // method total.
        return node ?? DataNode.Scalar(DataValue.Null);
    }

    private static DataValue ReadNumber(ref Utf8JsonReader reader)
    {
        if (reader.TryGetInt64(out var integer))
        {
            return DataValue.From(integer);
        }

        if (reader.TryGetDouble(out var number) && double.IsFinite(number))
        {
            return DataValue.From(number);
        }

        // A number outside the double range would become an infinity and never
        // round-trip, so it is kept verbatim as text instead.
        return DataValue.From(Encoding.UTF8.GetString(reader.ValueSpan));
    }

    private static DataDocument ToDocument(DataNode root)
    {
        DataDocument document = null!;

        switch (root.Kind)
        {
            case DataNodeKind.Scalar:
                document = new DataDocument(BuildValueTable("value", [root]));
                break;
            case DataNodeKind.List:
                document = ListDocument(root.Items);
                break;
            case DataNodeKind.Map:
                document = MapDocument(root.Properties);
                break;
        }

        return document;
    }

    // A valid top-level array of objects is streamed by TryReadObjectArray, so
    // this tree path only sees an array of scalars or an empty array.
    private static DataDocument ListDocument(IReadOnlyList<DataNode> items) =>
        new(BuildValueTable("value", items));

    private static DataDocument MapDocument(IReadOnlyList<DataProperty> properties)
    {
        var sections = new List<DataTable>();
        var columnNames = new List<string>();
        var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        var row = new List<DataValue>();

        foreach (var property in properties)
        {
            if (IsSection(property.Value))
            {
                sections.Add(BuildTableFromNodes(property.Name, property.Value.Items));
                continue;
            }

            if (indexByName.TryAdd(property.Name, columnNames.Count))
            {
                columnNames.Add(property.Name);
                row.Add(ToCell(property.Value));
            }
            else
            {
                // A duplicate JSON key takes its last value.
                row[indexByName[property.Name]] = ToCell(property.Value);
            }
        }

        var root = columnNames.Count == 0
            ? new DataTable(string.Empty, DataSchema.Empty)
            : BuildTable(string.Empty, columnNames, [row.ToArray()]);

        return new DataDocument(root, sections);
    }

    private static bool IsSection(DataNode node) =>
        node.Kind == DataNodeKind.List && node.Items.All(static item => item.Kind == DataNodeKind.Map);

    private static DataTable BuildValueTable(string name, IReadOnlyList<DataNode> items)
    {
        var rows = new DataValue[items.Count][];

        for (var index = 0; index < items.Count; index++)
        {
            rows[index] = [ToCell(items[index])];
        }

        return BuildTable(name, ["value"], rows);
    }

    private static DataTable BuildTableFromNodes(string name, IReadOnlyList<DataNode> maps)
    {
        var columnNames = new List<string>();
        var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var map in maps)
        {
            foreach (var property in map.Properties)
            {
                if (indexByName.TryAdd(property.Name, indexByName.Count))
                {
                    columnNames.Add(property.Name);
                }
            }
        }

        var rows = new DataValue[maps.Count][];

        for (var rowIndex = 0; rowIndex < maps.Count; rowIndex++)
        {
            var values = new DataValue[columnNames.Count];

            foreach (var property in maps[rowIndex].Properties)
            {
                if (indexByName.TryGetValue(property.Name, out var column))
                {
                    values[column] = ToCell(property.Value);
                }
            }

            rows[rowIndex] = values;
        }

        return BuildTable(name, columnNames, rows);
    }

    private static DataTable BuildTable(string name, List<string> columnNames, IReadOnlyList<DataValue[]> rows)
    {
        var kinds = new DataKind[columnNames.Count];
        var fields = new DataField[columnNames.Count];

        for (var column = 0; column < columnNames.Count; column++)
        {
            kinds[column] = InferKind(rows, column);
            Coerce(rows, column, kinds[column]);
            fields[column] = new DataField(columnNames[column], kinds[column]);
        }

        var table = new DataTable(name, new DataSchema(fields));

        foreach (var row in rows)
        {
            table.AddRow(row);
        }

        return table;
    }

    private static DataKind InferKind(IReadOnlyList<DataValue[]> rows, int column)
    {
        var kinds = new ColumnKindAccumulator();

        foreach (var row in rows)
        {
            kinds.Add(row[column].Kind);
        }

        return kinds.Resolve();
    }

    private static void Coerce(IReadOnlyList<DataValue[]> rows, int column, DataKind kind)
    {
        foreach (var row in rows)
        {
            var value = row[column];

            if (value.IsNull)
            {
                continue;
            }

            switch (kind)
            {
                case DataKind.Node:
                    if (value.Kind != DataKind.Node)
                    {
                        row[column] = DataValue.FromNode(DataNode.Scalar(value));
                    }

                    break;
                case DataKind.Text:
                    if (value.Kind != DataKind.Text)
                    {
                        row[column] = DataValue.From(value.ToInvariantText());
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private static DataValue ToCell(DataNode node) =>
        node.Kind == DataNodeKind.Scalar ? node.Value : DataValue.FromNode(node);

    private static CoreError Syntax(string reason) =>
        new("serialization.syntax", $"The JSON document is not valid: {reason}.");

    /// <summary>Rejects nesting past the caller's maximum with a dedicated signal so it maps to a depth failure, not a syntax one.</summary>
    private static void Guard(int depth, int maximumDepth)
    {
        if (depth > maximumDepth)
        {
            throw new JsonDepthExceededException();
        }
    }

    private static CoreError TooDeep(int maximumDepth) =>
        new(CoreErrorCodes.SerializationDepthExceeded, $"The JSON document nests deeper than the maximum depth of {maximumDepth}.");

    /// <summary>Signals our own depth guard; never surfaces as an escaping exception.</summary>
    private sealed class JsonDepthExceededException : Exception;
}
