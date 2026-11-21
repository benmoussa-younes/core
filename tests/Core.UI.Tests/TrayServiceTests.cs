// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Core.Mvp.Presentation;
using Core.UI.Tray;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class TrayServiceTests
{
    [Fact]
    public void It_builds_a_menu_with_groups_and_state()
    {
        var activated = new List<string>();

        var menu = TrayService.BuildMenu(
            new CommandSet(
            [
                new AppCommand("open", "Open", Group: "main"),
                new AppCommand("quit", "Quit", IsEnabled: false, IsChecked: true, Group: "app"),
            ]),
            activated.Add);

        Assert.Equal(3, menu.Items.Count);
        Assert.IsType<NativeMenuItemSeparator>(menu.Items[1]);

        var open = Assert.IsType<NativeMenuItem>(menu.Items[0]);
        var quit = Assert.IsType<NativeMenuItem>(menu.Items[2]);

        Assert.Equal("Open", open.Header);
        Assert.True(quit.IsChecked);

        Assert.True(open.Command!.CanExecute(null));
        Assert.False(quit.Command!.CanExecute(null));

        EventHandler changed = (_, _) => { };
        open.Command.CanExecuteChanged += changed;
        open.Command.CanExecuteChanged -= changed;

        open.Command.Execute(null);

        Assert.Equal(["open"], activated);
    }

    [Fact]
    public async Task The_activation_handler_can_be_replaced()
    {
        await Headless.Dispatch(() =>
        {
            var first = new List<string>();
            var second = new List<string>();
            var service = new TrayService(null, first.Add);
            var menu = new CommandSet([new AppCommand("open", "Open")]);
            service.Show("app", menu);

            var icon = TrayIcon.GetIcons(Application.Current!)!.Single();
            Assert.IsType<NativeMenuItem>(icon.Menu!.Items[0]).Command!.Execute(null);
            Assert.Equal(["open"], first);

            service.Activated = second.Add;
            Assert.NotNull(service.Activated);
            service.Show("app", menu);
            Assert.IsType<NativeMenuItem>(icon.Menu!.Items[0]).Command!.Execute(null);
            Assert.Equal(["open"], second);

            Assert.Throws<ArgumentNullException>(() => service.Activated = null!);

            service.Dispose();
        });
    }

    [Fact]
    public void Null_inputs_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => TrayService.BuildMenu(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => TrayService.BuildMenu(new CommandSet([]), null!));
        Assert.Throws<ArgumentNullException>(() => new TrayService(null, null!));
    }

    [Fact]
    public async Task It_shows_and_hides_the_tray_icon()
    {
        await Headless.Dispatch(() =>
        {
            var service = new TrayService(null, _ => { });

            // Nothing to hide yet.
            service.Hide();

            service.Show("Core", new CommandSet([new AppCommand("quit", "Quit")]));

            service.Hide();

            service.Dispose();
        });
    }

    [Fact]
    public async Task Disposing_before_showing_is_a_noop()
    {
        await Headless.Dispatch(() =>
        {
            var service = new TrayService(null, _ => { });

            service.Dispose();

            var icons = TrayIcon.GetIcons(Application.Current!);
            Assert.True(icons is null || icons.Count == 0);
        });
    }

    [Fact]
    public async Task Showing_twice_keeps_one_icon_and_dispose_removes_it()
    {
        await Headless.Dispatch(() =>
        {
            var service = new TrayService(null, _ => { });
            var menu = new CommandSet([new AppCommand("quit", "Quit")]);

            service.Show("first", menu);
            service.Show("second", menu);

            var icons = TrayIcon.GetIcons(Application.Current!);
            Assert.NotNull(icons);
            Assert.Single(icons);

            service.Dispose();

            Assert.Empty(TrayIcon.GetIcons(Application.Current!)!);
        });
    }

    [Fact]
    public async Task Use_after_dispose_is_rejected()
    {
        await Headless.Dispatch(() =>
        {
            var service = new TrayService(null, _ => { });
            service.Dispose();

            Assert.Throws<ObjectDisposedException>(
                () => service.Show("app", new CommandSet([new AppCommand("quit", "Quit")])));
            Assert.Throws<ObjectDisposedException>(service.Hide);
        });
    }

    [Fact]
    public async Task Two_services_coexist_and_dispose_removes_only_its_own()
    {
        await Headless.Dispatch(() =>
        {
            var menu = new CommandSet([new AppCommand("quit", "Quit")]);
            var first = new TrayService(null, _ => { });
            var second = new TrayService(null, _ => { });

            first.Show("first", menu);
            second.Show("second", menu);

            Assert.Equal(2, TrayIcon.GetIcons(Application.Current!)!.Count);

            first.Dispose();

            Assert.Single(TrayIcon.GetIcons(Application.Current!)!);

            second.Dispose();

            Assert.Empty(TrayIcon.GetIcons(Application.Current!)!);
        });
    }

    [Fact]
    public async Task Show_reuses_the_menu_when_the_commands_are_unchanged()
    {
        await Headless.Dispatch(() =>
        {
            using var tray = new TrayService(icon: null, activated: _ => { });

            tray.Show("Core", new CommandSet([new AppCommand("quit", "Quit")]));
            var first = tray.Menu;
            tray.Show("Core", new CommandSet([new AppCommand("quit", "Quit")]));

            Assert.Same(first, tray.Menu);

            tray.Show("Core", new CommandSet([new AppCommand("quit", "Close")]));

            Assert.NotSame(first, tray.Menu);
        });
    }
}
