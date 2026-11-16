// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Serialization.Mapping;

using Core.Serialization.Tables;
using Core.Serialization.Values;

/// <summary>
/// The two-text-column <c>{key, value}</c> shape the INI codec uses, with a
/// lookup that reads it the way a <c>key = value</c> config does: a repeated key
/// is several rows, so the first value wins and every value is available.
/// </summary>
public static class KeyValueTable
{
    /// <summary>The key column name.</summary>
    public const string KeyColumnName = "key";

    /// <summary>The value column name.</summary>
    public const string ValueColumnName = "value";

    /// <summary>Creates an empty key/value table.</summary>
    /// <param name="name">The table name.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static DataTable Create(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new DataTable(
            name,
            new DataSchema([new DataField(KeyColumnName, DataKind.Text), new DataField(ValueColumnName, DataKind.Text)]));
    }

    /// <summary>Gets the first value of a key.</summary>
    /// <param name="table">The key/value table; must not be <see langword="null"/>.</param>
    /// <param name="key">The key.</param>
    /// <param name="value">The first value when the key is present; otherwise an empty string.</param>
    /// <returns><see langword="true"/> when the key is present.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="key"/> is <see langword="null"/>.</exception>
    public static bool TryGetValue(DataTable table, string key, out string value)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(key);

        for (var row = 0; row < table.RowCount; row++)
        {
            if (Matches(table, row, key))
            {
                value = Text(table[row, 1]);
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    /// <summary>Gets every value of a key, in row order.</summary>
    /// <param name="table">The key/value table; must not be <see langword="null"/>.</param>
    /// <param name="key">The key.</param>
    /// <returns>The values; empty when the key is absent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="key"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<string> GetValues(DataTable table, string key)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(key);

        var values = new List<string>();

        for (var row = 0; row < table.RowCount; row++)
        {
            if (Matches(table, row, key))
            {
                values.Add(Text(table[row, 1]));
            }
        }

        return values;
    }

    private static bool Matches(DataTable table, int row, string key) =>
        table.ColumnCount >= 2 &&
        string.Equals(Text(table[row, 0]), key, StringComparison.Ordinal);

    private static string Text(DataValue value) => value.IsNull ? string.Empty : value.ToInvariantText();
}
