// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Core.UI.Chrome;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class TabStripTests
{
    [Fact]
    public void The_tab_label_and_close_button_are_flush()
    {
        var strip = new TabStrip();
        strip.Open("one", "One");

        var container = Assert.IsType<Border>(strip.Children[0]);
        var content = Assert.IsType<StackPanel>(container.Child);

        // No gap: the close button sits inside the tab box.
        Assert.Equal(0, content.Spacing);
        Assert.Equal(2, content.Children.Count);
    }

    [Fact]
    public void Opening_a_tab_selects_it_and_shows_the_strip()
    {
        var strip = new TabStrip();
        var selected = new List<string>();

        strip.TabSelected += selected.Add;
        strip.Open("one", "One");

        Assert.Equal(1, strip.TabCount);
        Assert.Equal("one", strip.SelectedId);
        Assert.True(strip.IsVisible);
        Assert.Equal(["one"], selected);
    }

    [Fact]
    public void Opening_an_existing_tab_updates_and_reselects_it()
    {
        var strip = new TabStrip();
        strip.Open("one", "One");
        strip.Open("two", "Two");

        var selected = new List<string>();
        strip.TabSelected += selected.Add;
        strip.Open("one", "One revised");

        Assert.Equal(2, strip.TabCount);
        Assert.Equal("one", strip.SelectedId);
        Assert.Equal(["one"], selected);
    }

    [Fact]
    public void Selecting_a_tab_raises_it()
    {
        var strip = new TabStrip();
        strip.Open("one", "One");
        strip.Open("two", "Two");

        var selected = new List<string>();
        strip.TabSelected += selected.Add;

        Click(strip.SelectButtons[0]);

        Assert.Equal("one", strip.SelectedId);
        Assert.Equal(["one"], selected);
    }

    [Fact]
    public void Closing_a_non_selected_tab_keeps_the_selection()
    {
        var strip = new TabStrip();
        strip.Open("one", "One");
        strip.Open("two", "Two");

        var selected = new List<string>();
        strip.TabSelected += selected.Add;
        strip.Close("one");

        Assert.Equal(1, strip.TabCount);
        Assert.Equal("two", strip.SelectedId);
        Assert.Empty(selected);
    }

    [Fact]
    public void Closing_the_selected_tab_selects_the_next_one()
    {
        var strip = new TabStrip();
        strip.Open("one", "One");
        strip.Open("two", "Two");

        var selected = new List<string>();
        strip.TabSelected += selected.Add;
        strip.Close("two");

        Assert.Equal("one", strip.SelectedId);
        Assert.Equal(["one"], selected);
    }

    [Fact]
    public void Closing_the_last_tab_hides_the_strip()
    {
        var strip = new TabStrip();
        strip.Open("one", "One");

        var selected = new List<string>();
        strip.TabSelected += selected.Add;
        strip.Close("one");

        Assert.Equal(0, strip.TabCount);
        Assert.Null(strip.SelectedId);
        Assert.False(strip.IsVisible);
        Assert.Empty(selected);
    }

    [Fact]
    public void Closing_an_unknown_tab_is_a_no_op()
    {
        var strip = new TabStrip();

        strip.Close("absent");

        Assert.Equal(0, strip.TabCount);
    }

    [Fact]
    public void The_close_button_raises_a_request()
    {
        var strip = new TabStrip();
        strip.Open("one", "One");

        var requests = new List<string>();
        strip.TabCloseRequested += requests.Add;

        Click(strip.CloseButtons[0]);

        Assert.Equal(["one"], requests);
        Assert.Equal(1, strip.TabCount);
    }

    [Fact]
    public void Invalid_identities_are_rejected()
    {
        var strip = new TabStrip();

        Assert.Throws<ArgumentException>(() => strip.Open(string.Empty, "One"));
        Assert.Throws<ArgumentException>(() => strip.Open(" ", "One"));
        Assert.Throws<ArgumentNullException>(() => strip.Open("one", null!));
        Assert.Throws<ArgumentNullException>(() => strip.Close(null!));
    }

    [Fact]
    public void Tabs_and_close_buttons_have_accessible_names()
    {
        var strip = new TabStrip();
        strip.Open("one", "One");

        Assert.Equal("One", AutomationProperties.GetName(strip.SelectButtons[0]));
        Assert.Equal("Close One", AutomationProperties.GetName(strip.CloseButtons[0]));

        strip.Open("one", "Revised");

        Assert.Equal("Revised", AutomationProperties.GetName(strip.SelectButtons[0]));
        Assert.Equal("Close Revised", AutomationProperties.GetName(strip.CloseButtons[0]));
    }

    [Fact]
    public void The_dirty_marker_is_opt_in()
    {
        var strip = new TabStrip();
        strip.Open("one", "One");

        Assert.False(strip.IsDirty("one"));

        strip.SetDirty("one", true);
        Assert.True(strip.IsDirty("one"));

        strip.SetDirty("one", false);
        Assert.False(strip.IsDirty("one"));

        strip.SetDirty("missing", true);
        Assert.Throws<ArgumentNullException>(() => strip.SetDirty(null!, true));
    }

    [Fact]
    public void Tab_ids_are_in_strip_order()
    {
        var strip = new TabStrip();
        strip.Open("one", "One");
        strip.Open("two", "Two");

        Assert.Equal(["one", "two"], strip.TabIds);
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
