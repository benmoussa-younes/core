// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;
using Core.Serialization.Values;

namespace Core.Serialization.Tables;

/// <summary>
/// The aggregate functions a data screen needs over one column: count, sum,
/// average, minimum, maximum, first and last. Null cells are skipped; a sum of an
/// empty numeric column is zero, while an average, a minimum, a maximum and a
/// first/last with no non-null cell are <c>data.empty</c> failures.
/// </summary>
public static class DataAggregates
{
    /// <summary>Counts the non-null cells.</summary>
    /// <param name="column">The column.</param>
    /// <returns>The non-null count.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="column"/> is <see langword="null"/>.</exception>
    public static int CountNonNull(DataColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        var count = 0;

        for (var index = 0; index < column.Count; index++)
        {
            if (!column.Get(index).IsNull)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Sums a numeric column; nulls are skipped.</summary>
    /// <param name="column">The column.</param>
    /// <returns>The sum, or a failure with code <c>data.not_numeric</c> or <c>data.overflow</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="column"/> is <see langword="null"/>.</exception>
    public static Result<DataValue> Sum(DataColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        try
        {
            switch (column.Kind)
            {
                case DataKind.Integer:
                    var integerSum = 0L;

                    for (var index = 0; index < column.Count; index++)
                    {
                        var value = column.Get(index);

                        if (!value.IsNull)
                        {
                            _ = value.TryGetInteger(out var number);
                            integerSum = checked(integerSum + number);
                        }
                    }

                    return Result<DataValue>.Success(DataValue.From(integerSum));

                case DataKind.Decimal:
                    var decimalSum = 0m;

                    for (var index = 0; index < column.Count; index++)
                    {
                        var value = column.Get(index);

                        if (!value.IsNull)
                        {
                            _ = value.TryGetDecimal(out var number);
                            decimalSum = checked(decimalSum + number);
                        }
                    }

                    return Result<DataValue>.Success(DataValue.From(decimalSum));

                case DataKind.Number:
                    var numberSum = 0d;

                    for (var index = 0; index < column.Count; index++)
                    {
                        var value = column.Get(index);

                        if (!value.IsNull)
                        {
                            _ = value.TryGetNumber(out var number);
                            numberSum += number;
                        }
                    }

                    return double.IsFinite(numberSum)
                        ? Result<DataValue>.Success(DataValue.From(numberSum))
                        : Result<DataValue>.Failure(Overflow(column));

                default:
                    return Result<DataValue>.Failure(NotNumeric(column));
            }
        }
        catch (OverflowException)
        {
            return Result<DataValue>.Failure(Overflow(column));
        }
    }

    /// <summary>Averages a numeric column; nulls are skipped.</summary>
    /// <param name="column">The column.</param>
    /// <returns>The average, or a failure with code <c>data.empty</c>, <c>data.not_numeric</c> or <c>data.overflow</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="column"/> is <see langword="null"/>.</exception>
    public static Result<DataValue> Average(DataColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (column.Kind is not (DataKind.Integer or DataKind.Decimal or DataKind.Number))
        {
            return Result<DataValue>.Failure(NotNumeric(column));
        }

        var count = CountNonNull(column);

        if (count == 0)
        {
            return Result<DataValue>.Failure(Empty(column));
        }

        var sum = Sum(column);

        if (!sum.TryGetValue(out var total))
        {
            return Result<DataValue>.Failure(sum.Error);
        }

        return column.Kind switch
        {
            DataKind.Integer => total.TryGetInteger(out var integer)
                ? Result<DataValue>.Success(DataValue.From((double)integer / count))
                : Result<DataValue>.Failure(NotNumeric(column)),
            DataKind.Decimal => total.TryGetDecimal(out var number)
                ? Result<DataValue>.Success(DataValue.From(number / count))
                : Result<DataValue>.Failure(NotNumeric(column)),
            _ => total.TryGetNumber(out var floating)
                ? Result<DataValue>.Success(DataValue.From(floating / count))
                : Result<DataValue>.Failure(NotNumeric(column)),
        };
    }

    /// <summary>Finds the smallest non-null cell.</summary>
    /// <param name="column">The column.</param>
    /// <returns>The minimum, or a failure with code <c>data.empty</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="column"/> is <see langword="null"/>.</exception>
    public static Result<DataValue> Min(DataColumn column) => Extremum(column, maximum: false);

    /// <summary>Finds the largest non-null cell.</summary>
    /// <param name="column">The column.</param>
    /// <returns>The maximum, or a failure with code <c>data.empty</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="column"/> is <see langword="null"/>.</exception>
    public static Result<DataValue> Max(DataColumn column) => Extremum(column, maximum: true);

    /// <summary>Finds the first non-null cell.</summary>
    /// <param name="column">The column.</param>
    /// <returns>The first value, or a failure with code <c>data.empty</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="column"/> is <see langword="null"/>.</exception>
    public static Result<DataValue> First(DataColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        for (var index = 0; index < column.Count; index++)
        {
            var value = column.Get(index);

            if (!value.IsNull)
            {
                return Result<DataValue>.Success(value);
            }
        }

        return Result<DataValue>.Failure(Empty(column));
    }

    /// <summary>Finds the last non-null cell.</summary>
    /// <param name="column">The column.</param>
    /// <returns>The last value, or a failure with code <c>data.empty</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="column"/> is <see langword="null"/>.</exception>
    public static Result<DataValue> Last(DataColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        for (var index = column.Count - 1; index >= 0; index--)
        {
            var value = column.Get(index);

            if (!value.IsNull)
            {
                return Result<DataValue>.Success(value);
            }
        }

        return Result<DataValue>.Failure(Empty(column));
    }

    private static Result<DataValue> Extremum(DataColumn column, bool maximum)
    {
        ArgumentNullException.ThrowIfNull(column);

        DataValue? best = null;

        for (var index = 0; index < column.Count; index++)
        {
            var value = column.Get(index);

            if (value.IsNull)
            {
                continue;
            }

            if (best is not { } current)
            {
                best = value;
                continue;
            }

            var comparison = DataValueComparer.Compare(value, current);

            if (maximum ? comparison > 0 : comparison < 0)
            {
                best = value;
            }
        }

        return best is { } result
            ? Result<DataValue>.Success(result)
            : Result<DataValue>.Failure(Empty(column));
    }

    private static CoreError Empty(DataColumn column) =>
        new(CoreErrorCodes.DataEmpty, $"The column '{column.Name}' has no non-null value.");

    private static CoreError NotNumeric(DataColumn column) =>
        new(CoreErrorCodes.DataNotNumeric, $"The column '{column.Name}' is {column.Kind}, not numeric.");

    private static CoreError Overflow(DataColumn column) =>
        new(CoreErrorCodes.DataOverflow, $"The aggregate of '{column.Name}' overflowed.");
}
