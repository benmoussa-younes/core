// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Core.UI.Adwaita;
using Core.UI.Controls;
using Core.Mvp.Services;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class ControlsTests
{
    [Fact]
    public void A_card_carries_the_shared_style()
    {
        var card = new Card();

        Assert.Contains("card", card.Classes);
    }

    [Fact]
    public void A_card_can_wrap_one_control()
    {
        var content = new TextBlock { Text = "hello" };
        var card = new Card(content);

        Assert.Same(content, card.Child);
    }

    [Fact]
    public void A_card_can_stack_a_heading_and_rows()
    {
        var first = new TextBlock { Text = "one" };
        var second = new TextBlock { Text = "two" };
        var card = new Card("Settings", first, second);

        var stack = Assert.IsType<StackPanel>(card.Child);
        Assert.Equal(3, stack.Children.Count);
        Assert.Same(first, stack.Children[1]);
        Assert.Same(second, stack.Children[2]);
    }

    [Fact]
    public void A_card_rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => new Card((Control)null!));
        Assert.Throws<ArgumentNullException>(() => new Card((string)null!));
        Assert.Throws<ArgumentNullException>(() => new Card("heading", null!));
    }

    [Fact]
    public void A_field_row_labels_its_field()
    {
        var field = new TextBox();
        var row = new FieldRow("Name", field);

        Assert.Equal(2, row.ColumnDefinitions.Count);
        Assert.Equal(FieldRow.DefaultLabelWidth, row.ColumnDefinitions[0].Width.Value);
        Assert.Equal("Name", Assert.IsType<TextBlock>(row.Children[0]).Text);
        Assert.Same(field, row.Children[1]);
        Assert.Equal(1, Grid.GetColumn(field));
    }

    [Fact]
    public void A_field_row_takes_an_explicit_width_and_validates()
    {
        var row = new FieldRow("Name", new TextBox(), 200);

        Assert.Equal(200, row.ColumnDefinitions[0].Width.Value);

        Assert.Throws<ArgumentNullException>(() => new FieldRow(null!, new TextBox()));
        Assert.Throws<ArgumentNullException>(() => new FieldRow("Name", null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FieldRow("Name", new TextBox(), 0));
    }

    [Fact]
    public void An_empty_state_starts_blank_and_configures()
    {
        var state = new EmptyState();

        Assert.Equal(string.Empty, state.Heading);
        Assert.False(state.ActionButton.IsVisible);

        state.Configure("Nothing here", "Add a tunnel", "New tunnel");

        Assert.Equal("Nothing here", state.Heading);
        Assert.Equal("Add a tunnel", state.Detail);
        Assert.Equal("New tunnel", state.ActionLabel);
        Assert.True(state.ActionButton.IsVisible);

        state.Configure("Nothing here", "Add a tunnel");
        Assert.Equal(string.Empty, state.ActionLabel);
        Assert.False(state.ActionButton.IsVisible);
    }

    [Fact]
    public void An_empty_state_rejects_null_lines()
    {
        var state = new EmptyState();

        Assert.Throws<ArgumentNullException>(() => state.Configure(null!, "detail"));
        Assert.Throws<ArgumentNullException>(() => state.Configure("heading", null!));
        Assert.Throws<ArgumentNullException>(() => state.Configure("heading", "detail", null!));
    }

    [Fact]
    public async Task The_empty_state_action_raises_when_clicked()
    {
        var raised = await Headless.Dispatch(() =>
        {
            var state = new EmptyState();
            state.Configure("Nothing here", "Add a tunnel", "New tunnel");
            var invoked = false;
            state.ActionRequested += () => invoked = true;

            var window = new Window { Width = 400, Height = 300, Content = state };
            window.Show();
            window.UpdateLayout();

            var button = state.ActionButton;
            var centre = button.TranslatePoint(
                new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
            Assert.NotNull(centre);
            window.MouseDown(centre.Value, MouseButton.Left);
            window.MouseUp(centre.Value, MouseButton.Left);

            window.Close();

            return invoked;
        });

        Assert.True(raised);
    }

    [Fact]
    public void A_status_chip_starts_cleared()
    {
        var chip = new StatusChip();

        Assert.Equal(string.Empty, chip.State);
        Assert.False(chip.IsVisible);

        chip.Clear();
        Assert.Equal(string.Empty, chip.State);
        Assert.False(chip.IsVisible);
    }

    [Theory]
    [InlineData(MessageSeverity.Success, "SuccessBrush")]
    [InlineData(MessageSeverity.Warning, "WarningBrush")]
    [InlineData(MessageSeverity.Error, "ErrorBrush")]
    [InlineData(MessageSeverity.Information, "MutedForegroundBrush")]
    public async Task A_status_chip_tones_its_dot(MessageSeverity severity, string token)
    {
        var (fill, expected) = await Headless.Dispatch(() =>
        {
            var chip = new StatusChip();
            chip.Show("Up", severity);

            var window = new Window { Width = 200, Height = 100, Content = chip };
            window.Show();
            window.UpdateLayout();
            var current = chip.Dot.Fill;
            window.Close();

            return (current, ThemeBrushes.Resolve(token));
        });

        Assert.Same(expected, fill);
    }

    [Fact]
    public void A_status_chip_hides_on_an_empty_word_and_rejects_null()
    {
        var chip = new StatusChip();
        chip.Show("Up", MessageSeverity.Success);

        chip.Show(string.Empty, MessageSeverity.Information);
        Assert.False(chip.IsVisible);

        Assert.Throws<ArgumentNullException>(() => chip.Show(null!, MessageSeverity.Success));
    }
}
