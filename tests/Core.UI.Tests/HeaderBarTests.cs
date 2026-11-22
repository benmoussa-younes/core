// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Core.UI.Chrome;
using Core.UI.Adwaita;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class HeaderBarTests
{
    [Fact]
    public void It_starts_empty_with_the_gnome_metrics()
    {
        var bar = new HeaderBar();

        Assert.Equal(GnomeMetrics.HeaderBarHeight, bar.MinHeight);
        Assert.Equal(new Thickness(6, 0, 6, 0), bar.Padding);
        Assert.Null(bar.Title);
        Assert.Null(bar.StartContent);
        Assert.Null(bar.EndContent);
    }

    [Fact]
    public void The_title_is_rendered_in_the_centre()
    {
        var bar = new HeaderBar { Title = "Files" };

        Assert.Equal("Files", TitleBlock(bar).Text);
    }

    [Fact]
    public void The_subtitle_is_shown_only_when_set()
    {
        var bar = new HeaderBar { Title = "Files" };

        Assert.False(SubtitleBlock(bar).IsVisible);

        bar.Subtitle = "3 items";

        Assert.Equal("3 items", SubtitleBlock(bar).Text);
        Assert.True(SubtitleBlock(bar).IsVisible);
    }

    [Fact]
    public void The_dirty_marker_is_shown_only_when_set()
    {
        var bar = new HeaderBar { Title = "Files" };

        Assert.False(bar.IsDirty);
        Assert.False(DirtyDot(bar).IsVisible);

        bar.IsDirty = true;

        Assert.True(DirtyDot(bar).IsVisible);

        bar.IsDirty = false;

        Assert.False(DirtyDot(bar).IsVisible);
    }

    [Fact]
    public void The_dirty_marker_announces_the_state_on_the_title()
    {
        var bar = new HeaderBar { Title = "Files" };

        bar.IsDirty = true;

        Assert.Equal("Files, unsaved changes", AutomationProperties.GetName(TitleBlock(bar)));

        bar.Title = "Renamed";

        Assert.Equal("Renamed, unsaved changes", AutomationProperties.GetName(TitleBlock(bar)));

        bar.IsDirty = false;

        Assert.Null(AutomationProperties.GetName(TitleBlock(bar)));
    }

    [Fact]
    public async Task The_dirty_dot_follows_the_primary_brush_resource()
    {
        await Headless.Dispatch(() =>
        {
            var bar = new HeaderBar { Title = "Files" };
            var window = new Window { Width = 400, Height = 100, Content = bar };

            window.Show();
            window.UpdateLayout();

            var resources = Application.Current!.Resources;
            var previous = resources["PrimaryBrush"];

            try
            {
                resources["PrimaryBrush"] = new SolidColorBrush(Colors.Red);
                window.UpdateLayout();

                var background = Assert.IsAssignableFrom<ISolidColorBrush>(DirtyDot(bar).Background);
                Assert.Equal(Colors.Red, background.Color);
            }
            finally
            {
                resources["PrimaryBrush"] = previous;
            }

            window.Close();
        });
    }

    [Fact]
    public async Task Toggling_the_dirty_dot_does_not_move_the_title()
    {
        await Headless.Dispatch(() =>
        {
            var bar = new HeaderBar { Title = "Files" };
            var window = new Window { Width = 400, Height = 100, Content = bar };

            window.Show();
            window.UpdateLayout();

            var before = TitleBlock(bar).TranslatePoint(default, bar);
            Assert.NotNull(before);

            bar.IsDirty = true;
            window.UpdateLayout();
            var dirty = TitleBlock(bar).TranslatePoint(default, bar);

            bar.IsDirty = false;
            window.UpdateLayout();
            var clean = TitleBlock(bar).TranslatePoint(default, bar);

            Assert.Equal(before.Value.X, dirty!.Value.X);
            Assert.Equal(before.Value.X, clean!.Value.X);

            window.Close();
        });
    }

    [Fact]
    public void Center_content_replaces_the_title()
    {
        var bar = new HeaderBar { Title = "Files", Subtitle = "3 items" };

        bar.CenterContent = new Button();

        Assert.False(IdentityPanel(bar).IsVisible);
    }

    [Fact]
    public void Start_and_end_content_are_hosted()
    {
        var start = new Button();
        var end = new Button();
        var bar = new HeaderBar { StartContent = start, EndContent = end };

        Assert.Same(start, Assert.Single(StartPanel(bar).Children));
        Assert.Same(end, Assert.Single(EndPanel(bar).Children));
    }

    [Fact]
    public void Content_can_be_cleared()
    {
        var bar = new HeaderBar { StartContent = new Button(), EndContent = new Button() };

        bar.StartContent = null;
        bar.EndContent = null;

        Assert.Empty(StartPanel(bar).Children);
        Assert.Empty(EndPanel(bar).Children);
    }

    [Fact]
    public void An_unrelated_property_change_leaves_the_slots_alone()
    {
        var bar = new HeaderBar { Title = "Files" };

        bar.Background = new SolidColorBrush(Colors.Red);

        Assert.Equal("Files", TitleBlock(bar).Text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_press_on_an_interactive_child_does_not_start_a_window_drag(bool interactive)
    {
        Control target = interactive ? new Button() : new TextBlock();

        Assert.Equal(interactive, HeaderBar.IsInteractive(target));
    }

    [Fact]
    public void An_empty_press_source_is_not_interactive()
    {
        Assert.False(HeaderBar.IsInteractive(null));
    }

    [Fact]
    public void The_icon_is_shown_only_when_set()
    {
        var bar = new HeaderBar { Title = "Files" };

        Assert.False(IconBlock(bar).IsVisible);

        var image = new DrawingImage { Drawing = new GeometryDrawing() };
        bar.Icon = image;

        Assert.True(IconBlock(bar).IsVisible);
        Assert.Same(image, IconBlock(bar).Source);
    }

    private static Grid Layout(HeaderBar bar) => Assert.IsType<Grid>(bar.Child);

    private static StackPanel StartPanel(HeaderBar bar) =>
        Assert.IsType<StackPanel>(Layout(bar).Children[0]);

    private static StackPanel CenterPanel(HeaderBar bar) =>
        Assert.IsType<StackPanel>(Layout(bar).Children[1]);

    private static StackPanel IdentityPanel(HeaderBar bar) =>
        Assert.IsType<StackPanel>(CenterPanel(bar).Children[0]);

    private static Image IconBlock(HeaderBar bar) =>
        Assert.IsType<Image>(IdentityPanel(bar).Children[0]);

    private static Border DirtyDot(HeaderBar bar) =>
        Assert.IsType<Border>(Assert.IsType<Panel>(IdentityPanel(bar).Children[1]).Children[0]);

    private static StackPanel TitlePanel(HeaderBar bar) =>
        Assert.IsType<StackPanel>(IdentityPanel(bar).Children[2]);

    private static TextBlock TitleBlock(HeaderBar bar) =>
        Assert.IsType<TextBlock>(TitlePanel(bar).Children[0]);

    private static TextBlock SubtitleBlock(HeaderBar bar) =>
        Assert.IsType<TextBlock>(TitlePanel(bar).Children[1]);

    private static StackPanel EndPanel(HeaderBar bar) =>
        Assert.IsType<StackPanel>(Layout(bar).Children[2]);
}
