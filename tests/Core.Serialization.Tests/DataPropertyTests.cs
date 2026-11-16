// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class DataPropertyTests
{
    [Fact]
    public void It_carries_a_name_and_a_node()
    {
        var node = DataNode.Scalar(DataValue.From(1L));
        var property = new DataProperty("a", node);

        Assert.Equal("a", property.Name);
        Assert.Same(node, property.Value);
        Assert.Equal("a: 1", property.ToString());
    }

    [Fact]
    public void It_refuses_null_inputs()
    {
        Assert.Throws<ArgumentNullException>(() => new DataProperty(null!, DataNode.Scalar(DataValue.Null)));
        Assert.Throws<ArgumentNullException>(() => new DataProperty("a", null!));
    }

    [Fact]
    public void Equality_compares_the_name_and_the_node_identity()
    {
        var node = DataNode.Scalar(DataValue.From(1L));
        var property = new DataProperty("a", node);
        var same = new DataProperty("a", node);
        var other = new DataProperty("a", DataNode.Scalar(DataValue.From(1L)));

        Assert.True(property.Equals(same));
        Assert.True(property == same);
        Assert.False(property != same);
        Assert.Equal(property.GetHashCode(), same.GetHashCode());

        Assert.False(property.Equals(other));
        Assert.False(property == other);
        Assert.True(property != other);
        Assert.False(property.Equals(new DataProperty("b", node)));
        Assert.False(property.Equals("not a property"));
    }
}
