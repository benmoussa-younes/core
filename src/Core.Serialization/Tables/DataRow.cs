// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Values;

namespace Core.Serialization.Tables;

/// <summary>
/// A row cursor over a <see cref="DataTable"/>: a table and a row index. It is a
/// view, not storage — reading a cell reaches the column's typed list directly.
/// </summary>
public readonly struct DataRow
{
    private readonly DataTable _table;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataRow"/> struct.
    /// </summary>
    /// <param name="table">The table; must not be <see langword="null"/>.</param>
    /// <param name="index">The row index.</param>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    public DataRow(DataTable table, int index)
    {
        ArgumentNullException.ThrowIfNull(table);

        _table = table;
        Index = index;
    }

    /// <summary>Gets the underlying table.</summary>
    public DataTable Table => _table;

    /// <summary>Gets the row index.</summary>
    public int Index { get; }

    /// <summary>Gets the number of cells in the row.</summary>
    public int Count => _table.ColumnCount;

    /// <summary>Gets the value at a column.</summary>
    /// <param name="column">The column index.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="column"/> is outside the columns.</exception>
    public DataValue this[int column] => _table.Column(column).Get(Index);

    /// <summary>Looks a cell up by column name, ordinally.</summary>
    /// <param name="name">The column name.</param>
    /// <param name="value">The value when the column is present.</param>
    /// <returns><see langword="true"/> when the column is present.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGetValue(string name, out DataValue value)
    {
        if (_table.TryGetColumnIndex(name, out var column))
        {
            value = _table.Column(column).Get(Index);
            return true;
        }

        value = DataValue.Null;
        return false;
    }
}
