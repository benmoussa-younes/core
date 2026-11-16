// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;
using Core.Serialization.Values;

namespace Core.Serialization.Tables;

/// <summary>
/// Column projections over a table: keep an explicit column list, or drop a set
/// of columns. The schema and the row order are preserved; the source table is
/// not mutated.
/// </summary>
public static class DataProjection
{
    /// <summary>Keeps only the named columns, in the requested order.</summary>
    /// <param name="table">The source table.</param>
    /// <param name="columns">The column names to keep; at least one is required.</param>
    /// <returns>The projected table, or a failure with code <c>data.invalid_projection</c>.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    public static Result<DataTable> Select(DataTable table, IReadOnlyList<string> columns)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(columns);

        if (columns.Count == 0)
        {
            return Result<DataTable>.Failure(Invalid("A projection needs at least one column."));
        }

        var indices = new int[columns.Count];

        for (var index = 0; index < columns.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(columns[index]) ||
                !table.TryGetColumnIndex(columns[index], out indices[index]))
            {
                return Result<DataTable>.Failure(
                    Invalid($"'{columns[index]}' is not a column of the table."));
            }
        }

        return Build(table, indices);
    }

    /// <summary>Drops the named columns, preserving the remaining order.</summary>
    /// <param name="table">The source table.</param>
    /// <param name="columns">The column names to drop; at least one is required.</param>
    /// <returns>The projected table, or a failure with code <c>data.invalid_projection</c>.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    public static Result<DataTable> Drop(DataTable table, IReadOnlyList<string> columns)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(columns);

        if (columns.Count == 0)
        {
            return Result<DataTable>.Failure(Invalid("A projection needs at least one column to drop."));
        }

        var dropped = new HashSet<int>();

        foreach (var column in columns)
        {
            if (string.IsNullOrWhiteSpace(column) || !table.TryGetColumnIndex(column, out var index))
            {
                return Result<DataTable>.Failure(
                    Invalid($"'{column}' is not a column of the table."));
            }

            _ = dropped.Add(index);
        }

        var kept = new List<int>(table.ColumnCount - dropped.Count);

        for (var index = 0; index < table.ColumnCount; index++)
        {
            if (!dropped.Contains(index))
            {
                kept.Add(index);
            }
        }

        return kept.Count == 0
            ? Result<DataTable>.Failure(Invalid("A projection cannot drop every column."))
            : Build(table, [.. kept]);
    }

    private static Result<DataTable> Build(DataTable table, int[] indices)
    {
        DataTable projected;

        try
        {
            var fields = new DataField[indices.Length];

            for (var index = 0; index < indices.Length; index++)
            {
                fields[index] = table.Schema[indices[index]];
            }

            projected = new DataTable(table.Name, new DataSchema(fields));
        }
        catch (ArgumentException exception)
        {
            return Result<DataTable>.Failure(Invalid(exception.Message));
        }

        var row = new DataValue[indices.Length];

        for (var sourceRow = 0; sourceRow < table.RowCount; sourceRow++)
        {
            for (var column = 0; column < indices.Length; column++)
            {
                row[column] = table[sourceRow, indices[column]];
            }

            projected.AddRow(row);
        }

        return Result<DataTable>.Success(projected);
    }

    private static CoreError Invalid(string message) =>
        new(CoreErrorCodes.DataInvalidProjection, message);
}
