// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;
using Core.Results;
using Core.Serialization.Values;

namespace Core.Serialization.Tables;

/// <summary>
/// Joins two tables on one or more key columns. The right columns are prefixed
/// (by default with <c>right.</c>) so they can sit beside the left columns, a
/// duplicate right key produces one output row per match, a null key never
/// matches, and a left join emits an unmatched left row with null right cells.
/// </summary>
public static class DataJoin
{
    /// <summary>Joins two tables.</summary>
    /// <param name="left">The left table.</param>
    /// <param name="right">The right table.</param>
    /// <param name="keys">The key column pairs; at least one is required.</param>
    /// <param name="kind">Inner or left.</param>
    /// <param name="rightPrefix">The prefix applied to every right column.</param>
    /// <returns>The joined table, or a failure with code <c>data.invalid_join</c>.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    public static Result<DataTable> Join(
        DataTable left,
        DataTable right,
        IReadOnlyList<JoinKey> keys,
        JoinKind kind = JoinKind.Inner,
        string rightPrefix = "right.")
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(rightPrefix);

        if (kind is not (JoinKind.Inner or JoinKind.Left))
        {
            return Result<DataTable>.Failure(Invalid($"'{kind}' is not a join kind."));
        }

        if (keys.Count == 0)
        {
            return Result<DataTable>.Failure(Invalid("A join needs at least one key column."));
        }

        var leftKeys = new int[keys.Count];
        var rightKeys = new int[keys.Count];

        for (var index = 0; index < keys.Count; index++)
        {
            var key = keys[index];

            if (string.IsNullOrWhiteSpace(key.LeftColumn) ||
                string.IsNullOrWhiteSpace(key.RightColumn) ||
                !left.TryGetColumnIndex(key.LeftColumn, out leftKeys[index]) ||
                !right.TryGetColumnIndex(key.RightColumn, out rightKeys[index]))
            {
                return Result<DataTable>.Failure(
                    Invalid($"The join key '{key.LeftColumn}' / '{key.RightColumn}' does not name a column."));
            }

            var leftKind = left.Schema[leftKeys[index]].Kind;
            var rightKind = right.Schema[rightKeys[index]].Kind;

            if (leftKind != rightKind || leftKind == DataKind.Node)
            {
                return Result<DataTable>.Failure(Invalid(
                    $"The join key '{key.LeftColumn}' / '{key.RightColumn}' is {leftKind} / {rightKind}; keys must share a scalar kind."));
            }
        }

        DataTable output;

        try
        {
            var fields = new List<DataField>(left.ColumnCount + right.ColumnCount);

            for (var column = 0; column < left.ColumnCount; column++)
            {
                fields.Add(left.Schema[column]);
            }

            for (var column = 0; column < right.ColumnCount; column++)
            {
                fields.Add(new DataField(
                    string.Concat(rightPrefix, right.Schema[column].Name),
                    right.Schema[column].Kind));
            }

            output = new DataTable(string.Concat(left.Name, "+", right.Name), new DataSchema(fields));
        }
        catch (ArgumentException exception)
        {
            return Result<DataTable>.Failure(Invalid(exception.Message));
        }

        var rightRows = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (var row = 0; row < right.RowCount; row++)
        {
            var key = KeyOf(right, row, rightKeys);

            if (key is null)
            {
                continue;
            }

            if (!rightRows.TryGetValue(key, out var matches))
            {
                matches = [];
                rightRows.Add(key, matches);
            }

            matches.Add(row);
        }

        var outputRow = new DataValue[left.ColumnCount + right.ColumnCount];

        for (var leftRow = 0; leftRow < left.RowCount; leftRow++)
        {
            var key = KeyOf(left, leftRow, leftKeys);

            if (key is not null && rightRows.TryGetValue(key, out var matches))
            {
                foreach (var rightRow in matches)
                {
                    Copy(left, leftRow, outputRow, 0);
                    Copy(right, rightRow, outputRow, left.ColumnCount);
                    output.AddRow(outputRow);
                }

                continue;
            }

            if (kind == JoinKind.Left)
            {
                Copy(left, leftRow, outputRow, 0);
                Array.Clear(outputRow, left.ColumnCount, right.ColumnCount);
                output.AddRow(outputRow);
            }
        }

        return Result<DataTable>.Success(output);
    }

    // A length-prefixed key cannot be forged by a delimiter in a text value.
    private static string? KeyOf(DataTable table, int row, ReadOnlySpan<int> columns)
    {
        var builder = new StringBuilder();

        foreach (var column in columns)
        {
            var value = table[row, column];

            if (value.IsNull)
            {
                return null;
            }

            var text = value.ToInvariantText();
            builder.Append(text.Length).Append(':').Append(text);
        }

        return builder.ToString();
    }

    private static void Copy(DataTable table, int row, DataValue[] destination, int offset)
    {
        for (var column = 0; column < table.ColumnCount; column++)
        {
            destination[offset + column] = table[row, column];
        }
    }

    private static CoreError Invalid(string message) =>
        new(CoreErrorCodes.DataInvalidJoin, message);
}
