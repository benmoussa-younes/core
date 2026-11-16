// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Core.Mvp.Presentation;
using Core.UI.Chrome;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class ActionBarTests
{
    [Fact]
    public void An_empty_command_set_renders_nothing()
    {
        var bar = new ActionBar { Commands = new CommandSet([]) };

        Assert.Empty(bar.Children);

        bar.Commands = null;

        Assert.Empty(bar.Children);
    }

    [Fact]
    public void It_renders_one_button_per_command()
    {
        var bar = new ActionBar
        {
            Commands = new CommandSet([new AppCommand("a", "A"), new AppCommand("b", "B")]),
        };

        Assert.Equal(2, bar.Children.OfType<Button>().Count());
    }

    [Fact]
    public void Every_button_has_an_accessible_name()
    {
        var bar = new ActionBar
        {
            Commands = new CommandSet(
            [
                new AppCommand("save", "Save", ToolTip: "Save the file", Shortcut: "Ctrl+S"),
                new AppCommand("icon", "Icon", IconKey: "IconSave"),
            ]),
        };

        var buttons = bar.Children.OfType<Button>().ToList();

        Assert.Equal("Save the file (Ctrl+S)", AutomationProperties.GetName(buttons[0]));
        Assert.Equal("Icon", AutomationProperties.GetName(buttons[1]));
    }

    [Fact]
    public void It_separates_the_groups()
    {
        var bar = new ActionBar
        {
            Commands = new CommandSet(
            [
                new AppCommand("a", "A", Group: "file"),
                new AppCommand("b", "B", Group: "edit"),
            ]),
        };

        Assert.Single(bar.Children.OfType<Border>());
    }

    [Fact]
    public void It_raises_the_activated_id()
    {
        var bar = new ActionBar { Commands = new CommandSet([new AppCommand("save", "Save")]) };
        string? invoked = null;

        bar.CommandInvoked += id => invoked = id;
        Click(bar);

        Assert.Equal("save", invoked);
    }

    [Fact]
    public void Clicking_without_a_subscriber_is_safe()
    {
        var bar = new ActionBar { Commands = new CommandSet([new AppCommand("save", "Save")]) };

        Click(bar);
    }

    [Fact]
    public void An_unchanged_command_set_does_not_rebuild_the_buttons()
    {
        var bar = new ActionBar { Commands = new CommandSet([new AppCommand("a", "A")]) };
        var button = bar.Children.OfType<Button>().Single();

        bar.Commands = new CommandSet([new AppCommand("a", "A")]);

        Assert.Same(button, bar.Children.OfType<Button>().Single());
    }

    [Fact]
    public void A_changed_command_set_rebuilds_the_buttons()
    {
        var bar = new ActionBar { Commands = new CommandSet([new AppCommand("a", "A")]) };
        var button = bar.Children.OfType<Button>().Single();

        bar.Commands = new CommandSet([new AppCommand("a", "A", IsChecked: true)]);

        var rebuilt = bar.Children.OfType<Button>().Single();
        Assert.NotSame(button, rebuilt);
        Assert.Contains("active", rebuilt.Classes);
    }

    [Fact]
    public void A_forced_rebuild_recreates_the_buttons()
    {
        var bar = new ActionBar { Commands = new CommandSet([new AppCommand("a", "A")]) };
        var button = bar.Children.OfType<Button>().Single();

        bar.Rebuild(force: true);

        Assert.NotSame(button, bar.Children.OfType<Button>().Single());
    }

    [Fact]
    public void It_reflects_the_enabled_and_checked_state()
    {
        var bar = new ActionBar
        {
            Commands = new CommandSet([new AppCommand("a", "A", IsEnabled: false, IsChecked: true)]),
        };

        var button = bar.Children.OfType<Button>().Single();

        Assert.False(button.IsEnabled);
        Assert.Contains("active", button.Classes);
    }

    private static void Click(ActionBar bar)
    {
        bar.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [Fact]
    public async Task It_renders_a_glyph_for_a_resolved_icon()
    {
        var (content, classes) = await Headless.Dispatch(() =>
        {
            var bar = new ActionBar
            {
                Commands = new CommandSet([new AppCommand("save", "Save", IconKey: "IconSave")]),
            };

            var button = bar.Children.OfType<Button>().Single();
            return (button.Content, button.Classes);
        });

        var path = Assert.IsType<ShapePath>(content);
        Assert.NotNull(path.Data);
        Assert.Contains("icon", path.Classes);
        Assert.DoesNotContain("text", classes);
    }

    [Fact]
    public async Task It_falls_back_to_text_for_an_unknown_icon()
    {
        var (content, classes) = await Headless.Dispatch(() =>
        {
            var bar = new ActionBar
            {
                Commands = new CommandSet([new AppCommand("save", "Save", IconKey: "IconNope")]),
            };

            var button = bar.Children.OfType<Button>().Single();
            return (button.Content, button.Classes);
        });

        Assert.Equal("Save", content);
        Assert.Contains("text", classes);
    }

    [Fact]
    public async Task It_shows_the_shortcut_in_the_tooltip()
    {
        var tip = await Headless.Dispatch(() =>
        {
            var bar = new ActionBar
            {
                Commands = new CommandSet([new AppCommand("save", "Save", Shortcut: "Ctrl+S")]),
            };

            var button = bar.Children.OfType<Button>().Single();
            return ToolTip.GetTip(button);
        });

        Assert.Equal("Save (Ctrl+S)", tip);
    }
}
