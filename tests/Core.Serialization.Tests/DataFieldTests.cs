// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class DataFieldTests
{
    [Fact]
    public void It_carries_a_name_and_a_kind()
    {
        var field = new DataField("a", DataKind.Integer);

        Assert.Equal("a", field.Name);
        Assert.Equal(DataKind.Integer, field.Kind);
        Assert.Equal("a:Integer", field.ToString());
    }

    [Fact]
    public void It_refuses_a_null_name_and_the_null_kind()
    {
        Assert.Throws<ArgumentNullException>(() => new DataField(null!, DataKind.Text));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataField("a", DataKind.Null));
    }

    [Fact]
    public void Equality_compares_the_name_and_the_kind()
    {
        var field = new DataField("a", DataKind.Integer);
        var same = new DataField("a", DataKind.Integer);

        Assert.True(field.Equals(same));
        Assert.True(field == same);
        Assert.False(field != same);
        Assert.Equal(field.GetHashCode(), same.GetHashCode());

        Assert.False(field.Equals(new DataField("b", DataKind.Integer)));
        Assert.False(field.Equals(new DataField("a", DataKind.Text)));
        Assert.False(field.Equals("not a field"));
        Assert.True(field != new DataField("b", DataKind.Integer));
    }
}
