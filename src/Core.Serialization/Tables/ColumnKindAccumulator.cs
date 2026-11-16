// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Values;

namespace Core.Serialization.Tables;

/// <summary>
/// Infers a column's <see cref="DataKind"/> from the kinds of its values, shared
/// by the JSON and CSV readers: a nested node wins, any text makes the column
/// text, a boolean mixed with a number or a temporal value makes it text, an
/// integer widens into a decimal, a number mixed with a decimal is a decimal, and
/// a date mixed with a timestamp is a timestamp.
/// </summary>
internal struct ColumnKindAccumulator
{
    private bool _node;
    private bool _boolean;
    private bool _integer;
    private bool _number;
    private bool _decimal;
    private bool _date;
    private bool _time;
    private bool _timestamp;
    private bool _text;

    /// <summary>Adds one value's kind.</summary>
    /// <param name="kind">The value kind.</param>
    internal void Add(DataKind kind)
    {
        switch (kind)
        {
            case DataKind.Null:
                break;
            case DataKind.Node:
                _node = true;
                break;
            case DataKind.Boolean:
                _boolean = true;
                break;
            case DataKind.Integer:
                _integer = true;
                break;
            case DataKind.Number:
                _number = true;
                break;
            case DataKind.Decimal:
                _decimal = true;
                break;
            case DataKind.Date:
                _date = true;
                break;
            case DataKind.Time:
                _time = true;
                break;
            case DataKind.Timestamp:
                _timestamp = true;
                break;
            default:
                _text = true;
                break;
        }
    }

    /// <summary>Resolves the column kind.</summary>
    /// <returns>The inferred kind.</returns>
    internal DataKind Resolve()
    {
        if (_node)
        {
            return DataKind.Node;
        }

        if (_text)
        {
            return DataKind.Text;
        }

        var numeric = _integer || _number || _decimal;
        var temporal = _date || _time || _timestamp;

        if (_boolean)
        {
            return numeric || temporal ? DataKind.Text : DataKind.Boolean;
        }

        // A column that mixes a number with a date (or a time) is not typed.
        if (numeric && temporal)
        {
            return DataKind.Text;
        }

        if (_timestamp)
        {
            return DataKind.Timestamp;
        }

        // A date and a time of day in the same column do not make an instant.
        if (_date && _time)
        {
            return DataKind.Text;
        }

        if (_date)
        {
            return DataKind.Date;
        }

        if (_time)
        {
            return DataKind.Time;
        }

        if (_decimal)
        {
            return DataKind.Decimal;
        }

        if (_number)
        {
            return DataKind.Number;
        }

        return _integer ? DataKind.Integer : DataKind.Text;
    }
}
