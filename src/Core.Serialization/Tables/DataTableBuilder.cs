// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Values;

namespace Core.Serialization.Tables;

/// <summary>
/// Builds a <see cref="DataTable"/> column by column and row by row, so a caller
/// declares a schema without hand-writing a <see cref="DataSchema"/> and a value
/// array.
/// </summary>
public sealed class DataTableBuilder
{
    private readonly string _name;
    private readonly List<DataField> _fields = [];
    private readonly List<DataValue[]> _rows = [];

    private DataTableBuilder(string name)
    {
        _name = name;
    }

    /// <summary>Starts a named table.</summary>
    /// <param name="name">The table name.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static DataTableBuilder Create(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new DataTableBuilder(name);
    }

    /// <summary>Adds a column, in schema order.</summary>
    /// <param name="name">The column name.</param>
    /// <param name="kind">The column kind; must not be <see cref="DataKind.Null"/>.</param>
    /// <returns>This builder, for chaining.</returns>
    public DataTableBuilder Column(string name, DataKind kind)
    {
        _fields.Add(new DataField(name, kind));

        return this;
    }

    /// <summary>Adds a row, one value per column in schema order.</summary>
    /// <param name="values">The values.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    public DataTableBuilder Row(params DataValue[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        _rows.Add(values);

        return this;
    }

    /// <summary>Builds the table.</summary>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentException">A row does not match the columns, or a value cannot be stored in its column.</exception>
    public DataTable Build()
    {
        var table = new DataTable(_name, new DataSchema(_fields));

        foreach (var row in _rows)
        {
            table.AddRow(row);
        }

        return table;
    }
}
