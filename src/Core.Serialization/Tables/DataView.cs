// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers;
using Core.Results;
using Core.Serialization.Values;

namespace Core.Serialization.Tables;

/// <summary>
/// An ordered, filtered view over a <see cref="DataTable"/>: the table is not
/// mutated, the view holds row indexes, and a sort is stable (equal rows keep
/// their original order).
/// </summary>
public sealed class DataView
{
    private readonly int[] _rows;

    /// <summary>Initializes a view over every row of a table.</summary>
    /// <param name="table">The table.</param>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    public DataView(DataTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        Table = table;
        _rows = new int[table.RowCount];

        for (var index = 0; index < _rows.Length; index++)
        {
            _rows[index] = index;
        }
    }

    private DataView(DataTable table, int[] rows)
    {
        Table = table;
        _rows = rows;
    }

    /// <summary>Gets the underlying table.</summary>
    public DataTable Table { get; }

    /// <summary>Gets the number of visible rows.</summary>
    public int Count => _rows.Length;

    /// <summary>
    /// Gets the underlying <em>table</em> row indexes, in view order: each element
    /// can index a typed column span or be passed to a table predicate.
    /// </summary>
    public ReadOnlySpan<int> RowIndexes => _rows;

    /// <summary>Gets one visible row.</summary>
    /// <param name="index">The view index, from zero to <see cref="Count"/> minus one.</param>
    /// <returns>The row.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the view.</exception>
    public DataRow this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _rows.Length);

            return Table.GetRow(_rows[index]);
        }
    }

    /// <summary>Returns a stable, multi-column sort of this view.</summary>
    /// <param name="keys">The sort keys, most significant first; empty keeps the current order.</param>
    /// <returns>
    /// The sorted view, or a failure with code <c>data.invalid_sort</c> when a key
    /// names an empty or unknown column. This view is unchanged.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> is <see langword="null"/>.</exception>
    public Result<DataView> Sort(params SortKey[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Length == 0)
        {
            return Result<DataView>.Success(this);
        }

        if (keys.Length == 1 && TrySortInteger(keys[0], out var integerSort))
        {
            return integerSort;
        }

        var comparers = new KeyComparer[keys.Length];

        for (var index = 0; index < keys.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(keys[index].Column) ||
                !Table.TryGetColumn(keys[index].Column, out var column))
            {
                return Result<DataView>.Failure(new CoreError(
                    CoreErrorCodes.DataInvalidSort,
                    $"'{keys[index].Column}' is not a column of the table."));
            }

            // A single-key sort carries the stable tie-break itself; a composite
            // sort applies it once after every key has tied.
            comparers[index] = KeyComparer.Create(
                column!,
                keys[index].Descending,
                Table.RowCount,
                tieBreak: keys.Length == 1);
        }

        var rows = (int[])_rows.Clone();

        IComparer<int> comparer = comparers.Length == 1
            ? comparers[0]
            : new CompositeKeyComparer(comparers);

        Array.Sort(rows, comparer);

        return Result<DataView>.Success(new DataView(Table, rows));
    }

    /// <summary>Returns a view of the rows a predicate accepts.</summary>
    /// <param name="predicate">The predicate, evaluated once per visible row.</param>
    /// <returns>The filtered view; this view is unchanged.</returns>
    /// <remarks>
    /// The predicate materialises a <see cref="DataRow"/> per visible row, which is
    /// the convenient form for a small table. For a large table use
    /// <see cref="FilterRows"/> with a typed column span instead: the benchmark
    /// measures this overload ~12x slower on 1 M rows.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public DataView Filter(Func<DataRow, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return Collect(index => predicate(Table.GetRow(index)));
    }

    // A pooled index buffer avoids the growing List's repeated copies; the exact
    // result array is the only allocation the caller keeps.
    private DataView Collect(Func<int, bool> predicate)
    {
        var buffer = ArrayPool<int>.Shared.Rent(_rows.Length);

        try
        {
            var count = 0;

            foreach (var index in _rows)
            {
                if (predicate(index))
                {
                    buffer[count++] = index;
                }
            }

            var rows = new int[count];
            Array.Copy(buffer, rows, count);
            return new DataView(Table, rows);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(buffer);
        }
    }

    private bool TrySortInteger(SortKey key, out Result<DataView> result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(key.Column) ||
            !Table.TryGetColumn(key.Column, out var column) ||
            column is null ||
            column.Kind != DataKind.Integer ||
            column.HasNulls())
        {
            return false;
        }

        var rows = (int[])_rows.Clone();
        RadixSort(rows, column.Integers, key.Descending);
        result = Result<DataView>.Success(new DataView(Table, rows));
        return true;
    }

    // Stable LSD radix over the row indexes: a single integer key with no nulls
    // is the hot path (1 M rows), and a comparison sort pays two virtual calls
    // and two key-array probes per comparison. The 11-bit pass keeps the 2 KiB
    // count table in L1; six passes cover 64 bits.
    private static void RadixSort(int[] rows, ReadOnlySpan<long> keys, bool descending)
    {
        if (rows.Length < 2)
        {
            return;
        }

        const int Bits = 11;
        const int Buckets = 1 << Bits;
        var buffer = ArrayPool<int>.Shared.Rent(rows.Length);
        var counts = ArrayPool<int>.Shared.Rent(Buckets);

        try
        {
            var source = rows;
            var target = buffer;

            for (var shift = 0; shift < 64; shift += Bits)
            {
                Array.Clear(counts, 0, Buckets);

                for (var index = 0; index < rows.Length; index++)
                {
                    var key = OrderKey(keys[source[index]], descending);
                    counts[(int)((key >> shift) & (Buckets - 1))]++;
                }

                var sum = 0;

                for (var bucket = 0; bucket < Buckets; bucket++)
                {
                    var count = counts[bucket];
                    counts[bucket] = sum;
                    sum += count;
                }

                for (var index = 0; index < rows.Length; index++)
                {
                    var row = source[index];
                    var key = OrderKey(keys[row], descending);
                    target[counts[(int)((key >> shift) & (Buckets - 1))]++] = row;
                }

                (source, target) = (target, source);
            }

            if (!ReferenceEquals(source, rows))
            {
                Array.Copy(source, rows, rows.Length);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(buffer);
            ArrayPool<int>.Shared.Return(counts);
        }
    }

    private static ulong OrderKey(long value, bool descending)
    {
        var key = (ulong)value ^ 0x8000_0000_0000_0000UL;
        return descending ? ~key : key;
    }

    private sealed class CompositeKeyComparer(KeyComparer[] keys) : IComparer<int>
    {
        public int Compare(int left, int right)
        {
            foreach (var key in keys)
            {
                var comparison = key.Compare(left, right);

                if (comparison != 0)
                {
                    return comparison;
                }
            }

            // The original index makes the sort stable: Array.Sort is not.
            return left.CompareTo(right);
        }
    }

    private abstract class KeyComparer(bool[] nulls, bool descending, bool tieBreak) : IComparer<int>
    {
        protected bool[] Nulls { get; } = nulls;

        protected bool Descending { get; } = descending;

        protected bool TieBreak { get; } = tieBreak;

        public int Compare(int left, int right)
        {
            var leftNull = Nulls[left];
            var rightNull = Nulls[right];

            // Nulls stay after every non-null value in both directions.
            if (leftNull != rightNull)
            {
                return leftNull ? 1 : -1;
            }

            if (leftNull)
            {
                return TieBreak ? left.CompareTo(right) : 0;
            }

            var comparison = CompareValues(left, right);

            if (comparison != 0)
            {
                return Descending ? -comparison : comparison;
            }

            // The original index makes the sort stable: Array.Sort is not.
            return TieBreak ? left.CompareTo(right) : 0;
        }

        public static KeyComparer Create(
            DataColumn column,
            bool descending,
            int rowCount,
            bool tieBreak)
        {
            var nulls = new bool[rowCount];
            column.FillNulls(nulls);

            switch (column.Kind)
            {
                case DataKind.Boolean:
                    var booleans = new bool[rowCount];
                    column.Booleans.CopyTo(booleans);
                    return new BooleanKeyComparer(booleans, nulls, descending, tieBreak);
                case DataKind.Integer:
                    var integers = new long[rowCount];
                    column.Integers.CopyTo(integers);
                    return new IntegerKeyComparer(integers, nulls, descending, tieBreak);
                case DataKind.Number:
                    var numbers = new double[rowCount];
                    column.Numbers.CopyTo(numbers);
                    return new NumberKeyComparer(numbers, nulls, descending, tieBreak);
                case DataKind.Decimal:
                    var decimals = new decimal[rowCount];
                    column.Decimals.CopyTo(decimals);
                    return new DecimalKeyComparer(decimals, nulls, descending, tieBreak);
                case DataKind.Date:
                    var dates = new DateOnly[rowCount];
                    column.Dates.CopyTo(dates);
                    return new DateKeyComparer(dates, nulls, descending, tieBreak);
                case DataKind.Time:
                    var times = new TimeOnly[rowCount];
                    column.Times.CopyTo(times);
                    return new TimeKeyComparer(times, nulls, descending, tieBreak);
                case DataKind.Timestamp:
                    var timestamps = new DateTime[rowCount];
                    column.Timestamps.CopyTo(timestamps);
                    return new TimestampKeyComparer(timestamps, nulls, descending, tieBreak);
                case DataKind.Text:
                    var texts = new string?[rowCount];
                    column.Texts.CopyTo(texts);
                    return new TextKeyComparer(texts, nulls, descending, tieBreak);
                default:
                    // A node column has no scalar span; its typed value array is the
                    // only comparable form.
                    var values = new DataValue[rowCount];

                    for (var row = 0; row < values.Length; row++)
                    {
                        values[row] = column.Get(row);
                    }

                    return new ValueKeyComparer(values, nulls, descending, tieBreak);
            }
        }

        protected abstract int CompareValues(int left, int right);
    }

    private sealed class BooleanKeyComparer(bool[] keys, bool[] nulls, bool descending, bool tieBreak)
        : KeyComparer(nulls, descending, tieBreak)
    {
        protected override int CompareValues(int left, int right) => keys[left].CompareTo(keys[right]);
    }

    private sealed class IntegerKeyComparer(long[] keys, bool[] nulls, bool descending, bool tieBreak)
        : KeyComparer(nulls, descending, tieBreak)
    {
        protected override int CompareValues(int left, int right) => keys[left].CompareTo(keys[right]);
    }

    private sealed class NumberKeyComparer(double[] keys, bool[] nulls, bool descending, bool tieBreak)
        : KeyComparer(nulls, descending, tieBreak)
    {
        protected override int CompareValues(int left, int right) => keys[left].CompareTo(keys[right]);
    }

    private sealed class DecimalKeyComparer(decimal[] keys, bool[] nulls, bool descending, bool tieBreak)
        : KeyComparer(nulls, descending, tieBreak)
    {
        protected override int CompareValues(int left, int right) => keys[left].CompareTo(keys[right]);
    }

    private sealed class DateKeyComparer(DateOnly[] keys, bool[] nulls, bool descending, bool tieBreak)
        : KeyComparer(nulls, descending, tieBreak)
    {
        protected override int CompareValues(int left, int right) => keys[left].CompareTo(keys[right]);
    }

    private sealed class TimeKeyComparer(TimeOnly[] keys, bool[] nulls, bool descending, bool tieBreak)
        : KeyComparer(nulls, descending, tieBreak)
    {
        protected override int CompareValues(int left, int right) => keys[left].CompareTo(keys[right]);
    }

    private sealed class TimestampKeyComparer(DateTime[] keys, bool[] nulls, bool descending, bool tieBreak)
        : KeyComparer(nulls, descending, tieBreak)
    {
        protected override int CompareValues(int left, int right) => keys[left].CompareTo(keys[right]);
    }

    private sealed class TextKeyComparer(string?[] keys, bool[] nulls, bool descending, bool tieBreak)
        : KeyComparer(nulls, descending, tieBreak)
    {
        protected override int CompareValues(int left, int right) =>
            string.CompareOrdinal(keys[left], keys[right]);
    }

    private sealed class ValueKeyComparer(DataValue[] keys, bool[] nulls, bool descending, bool tieBreak)
        : KeyComparer(nulls, descending, tieBreak)
    {
        protected override int CompareValues(int left, int right) =>
            DataValueComparer.Compare(keys[left], keys[right]);
    }

    /// <summary>
    /// Returns a view of the rows a predicate accepts, passing the <em>table</em>
    /// row index so a caller can use a typed column span without a
    /// <see cref="DataRow"/> round-trip.
    /// </summary>
    /// <param name="predicate">The predicate over the table row index.</param>
    /// <returns>The filtered view; this view is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public DataView FilterRows(Func<int, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return Collect(predicate);
    }

    /// <summary>Materializes the visible rows into a new table with the same schema.</summary>
    /// <returns>The new table.</returns>
    public DataTable ToTable()
    {
        var table = new DataTable(Table.Name, Table.Schema);
        var row = new DataValue[Table.ColumnCount];

        foreach (var index in _rows)
        {
            for (var column = 0; column < row.Length; column++)
            {
                row[column] = Table[index, column];
            }

            table.AddRow(row);
        }

        return table;
    }
}
