// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Core.UI.Chrome;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class HeaderBarInputTests
{
    [Fact]
    public async Task A_left_press_starts_a_window_move()
    {
        var state = await Headless.Dispatch(() =>
        {
            var (window, _) = ShowHeaderBar();
            var point = new Point(10, 10);

            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            var current = window.WindowState;
            window.Close();

            return current;
        });

        Assert.Equal(WindowState.Normal, state);
    }

    [Fact]
    public async Task A_double_click_toggles_maximise()
    {
        var state = await Headless.Dispatch(() =>
        {
            var (window, _) = ShowHeaderBar();
            var point = new Point(10, 10);

            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            var current = window.WindowState;
            window.Close();

            return current;
        });

        Assert.Equal(WindowState.Maximized, state);
    }

    [Fact]
    public async Task A_right_press_does_nothing()
    {
        var state = await Headless.Dispatch(() =>
        {
            var (window, _) = ShowHeaderBar();
            var point = new Point(10, 10);

            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            var current = window.WindowState;
            window.Close();

            return current;
        });

        Assert.Equal(WindowState.Normal, state);
    }

    [Fact]
    public async Task A_press_on_a_header_button_is_not_a_window_drag()
    {
        var (clicked, state) = await Headless.Dispatch(() =>
        {
            var bar = new HeaderBar { Title = "Files" };
            var invoked = false;
            var button = new Button { Content = "Back" };
            button.Click += (_, _) => invoked = true;
            bar.StartContent = button;

            var window = new Window { Width = 400, Height = 300, Content = bar };
            window.Show();
            window.UpdateLayout();

            var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
            Assert.NotNull(centre);
            window.MouseDown(centre.Value, MouseButton.Left);
            window.MouseUp(centre.Value, MouseButton.Left);

            var current = window.WindowState;
            window.Close();

            return (invoked, current);
        });

        Assert.True(clicked);
        Assert.Equal(WindowState.Normal, state);
    }

    [Fact]
    public void A_focusable_child_is_interactive()
    {
        var bar = new HeaderBar { Title = "Files" };
        var input = new TextBox();

        var pointer = new Pointer(1, PointerType.Mouse, isPrimary: true);
        var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        var pressed = new PointerPressedEventArgs(input, pointer, bar, new Point(1, 1), 0, properties, KeyModifiers.None);

        Assert.True(bar.IsPointerOnInteractiveChild(pressed));
    }

    [Fact]
    public void A_press_sourced_from_a_button_is_not_a_window_drag()
    {
        var bar = new HeaderBar { Title = "Files" };
        var button = new Button { Content = "Back" };
        bar.StartContent = button;

        var pointer = new Pointer(1, PointerType.Mouse, isPrimary: true);
        var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        var pressed = new PointerPressedEventArgs(button, pointer, bar, new Point(1, 1), 0, properties, KeyModifiers.None);
        var plain = new PointerPressedEventArgs(new TextBlock(), pointer, bar, new Point(1, 1), 0, properties, KeyModifiers.None);

        Assert.True(bar.IsPointerOnInteractiveChild(pressed));
        Assert.False(bar.IsPointerOnInteractiveChild(plain));

        // Exercises OnPointerPressed's early return through the routed event.
        button.RaiseEvent(pressed);
    }

    private static (Window Window, HeaderBar Bar) ShowHeaderBar()
    {
        var bar = new HeaderBar { Title = "Files" };
        var window = new Window
        {
            Width = 400,
            Height = 300,
            Content = bar,
        };

        window.Show();

        return (window, bar);
    }
}
