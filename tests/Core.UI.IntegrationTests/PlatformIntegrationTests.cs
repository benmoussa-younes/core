// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Core.Mvp.Models;
using Core.UI.Chrome;
using Core.UI.Tray;

namespace Core.UI.IntegrationTests;

// The platform paths the headless harness cannot reach: a real screen list (it
// reports none), the compositor's window geometry, the real X11 clipboard and the
// client-side tray registration. Measured at 7/7 over three consecutive runs and
// under dbus-run-session, in about 570 ms.
[Collection("desktop")]
public sealed class PlatformIntegrationTests
{
    [GuiFact]
    public async Task The_window_reports_a_screen_and_lays_out_inside_it()
    {
        var (screens, width, height) = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = CreateWindow();
            window.Show();
            var all = window.Screens.All;
            var size = window.ClientSize;
            window.Close();
            return (all.Count, size.Width, size.Height);
        });

        Assert.True(screens > 0, "the compositor reported no screen");
        Assert.True(width > 0 && height > 0, $"client size={width}x{height}");
    }

    // WHY: headless reports no screen, so ApplyPlacement's clamping against a
    // working area is only reachable here.
    [GuiFact]
    public async Task An_off_screen_placement_is_brought_back_onto_a_screen()
    {
        var position = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = CreateWindow();
            window.Show();

            window.ApplyPlacement(new WindowPlacement(100_000, 100_000, 800, 600));

            var point = window.Position;
            window.Close();
            return point;
        });

        Assert.True(position.X < 100_000, $"position={position}");
        Assert.True(position.Y < 100_000, $"position={position}");
    }

    // An in-process round trip through the real X11 clipboard, not a cross-process
    // one: it proves the Adwaita clipboard port on the real backend, nothing about
    // another application's ownership.
    [GuiFact]
    public async Task The_clipboard_round_trips_through_the_real_backend()
    {
        var (written, read) = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = CreateWindow();
            window.Show();

            var clipboard = window.Clipboard!;
            await clipboard.SetTextAsync("integration probe");
            var value = await clipboard.TryGetTextAsync();
            window.Close();
            return (true, value);
        });

        Assert.True(written);
        Assert.Equal("integration probe", read);
    }

    // Proves the client-side registration only: a StatusNotifierItem reaching a
    // watcher needs a session bus and a fake watcher, which this suite does not set
    // up, so the DBus delivery stays unverified here.
    [GuiFact]
    public async Task The_tray_icon_is_registered_on_the_application()
    {
        var registered = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var tray = new TrayService(icon: null, activated: _ => { });
            tray.Show("Core", Core.Mvp.Presentation.CommandSet.Empty);

            var icons = Avalonia.Controls.TrayIcon.GetIcons(Avalonia.Application.Current!);
            var contains = icons?.Count > 0;
            tray.Dispose();
            return contains;
        });

        Assert.True(registered, "the tray icon was not registered");
    }

    private static CoreWindow CreateWindow() => new(new TrayService(icon: null, activated: _ => { }));
}
