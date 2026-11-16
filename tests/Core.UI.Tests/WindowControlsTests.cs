// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Automation;
using Avalonia.Controls;
using Core.UI.Chrome;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class WindowControlsTests
{
    [Fact]
    public void The_caption_buttons_have_accessible_names()
    {
        var controls = new WindowControls();

        Assert.Equal("Minimize", AutomationProperties.GetName(controls.MinimizeButton));
        Assert.Equal("Maximize", AutomationProperties.GetName(controls.MaximizeButton));
        Assert.Equal("Close", AutomationProperties.GetName(controls.CloseButton));
    }

    [Fact]
    public async Task It_toggles_maximise_and_follows_the_glyph()
    {
        await Headless.Dispatch(() =>
        {
            var controls = new WindowControls();
            var window = new Window { Content = controls };

            window.Show();

            controls.ToggleMaximize();
            Assert.Equal(WindowState.Maximized, window.WindowState);
            var maximized = controls.MaximizeButton.Content;

            controls.ToggleMaximize();
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.NotEqual(maximized, controls.MaximizeButton.Content);

            window.Close();
        });
    }

    [Fact]
    public async Task It_minimises_the_window()
    {
        await Headless.Dispatch(() =>
        {
            var controls = new WindowControls();
            var window = new Window { Content = controls };

            window.Show();
            controls.Minimize();

            Assert.Equal(WindowState.Minimized, window.WindowState);

            window.Close();
        });
    }

    [Fact]
    public async Task Close_is_a_request_the_shell_can_handle()
    {
        await Headless.Dispatch(() =>
        {
            var controls = new WindowControls();
            var window = new Window { Content = controls };
            var raised = 0;

            window.Show();
            controls.CloseRequested += (_, _) => raised++;
            controls.RequestClose();

            Assert.Equal(1, raised);
            Assert.True(window.IsVisible);

            window.Close();
        });
    }

    [Fact]
    public void It_is_inert_when_not_attached()
    {
        var controls = new WindowControls();

        controls.Minimize();
        controls.ToggleMaximize();
        controls.RequestClose();

        Assert.IsType<Panel>(controls.MaximizeButton.Content);
    }

    [Fact]
    public void It_exposes_its_three_buttons()
    {
        var controls = new WindowControls();

        Assert.NotNull(controls.MinimizeButton);
        Assert.NotNull(controls.MaximizeButton);
        Assert.NotNull(controls.CloseButton);
    }

    [Fact]
    public async Task Close_closes_the_window_when_nothing_handles_it()
    {
        await Headless.Dispatch(() =>
        {
            var controls = new WindowControls();
            var window = new Window { Content = controls };

            window.Show();
            controls.RequestClose();

            Assert.False(window.IsVisible);
        });
    }
}
