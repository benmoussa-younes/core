// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.InteropServices;
using Core.Serialization.Values;

namespace Core.Serialization.Tables;

/// <summary>
/// One column of a <see cref="DataTable"/>: a name, a declared
/// <see cref="DataKind"/> and the values in row order.
/// </summary>
/// <remarks>
/// Storage is one typed list per kind plus a null flag per value-type cell: a
/// <c>Nullable&lt;long&gt;</c> is sixteen bytes because the flag is padded to the
/// value's alignment, so a separate <c>bool</c> beside a <c>long</c> stores the
/// same information in nine. A reference kind (text, node) carries its own null.
/// An integer value widens into a number or decimal column; no other kind
/// converts, so a mismatched value is a failure before it is stored.
/// </remarks>
public sealed class DataColumn
{
    // The storage kinds are exactly the concrete DataKinds, so every switch over
    // them is exhaustive with one reachable arm per kind and no dead default.
    private enum Storage
    {
        Text,
        Node,
        Boolean,
        Integer,
        Number,
        Decimal,
        Date,
        Time,
        Timestamp,
    }

    private readonly Storage _storage;
    private readonly List<string?>? _text;
    private readonly List<DataNode?>? _node;
    private readonly List<bool>? _boolean;
    private readonly List<bool>? _booleanNulls;
    private readonly List<long>? _integer;
    private readonly List<bool>? _integerNulls;
    private readonly List<double>? _number;
    private readonly List<bool>? _numberNulls;
    private readonly List<decimal>? _decimal;
    private readonly List<bool>? _decimalNulls;
    private readonly List<DateOnly>? _date;
    private readonly List<bool>? _dateNulls;
    private readonly List<TimeOnly>? _time;
    private readonly List<bool>? _timeNulls;
    private readonly List<DateTime>? _timestamp;
    private readonly List<bool>? _timestampNulls;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataColumn"/> class.
    /// </summary>
    /// <param name="name">The column name; must not be <see langword="null"/>.</param>
    /// <param name="kind">The value kind; must not be <see cref="DataKind.Null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is <see cref="DataKind.Null"/> or unknown.</exception>
    internal DataColumn(string name, DataKind kind)
    {
        ArgumentNullException.ThrowIfNull(name);

        Name = name;
        Kind = kind;
        _storage = Map(kind);

        switch (_storage)
        {
            case Storage.Text:
                _text = [];
                break;
            case Storage.Node:
                _node = [];
                break;
            case Storage.Boolean:
                _boolean = [];
                _booleanNulls = [];
                break;
            case Storage.Integer:
                _integer = [];
                _integerNulls = [];
                break;
            case Storage.Number:
                _number = [];
                _numberNulls = [];
                break;
            case Storage.Decimal:
                _decimal = [];
                _decimalNulls = [];
                break;
            case Storage.Date:
                _date = [];
                _dateNulls = [];
                break;
            case Storage.Time:
                _time = [];
                _timeNulls = [];
                break;
            case Storage.Timestamp:
                _timestamp = [];
                _timestampNulls = [];
                break;
        }
    }

    /// <summary>Gets the column name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the declared value kind.</summary>
    public DataKind Kind { get; }

    /// <summary>Gets the number of values.</summary>
    public int Count
    {
        get
        {
            int count = 0;

            switch (_storage)
            {
                case Storage.Text:
                    count = _text!.Count;
                    break;
                case Storage.Node:
                    count = _node!.Count;
                    break;
                case Storage.Boolean:
                    count = _boolean!.Count;
                    break;
                case Storage.Integer:
                    count = _integer!.Count;
                    break;
                case Storage.Number:
                    count = _number!.Count;
                    break;
                case Storage.Decimal:
                    count = _decimal!.Count;
                    break;
                case Storage.Date:
                    count = _date!.Count;
                    break;
                case Storage.Time:
                    count = _time!.Count;
                    break;
                case Storage.Timestamp:
                    count = _timestamp!.Count;
                    break;
            }

            return count;
        }
    }

    /// <summary>Determines whether a value can be stored in a column of a kind.</summary>
    /// <param name="kind">The column kind.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when the value can be stored.</returns>
    internal static bool CanStore(DataKind kind, DataValue value)
    {
        if (value.IsNull)
        {
            return true;
        }

        bool canStore = false;

        switch (kind)
        {
            case DataKind.Null:
                canStore = false;
                break;
            case DataKind.Boolean:
                canStore = value.Kind == DataKind.Boolean;
                break;
            case DataKind.Integer:
                canStore = value.Kind == DataKind.Integer;
                break;
            case DataKind.Number:
                canStore = value.Kind is DataKind.Number or DataKind.Integer;
                break;
            case DataKind.Decimal:
                canStore = value.Kind is DataKind.Decimal or DataKind.Integer;
                break;
            case DataKind.Text:
                canStore = value.Kind == DataKind.Text;
                break;
            case DataKind.Node:
                canStore = value.Kind == DataKind.Node;
                break;
            case DataKind.Date:
                canStore = value.Kind == DataKind.Date;
                break;
            case DataKind.Time:
                canStore = value.Kind == DataKind.Time;
                break;
            case DataKind.Timestamp:
                canStore = value.Kind == DataKind.Timestamp;
                break;
        }

        return canStore;
    }

    /// <summary>Gets the values as a span, without the per-cell value round-trip.</summary>
    /// <exception cref="InvalidOperationException">The column is not an integer column.</exception>
    public ReadOnlySpan<long> Integers => Typed(Storage.Integer, _integer);

    /// <summary>Gets the values as a span, without the per-cell value round-trip.</summary>
    /// <exception cref="InvalidOperationException">The column is not a number column.</exception>
    public ReadOnlySpan<double> Numbers => Typed(Storage.Number, _number);

    /// <summary>Gets the values as a span, without the per-cell value round-trip.</summary>
    /// <exception cref="InvalidOperationException">The column is not a boolean column.</exception>
    public ReadOnlySpan<bool> Booleans => Typed(Storage.Boolean, _boolean);

    /// <summary>Gets the values as a span, without boxing a decimal per cell.</summary>
    /// <exception cref="InvalidOperationException">The column is not a decimal column.</exception>
    public ReadOnlySpan<decimal> Decimals => Typed(Storage.Decimal, _decimal);

    /// <summary>Gets the values as a span, without the per-cell value round-trip.</summary>
    /// <exception cref="InvalidOperationException">The column is not a date column.</exception>
    public ReadOnlySpan<DateOnly> Dates => Typed(Storage.Date, _date);

    /// <summary>Gets the values as a span, without the per-cell value round-trip.</summary>
    /// <exception cref="InvalidOperationException">The column is not a time column.</exception>
    public ReadOnlySpan<TimeOnly> Times => Typed(Storage.Time, _time);

    /// <summary>Gets the values as a span, without the per-cell value round-trip.</summary>
    /// <exception cref="InvalidOperationException">The column is not a timestamp column.</exception>
    public ReadOnlySpan<DateTime> Timestamps => Typed(Storage.Timestamp, _timestamp);

    /// <summary>Gets the values as a span, without the per-cell value round-trip.</summary>
    /// <exception cref="InvalidOperationException">The column is not a text column.</exception>
    public ReadOnlySpan<string?> Texts => Typed(Storage.Text, _text);

    private ReadOnlySpan<T> Typed<T>(Storage storage, List<T>? values) => _storage == storage
        ? CollectionsMarshal.AsSpan(values!)
        : throw new InvalidOperationException($"The column '{Name}' is {Kind}, not {storage}.");

    /// <summary>Reads the value at a row.</summary>
    /// <param name="index">The row index.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the rows.</exception>
    public DataValue Get(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

        DataValue value = DataValue.Null;

        switch (_storage)
        {
            case Storage.Text:
                value = _text![index] is { } text ? DataValue.From(text) : DataValue.Null;
                break;
            case Storage.Node:
                value = _node![index] is { } node ? DataValue.FromNode(node) : DataValue.Null;
                break;
            case Storage.Boolean:
                value = _booleanNulls![index] ? DataValue.Null : DataValue.From(_boolean![index]);
                break;
            case Storage.Integer:
                value = _integerNulls![index] ? DataValue.Null : DataValue.From(_integer![index]);
                break;
            case Storage.Number:
                value = _numberNulls![index] ? DataValue.Null : DataValue.From(_number![index]);
                break;
            case Storage.Decimal:
                value = _decimalNulls![index] ? DataValue.Null : DataValue.From(_decimal![index]);
                break;
            case Storage.Date:
                value = _dateNulls![index] ? DataValue.Null : DataValue.From(_date![index]);
                break;
            case Storage.Time:
                value = _timeNulls![index] ? DataValue.Null : DataValue.From(_time![index]);
                break;
            case Storage.Timestamp:
                value = _timestampNulls![index] ? DataValue.Null : DataValue.From(_timestamp![index]);
                break;
        }

        return value;
    }

    internal bool HasNulls() => _storage switch
    {
        Storage.Boolean => _booleanNulls!.Contains(true),
        Storage.Integer => _integerNulls!.Contains(true),
        Storage.Number => _numberNulls!.Contains(true),
        Storage.Decimal => _decimalNulls!.Contains(true),
        Storage.Date => _dateNulls!.Contains(true),
        Storage.Time => _timeNulls!.Contains(true),
        Storage.Timestamp => _timestampNulls!.Contains(true),
        Storage.Text => _text!.Contains(null),
        Storage.Node => _node!.Contains(null),
        _ => false,
    };

    /// <summary>Copies the null flags into a caller-owned array, without a value round-trip.</summary>
    /// <param name="nulls">The destination; its length is the column count.</param>
    internal void FillNulls(bool[] nulls)
    {
        switch (_storage)
        {
            case Storage.Text:
                for (var index = 0; index < _text!.Count; index++)
                {
                    nulls[index] = _text[index] is null;
                }

                break;
            case Storage.Node:
                for (var index = 0; index < _node!.Count; index++)
                {
                    nulls[index] = _node[index] is null;
                }

                break;
            case Storage.Boolean:
                _booleanNulls!.CopyTo(nulls);
                break;
            case Storage.Integer:
                _integerNulls!.CopyTo(nulls);
                break;
            case Storage.Number:
                _numberNulls!.CopyTo(nulls);
                break;
            case Storage.Decimal:
                _decimalNulls!.CopyTo(nulls);
                break;
            case Storage.Date:
                _dateNulls!.CopyTo(nulls);
                break;
            case Storage.Time:
                _timeNulls!.CopyTo(nulls);
                break;
            case Storage.Timestamp:
                _timestampNulls!.CopyTo(nulls);
                break;
        }
    }

    /// <summary>Appends a value at the end of the column.</summary>
    /// <param name="value">The value; a mismatched kind is a programmer error.</param>
    /// <exception cref="ArgumentException">The value cannot be stored in this column's kind.</exception>
    internal void Add(DataValue value)
    {
        if (!CanStore(Kind, value))
        {
            throw Mismatch(value);
        }

        Append(value);
    }

    /// <summary>Appends a value the caller has already validated.</summary>
    /// <param name="value">The value, known to be storable.</param>
    internal void AddUnchecked(DataValue value) => Append(value);

    /// <summary>Replaces the value at a row.</summary>
    /// <param name="index">The row index.</param>
    /// <param name="value">The value; a mismatched kind is a programmer error.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the rows.</exception>
    /// <exception cref="ArgumentException">The value cannot be stored in this column's kind.</exception>
    public void Set(int index, DataValue value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

        if (!CanStore(Kind, value))
        {
            throw Mismatch(value);
        }

        WriteCore(index, value, replace: true);
    }

    /// <summary>Inserts a value at a row, shifting the later values down.</summary>
    /// <param name="index">The row index to insert at, from zero to <see cref="Count"/>.</param>
    /// <param name="value">The value; a mismatched kind is a programmer error.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the rows.</exception>
    /// <exception cref="ArgumentException">The value cannot be stored in this column's kind.</exception>
    public void Insert(int index, DataValue value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, Count);

        if (!CanStore(Kind, value))
        {
            throw Mismatch(value);
        }

        WriteCore(index, value, replace: false);
    }

    /// <summary>Removes the value at a row, shifting the later values up.</summary>
    /// <param name="index">The row index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the rows.</exception>
    public void RemoveAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

        RemoveCore(index);
    }

    /// <summary>Renames the column; the caller rebuilds the table's schema.</summary>
    /// <param name="name">The new name.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    internal void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
    }

    private static Storage Map(DataKind kind) => kind switch
    {
        DataKind.Text => Storage.Text,
        DataKind.Node => Storage.Node,
        DataKind.Boolean => Storage.Boolean,
        DataKind.Integer => Storage.Integer,
        DataKind.Number => Storage.Number,
        DataKind.Decimal => Storage.Decimal,
        DataKind.Date => Storage.Date,
        DataKind.Time => Storage.Time,
        DataKind.Timestamp => Storage.Timestamp,
        DataKind.Null => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A column must have a concrete kind."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown column kind."),
    };

    private void Append(DataValue value)
    {
        switch (_storage)
        {
            case Storage.Text:
                _text!.Add(value.IsNull ? null : value.ToInvariantText());
                break;
            case Storage.Node:
                _node!.Add(value.TryGetNode(out var node) ? node : null);
                break;
            case Storage.Boolean:
                _booleanNulls!.Add(value.IsNull);
                _boolean!.Add(value.TryGetBoolean(out var boolean) && boolean);
                break;
            case Storage.Integer:
                _integerNulls!.Add(value.IsNull);
                _integer!.Add(value.TryGetInteger(out var integer) ? integer : 0L);
                break;
            case Storage.Number:
                _numberNulls!.Add(value.IsNull);
                _number!.Add(value.TryGetNumber(out var number) ? number : 0d);
                break;
            case Storage.Decimal:
                _decimalNulls!.Add(value.IsNull);
                _decimal!.Add(value.TryGetDecimal(out var decimalValue) ? decimalValue : 0m);
                break;
            case Storage.Date:
                _dateNulls!.Add(value.IsNull);
                _date!.Add(value.TryGetDate(out var date) ? date : default);
                break;
            case Storage.Time:
                _timeNulls!.Add(value.IsNull);
                _time!.Add(value.TryGetTime(out var time) ? time : default);
                break;
            case Storage.Timestamp:
                _timestampNulls!.Add(value.IsNull);
                _timestamp!.Add(value.TryGetTimestamp(out var timestamp) ? timestamp : default);
                break;
        }
    }

    // One arm per storage kind: replace at the index (Set) or insert and shift the
    // later values (Insert). The append path is kept separate because
    // `Insert(Count, x)` is slower than `Add` and the table build is hot.
    private void WriteCore(int index, DataValue value, bool replace)
    {
        switch (_storage)
        {
            case Storage.Text:
                {
                    var text = value.IsNull ? null : value.ToInvariantText();
                    if (replace) { _text![index] = text; } else { _text!.Insert(index, text); }
                    break;
                }

            case Storage.Node:
                {
                    var node = value.TryGetNode(out var nodeValue) ? nodeValue : null;
                    if (replace) { _node![index] = node; } else { _node!.Insert(index, node); }
                    break;
                }

            case Storage.Boolean:
                {
                    var isNull = value.IsNull;
                    var typed = value.TryGetBoolean(out var boolean) && boolean;
                    if (replace) { _booleanNulls![index] = isNull; _boolean![index] = typed; }
                    else { _booleanNulls!.Insert(index, isNull); _boolean!.Insert(index, typed); }
                    break;
                }

            case Storage.Integer:
                {
                    var isNull = value.IsNull;
                    var typed = value.TryGetInteger(out var integer) ? integer : 0L;
                    if (replace) { _integerNulls![index] = isNull; _integer![index] = typed; }
                    else { _integerNulls!.Insert(index, isNull); _integer!.Insert(index, typed); }
                    break;
                }

            case Storage.Number:
                {
                    var isNull = value.IsNull;
                    var typed = value.TryGetNumber(out var number) ? number : 0d;
                    if (replace) { _numberNulls![index] = isNull; _number![index] = typed; }
                    else { _numberNulls!.Insert(index, isNull); _number!.Insert(index, typed); }
                    break;
                }

            case Storage.Decimal:
                {
                    var isNull = value.IsNull;
                    var typed = value.TryGetDecimal(out var decimalValue) ? decimalValue : 0m;
                    if (replace) { _decimalNulls![index] = isNull; _decimal![index] = typed; }
                    else { _decimalNulls!.Insert(index, isNull); _decimal!.Insert(index, typed); }
                    break;
                }

            case Storage.Date:
                {
                    var isNull = value.IsNull;
                    var typed = value.TryGetDate(out var date) ? date : default;
                    if (replace) { _dateNulls![index] = isNull; _date![index] = typed; }
                    else { _dateNulls!.Insert(index, isNull); _date!.Insert(index, typed); }
                    break;
                }

            case Storage.Time:
                {
                    var isNull = value.IsNull;
                    var typed = value.TryGetTime(out var time) ? time : default;
                    if (replace) { _timeNulls![index] = isNull; _time![index] = typed; }
                    else { _timeNulls!.Insert(index, isNull); _time!.Insert(index, typed); }
                    break;
                }

            case Storage.Timestamp:
                {
                    var isNull = value.IsNull;
                    var typed = value.TryGetTimestamp(out var timestamp) ? timestamp : default;
                    if (replace) { _timestampNulls![index] = isNull; _timestamp![index] = typed; }
                    else { _timestampNulls!.Insert(index, isNull); _timestamp!.Insert(index, typed); }
                    break;
                }
        }
    }

    private void RemoveCore(int index)
    {
        switch (_storage)
        {
            case Storage.Text:
                _text!.RemoveAt(index);
                break;
            case Storage.Node:
                _node!.RemoveAt(index);
                break;
            case Storage.Boolean:
                _booleanNulls!.RemoveAt(index);
                _boolean!.RemoveAt(index);
                break;
            case Storage.Integer:
                _integerNulls!.RemoveAt(index);
                _integer!.RemoveAt(index);
                break;
            case Storage.Number:
                _numberNulls!.RemoveAt(index);
                _number!.RemoveAt(index);
                break;
            case Storage.Decimal:
                _decimalNulls!.RemoveAt(index);
                _decimal!.RemoveAt(index);
                break;
            case Storage.Date:
                _dateNulls!.RemoveAt(index);
                _date!.RemoveAt(index);
                break;
            case Storage.Time:
                _timeNulls!.RemoveAt(index);
                _time!.RemoveAt(index);
                break;
            case Storage.Timestamp:
                _timestampNulls!.RemoveAt(index);
                _timestamp!.RemoveAt(index);
                break;
        }
    }

    private ArgumentException Mismatch(DataValue value) =>
        new($"A {value.Kind} value cannot be stored in the {Kind} column '{Name}'.", nameof(value));
}
