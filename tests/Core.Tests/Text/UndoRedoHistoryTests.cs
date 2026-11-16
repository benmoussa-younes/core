// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Text;

namespace Core.Tests;

public sealed class UndoRedoHistoryTests
{
    [Fact]
    public void It_undoes_and_redoes_in_order()
    {
        var history = new UndoRedoHistory<string>();
        history.Record("a");
        history.Record("b");

        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal(2, history.UndoCount);

        Assert.Equal("b", history.Undo("c"));
        Assert.Equal("a", history.Undo("b"));
        Assert.Null(history.Undo("a"));

        Assert.True(history.CanRedo);
        Assert.Equal("b", history.Redo("a"));
        Assert.Equal("c", history.Redo("b"));
        Assert.Null(history.Redo("c"));
    }

    [Fact]
    public void It_drops_the_oldest_step_at_capacity()
    {
        var history = new UndoRedoHistory<string>(capacity: 2);
        history.Record("1");
        history.Record("2");
        history.Record("3");

        Assert.Equal(2, history.UndoCount);
        Assert.Equal("3", history.Undo("4"));
        Assert.Equal("2", history.Undo("3"));
        Assert.Null(history.Undo("2"));
    }

    [Fact]
    public void A_coalesced_change_keeps_the_earlier_snapshot()
    {
        var history = new UndoRedoHistory<string>();
        history.Record("a");
        history.Record("b", coalesce: true);

        Assert.Equal(1, history.UndoCount);
        Assert.Equal("a", history.Undo("c"));
    }

    [Fact]
    public void A_new_change_clears_redo_and_clear_forgets_everything()
    {
        var history = new UndoRedoHistory<string>();
        history.Record("a");
        _ = history.Undo("b");
        Assert.True(history.CanRedo);

        history.Record("c");
        Assert.False(history.CanRedo);

        history.Clear();
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal(0, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
    }

    [Fact]
    public void It_validates_its_arguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UndoRedoHistory<string>(0));

        var history = new UndoRedoHistory<string>();

        Assert.Throws<ArgumentNullException>(() => history.Record(null!));
        Assert.Throws<ArgumentNullException>(() => history.Undo(null!));
        Assert.Throws<ArgumentNullException>(() => history.Redo(null!));
    }
}
