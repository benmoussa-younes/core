// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Core.UI.Chrome;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class ResizeGripsTests
{
    [Fact]
    public async Task It_covers_every_edge_and_corner()
    {
        var count = await Headless.Dispatch(() => new ResizeGrips().Children.Count);

        Assert.Equal(8, count);
    }

    [Fact]
    public async Task A_press_on_a_grip_starts_a_resize_and_is_handled()
    {
        var (cursor, handled, rightHandled) = await Headless.Dispatch(() =>
        {
            var grips = new ResizeGrips();
            var window = new Window { Width = 200, Height = 200, Content = grips };
            window.Show();
            window.UpdateLayout();

            var grip = (Border)grips.Children[0];
            var centre = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window);
            Assert.NotNull(centre);

            var pointer = new Pointer(1, PointerType.Mouse, isPrimary: true);
            var left = new PointerPressedEventArgs(
                grip,
                pointer,
                window,
                centre.Value,
                0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None);
            var right = new PointerPressedEventArgs(
                grip,
                pointer,
                window,
                centre.Value,
                0,
                new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed),
                KeyModifiers.None);

            grip.RaiseEvent(left);
            grip.RaiseEvent(right);

            var result = (grip.Cursor, left.Handled, right.Handled);
            window.Close();

            return result;
        });

        Assert.NotNull(cursor);
        Assert.True(handled);
        Assert.False(rightHandled);
    }
}
