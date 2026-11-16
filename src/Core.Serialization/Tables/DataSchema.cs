// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Values;

namespace Core.Serialization.Tables;

/// <summary>
/// The ordered fields of a <see cref="DataTable"/>: the column names and the
/// kind of value each column stores, with an ordinal name lookup.
/// </summary>
public sealed class DataSchema
{
    private readonly DataField[] _fields;
    private readonly Dictionary<string, int> _indexByName;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataSchema"/> class.
    /// </summary>
    /// <param name="fields">The fields, in column order; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fields"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Two fields share a name.</exception>
    public DataSchema(IEnumerable<DataField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        _fields = [.. fields];
        _indexByName = new Dictionary<string, int>(_fields.Length, StringComparer.Ordinal);

        for (var index = 0; index < _fields.Length; index++)
        {
            if (!_indexByName.TryAdd(_fields[index].Name, index))
            {
                throw new ArgumentException($"A field named '{_fields[index].Name}' appears more than once.", nameof(fields));
            }
        }
    }

    /// <summary>Gets an empty schema.</summary>
    public static DataSchema Empty { get; } = new([]);

    /// <summary>Gets the number of fields.</summary>
    public int Count => _fields.Length;

    /// <summary>Gets the fields, in column order.</summary>
    public IReadOnlyList<DataField> Fields => _fields;

    /// <summary>Gets the field at a column index.</summary>
    /// <param name="index">The column index.</param>
    /// <returns>The field.</returns>
    /// <exception cref="IndexOutOfRangeException"><paramref name="index"/> is outside the fields.</exception>
    public DataField this[int index] => _fields[index];

    /// <summary>Looks a field up by name, ordinally.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="index">The column index when the field is present.</param>
    /// <returns><see langword="true"/> when the field is present.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGetIndex(string name, out int index)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _indexByName.TryGetValue(name, out index);
    }

    /// <summary>Gets the value kind of a field.</summary>
    /// <param name="index">The column index.</param>
    /// <returns>The kind.</returns>
    /// <exception cref="IndexOutOfRangeException"><paramref name="index"/> is outside the fields.</exception>
    public DataKind KindOf(int index) => _fields[index].Kind;
}
