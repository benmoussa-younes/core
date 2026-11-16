// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class DataNodeTests
{
    [Fact]
    public void A_scalar_node_holds_one_value()
    {
        var node = DataNode.Scalar(DataValue.From(1L));

        Assert.Equal(DataNodeKind.Scalar, node.Kind);
        Assert.Equal(DataValue.From(1L), node.Value);
        Assert.Equal("1", node.ToString());
        Assert.Throws<InvalidOperationException>(() => node.Items);
        Assert.Throws<InvalidOperationException>(() => node.Properties);
    }

    [Fact]
    public void A_list_node_holds_ordered_items()
    {
        var node = DataNode.List([DataNode.Scalar(DataValue.From(1L)), DataNode.Scalar(DataValue.From(2L))]);

        Assert.Equal(DataNodeKind.List, node.Kind);
        Assert.Equal(2, node.Items.Count);
        Assert.Equal("List(2)", node.ToString());
        Assert.Throws<InvalidOperationException>(() => node.Value);
        Assert.Throws<InvalidOperationException>(() => node.Properties);
    }

    [Fact]
    public void A_map_node_holds_ordered_properties()
    {
        var node = DataNode.Map([new DataProperty("a", DataNode.Scalar(DataValue.From(1L)))]);

        Assert.Equal(DataNodeKind.Map, node.Kind);
        Assert.Single(node.Properties);
        Assert.Equal("Map(1)", node.ToString());
        Assert.True(node.TryGetProperty("a", out var value));
        Assert.Equal(DataValue.From(1L), value!.Value);
        Assert.False(node.TryGetProperty("missing", out var missing));
        Assert.Null(missing);
        Assert.Throws<InvalidOperationException>(() => node.Value);
        Assert.Throws<InvalidOperationException>(() => node.Items);
    }

    [Fact]
    public void A_scalar_node_has_no_property()
    {
        var node = DataNode.Scalar(DataValue.Null);

        Assert.False(node.TryGetProperty("a", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void It_refuses_null_inputs()
    {
        Assert.Throws<ArgumentNullException>(() => DataNode.List(null!));
        Assert.Throws<ArgumentNullException>(() => DataNode.Map(null!));
        Assert.Throws<ArgumentNullException>(() => DataNode.Scalar(DataValue.Null).TryGetProperty(null!, out _));
    }
}
