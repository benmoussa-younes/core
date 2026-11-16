// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Values;

namespace Core.Serialization.Tables;

/// <summary>
/// A named set of rows over one <see cref="DataSchema"/>: the canonical, columnar
/// table of the serialization model. Every column has the same row count, and a
/// row is a <see cref="DataRow"/> view rather than an allocated object.
/// </summary>
/// <remarks>
/// The table is editable: rows can be inserted and removed, and columns added,
/// inserted, removed, renamed and moved. A structural edit validates the new
/// schema before it mutates anything, so a rejected rename or a duplicate column
/// leaves the table unchanged.
/// </remarks>
public sealed class DataTable
{
    private DataColumn[] _columns;
    private int _rowCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataTable"/> class.
    /// </summary>
    /// <param name="name">The table name; empty for an unnamed root table.</param>
    /// <param name="schema">The schema; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="schema"/> is <see langword="null"/>.</exception>
    public DataTable(string name, DataSchema schema)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(schema);

        Name = name;
        Schema = schema;
        _columns = new DataColumn[schema.Count];

        for (var index = 0; index < schema.Count; index++)
        {
            _columns[index] = new DataColumn(schema[index].Name, schema[index].Kind);
        }
    }

    /// <summary>Gets the table name.</summary>
    public string Name { get; }

    /// <summary>Gets the schema. A structural edit replaces it.</summary>
    public DataSchema Schema { get; private set; }

    /// <summary>Gets the number of columns.</summary>
    public int ColumnCount => _columns.Length;

    /// <summary>Gets the number of rows.</summary>
    public int RowCount => _rowCount;

    /// <summary>Gets a column by index.</summary>
    /// <param name="index">The column index.</param>
    /// <returns>The column.</returns>
    /// <exception cref="IndexOutOfRangeException"><paramref name="index"/> is outside the columns.</exception>
    public DataColumn Column(int index) => _columns[index];

    /// <summary>Looks a column up by name, ordinally.</summary>
    /// <param name="name">The column name.</param>
    /// <param name="index">The column index when present.</param>
    /// <returns><see langword="true"/> when the column is present.</returns>
    public bool TryGetColumnIndex(string name, out int index) => Schema.TryGetIndex(name, out index);

    /// <summary>Looks a column up by name, ordinally.</summary>
    /// <param name="name">The column name.</param>
    /// <param name="column">The column when present.</param>
    /// <returns><see langword="true"/> when the column is present.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGetColumn(string name, out DataColumn? column)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (Schema.TryGetIndex(name, out var index))
        {
            column = _columns[index];
            return true;
        }

        column = null;
        return false;
    }

    /// <summary>Gets the value at a row and column.</summary>
    /// <param name="row">The row index.</param>
    /// <param name="column">The column index.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="row"/> is outside the rows.</exception>
    /// <exception cref="IndexOutOfRangeException"><paramref name="column"/> is outside the columns.</exception>
    public DataValue this[int row, int column] => _columns[column].Get(row);

    /// <summary>
    /// Sets the value at a row and column. The value is validated first, so a
    /// mismatched kind leaves the table unchanged.
    /// </summary>
    /// <param name="row">The row index.</param>
    /// <param name="column">The column index.</param>
    /// <param name="value">The value; a mismatched kind is a programmer error.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="row"/> or <paramref name="column"/> is outside the table.</exception>
    /// <exception cref="ArgumentException">The value cannot be stored in the column's kind.</exception>
    public void SetCell(int row, int column, DataValue value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, _rowCount);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, _columns.Length);

        _columns[column].Set(row, value);
    }

    /// <summary>Gets a row view.</summary>
    /// <param name="index">The row index.</param>
    /// <returns>The row.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the rows.</exception>
    public DataRow GetRow(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _rowCount);

        return new DataRow(this, index);
    }

    /// <summary>
    /// Appends a row. The values are validated first, so a mismatched value
    /// leaves the table unchanged.
    /// </summary>
    /// <param name="values">The values, one per column, in column order.</param>
    /// <exception cref="ArgumentException">The value count does not match the columns, or a value cannot be stored in its column.</exception>
    public void AddRow(ReadOnlySpan<DataValue> values)
    {
        ValidateRow(values);

        for (var index = 0; index < _columns.Length; index++)
        {
            _columns[index].AddUnchecked(values[index]);
        }

        _rowCount++;
    }

    /// <summary>
    /// Inserts a row at an index, shifting the later rows down. The values are
    /// validated first, so a mismatched value leaves the table unchanged.
    /// </summary>
    /// <param name="index">The row index to insert at, from zero to <see cref="RowCount"/>.</param>
    /// <param name="values">The values, one per column, in column order.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the rows.</exception>
    /// <exception cref="ArgumentException">The value count does not match the columns, or a value cannot be stored in its column.</exception>
    public void InsertRow(int index, ReadOnlySpan<DataValue> values)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _rowCount);
        ValidateRow(values);

        for (var column = 0; column < _columns.Length; column++)
        {
            _columns[column].Insert(index, values[column]);
        }

        _rowCount++;
    }

    /// <summary>Removes a row at an index, shifting the later rows up.</summary>
    /// <param name="index">The row index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the rows.</exception>
    public void RemoveRow(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _rowCount);

        for (var column = 0; column < _columns.Length; column++)
        {
            _columns[column].RemoveAt(index);
        }

        _rowCount--;
    }

    /// <summary>Appends a column, filled with nulls for the existing rows.</summary>
    /// <param name="field">The field (name and kind).</param>
    /// <exception cref="ArgumentException">A column with the same name already exists, or the kind is invalid.</exception>
    public void AddColumn(DataField field)
    {
        var column = CreatePaddedColumn(field);
        var columns = new DataColumn[_columns.Length + 1];
        Array.Copy(_columns, columns, _columns.Length);
        columns[^1] = column;
        Commit(columns);
    }

    /// <summary>Inserts a column at an index, filled with nulls for the existing rows.</summary>
    /// <param name="index">The column index to insert at, from zero to <see cref="ColumnCount"/>.</param>
    /// <param name="field">The field (name and kind).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the columns.</exception>
    /// <exception cref="ArgumentException">A column with the same name already exists, or the kind is invalid.</exception>
    public void InsertColumn(int index, DataField field)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _columns.Length);

        var columns = new DataColumn[_columns.Length + 1];

        for (var source = 0; source < index; source++)
        {
            columns[source] = _columns[source];
        }

        columns[index] = CreatePaddedColumn(field);

        for (var source = index; source < _columns.Length; source++)
        {
            columns[source + 1] = _columns[source];
        }

        Commit(columns);
    }

    /// <summary>Removes the column at an index.</summary>
    /// <param name="index">The column index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the columns.</exception>
    public void RemoveColumnAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _columns.Length);

        var columns = new DataColumn[_columns.Length - 1];
        var target = 0;

        for (var source = 0; source < _columns.Length; source++)
        {
            if (source != index)
            {
                columns[target++] = _columns[source];
            }
        }

        Commit(columns);
    }

    /// <summary>Renames the column at an index.</summary>
    /// <param name="index">The column index.</param>
    /// <param name="name">The new name.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the columns.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, or another column already has it.</exception>
    public void RenameColumn(int index, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _columns.Length);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var fields = CurrentFields();
        fields[index] = new DataField(name, _columns[index].Kind);

        // Validate before mutating, so a duplicate name leaves the table unchanged.
        var schema = new DataSchema(fields);
        _columns[index].Rename(name);
        Schema = schema;
    }

    /// <summary>Moves a column to another position.</summary>
    /// <param name="from">The column index to move.</param>
    /// <param name="to">The destination index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="from"/> or <paramref name="to"/> is outside the columns.</exception>
    public void MoveColumn(int from, int to)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, _columns.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(to);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(to, _columns.Length);

        if (from == to)
        {
            return;
        }

        var columns = new DataColumn[_columns.Length];
        var target = 0;

        for (var source = 0; source < _columns.Length; source++)
        {
            if (source == from)
            {
                continue;
            }

            if (target == to)
            {
                target++;
            }

            columns[target++] = _columns[source];
        }

        columns[to] = _columns[from];
        Commit(columns);
    }

    /// <summary>
    /// Replaces the column at an index with a new field and values, in one atomic
    /// commit. The value count, the values' kinds and the new name are validated
    /// first, so a rejected call leaves the table unchanged.
    /// </summary>
    /// <param name="index">The column index.</param>
    /// <param name="field">The new field (name and kind).</param>
    /// <param name="values">The values, one per row, in row order.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the columns.</exception>
    /// <exception cref="ArgumentException">
    /// The value count does not match the rows, a value cannot be stored in the
    /// new kind, or another column already has the new name.
    /// </exception>
    public void ReplaceColumnAt(int index, DataField field, ReadOnlySpan<DataValue> values)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _columns.Length);

        if (values.Length != _rowCount)
        {
            throw new ArgumentException(
                $"A column of {values.Length} values does not match the {_rowCount} rows of '{Name}'.",
                nameof(values));
        }

        for (var row = 0; row < values.Length; row++)
        {
            if (!DataColumn.CanStore(field.Kind, values[row]))
            {
                throw new ArgumentException(
                    $"A {values[row].Kind} value cannot be stored in the {field.Kind} column '{field.Name}'.",
                    nameof(values));
            }
        }

        var column = new DataColumn(field.Name, field.Kind);

        for (var row = 0; row < values.Length; row++)
        {
            column.AddUnchecked(values[row]);
        }

        var columns = new DataColumn[_columns.Length];
        Array.Copy(_columns, columns, _columns.Length);
        columns[index] = column;
        Commit(columns);
    }

    private void ValidateRow(ReadOnlySpan<DataValue> values)
    {
        if (values.Length != _columns.Length)
        {
            throw new ArgumentException(
                $"A row of {values.Length} values does not match the {_columns.Length} columns of '{Name}'.",
                nameof(values));
        }

        for (var index = 0; index < _columns.Length; index++)
        {
            if (!DataColumn.CanStore(_columns[index].Kind, values[index]))
            {
                throw new ArgumentException(
                    $"A {values[index].Kind} value cannot be stored in the {_columns[index].Kind} column '{_columns[index].Name}'.",
                    nameof(values));
            }
        }
    }

    private DataColumn CreatePaddedColumn(DataField field)
    {
        var column = new DataColumn(field.Name, field.Kind);

        for (var row = 0; row < _rowCount; row++)
        {
            column.AddUnchecked(DataValue.Null);
        }

        return column;
    }

    private DataField[] CurrentFields()
    {
        var fields = new DataField[_columns.Length];

        for (var index = 0; index < _columns.Length; index++)
        {
            fields[index] = new DataField(_columns[index].Name, _columns[index].Kind);
        }

        return fields;
    }

    private void Commit(DataColumn[] columns)
    {
        // Build and validate the schema first, so a rejected edit is atomic.
        var fields = new DataField[columns.Length];

        for (var index = 0; index < columns.Length; index++)
        {
            fields[index] = new DataField(columns[index].Name, columns[index].Kind);
        }

        var schema = new DataSchema(fields);
        _columns = columns;
        Schema = schema;
    }
}
