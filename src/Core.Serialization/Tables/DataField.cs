// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Serialization.Tables;

/// <summary>
/// One field of a <see cref="DataSchema"/>: a column name and the kind of value
/// its column stores.
/// </summary>
public readonly struct DataField : IEquatable<DataField>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataField"/> struct.
    /// </summary>
    /// <param name="name">The field name; must not be <see langword="null"/>.</param>
    /// <param name="kind">The value kind; must not be <see cref="Values.DataKind.Null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is <see cref="Values.DataKind.Null"/>.</exception>
    public DataField(string name, Values.DataKind kind)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (kind == Values.DataKind.Null)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A field must have a concrete kind.");
        }

        Name = name;
        Kind = kind;
    }

    /// <summary>Gets the field name.</summary>
    public string Name { get; }

    /// <summary>Gets the value kind of the field's column.</summary>
    public Values.DataKind Kind { get; }

    /// <inheritdoc />
    public bool Equals(DataField other) =>
        Kind == other.Kind && string.Equals(Name, other.Name, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DataField other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, Kind);

    /// <summary>Compares two fields.</summary>
    /// <param name="left">The first field.</param>
    /// <param name="right">The second field.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public static bool operator ==(DataField left, DataField right) => left.Equals(right);

    /// <summary>Compares two fields.</summary>
    /// <param name="left">The first field.</param>
    /// <param name="right">The second field.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(DataField left, DataField right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => $"{Name}:{Kind}";
}
