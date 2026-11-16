// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class DataValueComparerTests
{
    [Fact]
    public void It_orders_nulls_last_and_keeps_a_cross_kind_pair_total()
    {
        Assert.Equal(0, DataValueComparer.Compare(DataValue.Null, DataValue.Null));
        Assert.Equal(1, DataValueComparer.Compare(DataValue.Null, DataValue.From(1L)));
        Assert.Equal(-1, DataValueComparer.Compare(DataValue.From(1L), DataValue.Null));
        Assert.Equal(0, DataValueComparer.Compare(DataValue.From(1L), DataValue.From("1")));
    }

    [Fact]
    public void It_compares_every_scalar_kind()
    {
        Assert.Equal(-1, DataValueComparer.Compare(DataValue.From(false), DataValue.From(true)));
        Assert.Equal(-1, DataValueComparer.Compare(DataValue.From(1L), DataValue.From(2L)));
        Assert.Equal(-1, DataValueComparer.Compare(DataValue.From(1.0), DataValue.From(2.0)));
        Assert.Equal(-1, DataValueComparer.Compare(DataValue.From(1m), DataValue.From(2m)));
        Assert.Equal(-1, DataValueComparer.Compare(
            DataValue.From(new DateOnly(2026, 1, 1)),
            DataValue.From(new DateOnly(2026, 1, 2))));
        Assert.Equal(-1, DataValueComparer.Compare(
            DataValue.From(new TimeOnly(1, 0)),
            DataValue.From(new TimeOnly(2, 0))));
        Assert.Equal(-1, DataValueComparer.Compare(
            DataValue.From(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            DataValue.From(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc))));
        Assert.Equal(-1, DataValueComparer.Compare(DataValue.From("a"), DataValue.From("b")));
        Assert.Equal(-1, DataValueComparer.Compare(
            DataValue.FromNode(DataNode.Scalar(DataValue.From("a"))),
            DataValue.FromNode(DataNode.Scalar(DataValue.From("b")))));
    }
}
