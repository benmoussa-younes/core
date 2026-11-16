// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Controls;
using Core.UI.Chrome;
using Core.UI.Dialogs;
using Core.UI.Tray;
using Core.Mvp.Composition;
using Core.Mvp.Presentation;
using Core.Mvp.Services;
using Core.Mvp.Views;
using Core.Results;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class ApplicationMenuTests
{
    [Fact]
    public async Task The_shell_always_offers_quit()
    {
        var labels = await Headless.Dispatch(() =>
        {
            var window = new TestWindow();
            var items = Labels(window);
            window.Close();

            return items;
        });

        Assert.Equal(["Quit"], labels);
    }

    [Fact]
    public async Task The_product_menu_precedes_the_standard_tail()
    {
        var (labels, separators) = await Headless.Dispatch(() =>
        {
            var window = new TestWindow();
            window.SetApplicationMenu(new CommandSet(
            [
                new AppCommand("refresh", "Refresh", Shortcut: "Ctrl+R"),
                new AppCommand("export", "Export"),
            ]));

            var separators = window.PrimaryMenu.Items.OfType<Separator>().Count();
            var labels = Labels(window);
            window.Close();

            return (labels, separators);
        });

        Assert.Equal(["Refresh", "Export", "Quit"], labels);
        Assert.Equal(1, separators);
    }

    [Fact]
    public async Task About_and_preferences_are_appended_when_wired()
    {
        var labels = await Headless.Dispatch(() =>
        {
            var window = new TestWindow();
            window.SetAbout(new AboutInfo("Widget", "1.0", "notice", new Uri("https://example.test/src")));
            window.SetAppearance(new FakeAppearanceService());

            var labels = Labels(window);
            window.Close();

            return labels;
        });

        Assert.Equal(["About\u2026", "Preferences\u2026", "Quit"], labels);
    }

    [Fact]
    public async Task Quit_raises_the_quit_request()
    {
        var raised = await Headless.Dispatch(() =>
        {
            var window = new TestWindow();
            var quit = false;
            window.QuitRequested += () => { quit = true; return ValueTask.CompletedTask; };
            window.OnCommandInvoked(ShellCommandIds.Quit);
            window.Close();

            return quit;
        });

        Assert.True(raised);
    }

    [Fact]
    public async Task A_product_command_raises_command_invoked()
    {
        var invoked = await Headless.Dispatch(() =>
        {
            var window = new TestWindow();
            window.OpenTab("one", "One", new TestView());
            string? id = null;
            window.CommandInvoked += (_, value) => id = value;
            window.OnCommandInvoked("refresh");
            window.Close();

            return id;
        });

        Assert.Equal("refresh", invoked);
    }

    [Fact]
    public async Task A_command_carries_the_active_view()
    {
        var (first, second, received) = await Headless.Dispatch(() =>
        {
            var window = new TestWindow();
            var first = new TestView();
            var second = new TestView();

            window.OpenTab("one", "One", first);
            window.OpenTab("two", "Two", second);

            IView? seen = null;
            window.CommandInvoked += (view, _) => seen = view;
            window.OnCommandInvoked("refresh");
            window.Close();

            return (first, second, seen);
        });

        Assert.Same(second, received);
        Assert.NotSame(first, received);
    }

    [Fact]
    public async Task An_unwired_standard_command_falls_through()
    {
        var invoked = await Headless.Dispatch(() =>
        {
            var window = new TestWindow();
            window.OpenTab("one", "One", new TestView());
            string? id = null;
            window.CommandInvoked += (_, value) => id = value;
            window.OnCommandInvoked(ShellCommandIds.About);
            window.Close();

            return id;
        });

        Assert.Equal(ShellCommandIds.About, invoked);
    }

    [Fact]
    public async Task About_routes_to_the_about_dialog()
    {
        var captured = await Headless.Dispatch(() =>
        {
            var window = new TestWindow();
            var info = new AboutInfo("Widget", "1.0", "notice", new Uri("https://example.test/src"));
            window.SetAbout(info);

            AboutInfo? seen = null;
            Window? dialog = null;
            window.AboutFactory = value =>
            {
                seen = value;
                dialog = new Window();
                return dialog;
            };

            window.Show();
            window.OnCommandInvoked(ShellCommandIds.About);
            dialog!.Close();
            window.Close();

            return seen;
        });

        Assert.NotNull(captured);
        Assert.Equal("Widget", captured.ProductName);
    }

    [Fact]
    public async Task Preferences_routes_to_the_preferences_dialog()
    {
        var seen = await Headless.Dispatch(() =>
        {
            var window = new TestWindow();
            var service = new FakeAppearanceService();
            window.SetAppearance(service);

            IAppearanceService? captured = null;
            Window? dialog = null;
            window.PreferencesFactory = value =>
            {
                captured = value;
                dialog = new Window();
                return dialog;
            };

            window.Show();
            window.OnCommandInvoked(ShellCommandIds.Preferences);
            dialog!.Close();
            window.Close();

            return captured;
        });

        Assert.NotNull(seen);
    }

    [Fact]
    public async Task A_shortcut_is_bound_once_across_the_toolbar_and_menu()
    {
        var count = await Headless.Dispatch(() =>
        {
            var window = new TestWindow();
            window.SetApplicationMenu(new CommandSet([new AppCommand("refresh", "Refresh", Shortcut: "Ctrl+R")]));
            window.SetSnapshot(new ShellSnapshot(
                "Home",
                string.Empty,
                new CommandSet([new AppCommand("refresh", "Refresh", Shortcut: "Ctrl+R")])));

            var count = window.KeyBindings.Count;
            window.Close();

            return count;
        });

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task A_gesture_is_deduplicated_across_spellings()
    {
        var count = await Headless.Dispatch(() =>
        {
            var window = new TestWindow();
            window.SetApplicationMenu(new CommandSet(
            [
                new AppCommand("first", "First", Shortcut: "Ctrl+S"),
                new AppCommand("second", "Second", Shortcut: "ctrl+s"),
            ]));

            var count = window.KeyBindings.Count;
            window.Close();

            return count;
        });

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task A_confirmation_suspends_the_window_accelerators_and_restores_them()
    {
        await Headless.Dispatch(async () =>
        {
            var window = new TestWindow();
            window.SetApplicationMenu(new CommandSet([new AppCommand("refresh", "Refresh", Shortcut: "Ctrl+R")]));
            window.Show();

            Assert.Single(window.KeyBindings);

            using var source = new CancellationTokenSource();
            var pending = window.Dialogs.ConfirmAsync(
                new ConfirmRequest("Quit?", string.Empty, "Quit"),
                source.Token).AsTask();

            Assert.Empty(window.KeyBindings);
            Assert.False(window.ContentRegion.IsEffectivelyEnabled);

            source.Cancel();
            Assert.Equal(ConfirmResult.Cancel, await pending);

            Assert.Single(window.KeyBindings);
            Assert.True(window.ContentRegion.IsEffectivelyEnabled);

            window.Close();
        });
    }

    [Fact]
    public async Task An_application_command_shortcut_is_bound()
    {
        var count = await Headless.Dispatch(() =>
        {
            var window = new TestWindow();
            window.SetApplicationMenu(new CommandSet([new AppCommand("open", "Open", Shortcut: "Ctrl+K")]));

            var count = window.KeyBindings.Count;
            window.Close();

            return count;
        });

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task A_disabled_command_shortcut_is_not_bound()
    {
        var count = await Headless.Dispatch(() =>
        {
            var window = new TestWindow();
            window.SetApplicationMenu(new CommandSet(
                [new AppCommand("open", "Open", Shortcut: "Ctrl+K", IsEnabled: false)]));

            var count = window.KeyBindings.Count;
            window.Close();

            return count;
        });

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task The_window_exposes_the_file_picker_and_the_clipboard()
    {
        var (files, clipboard) = await Headless.Dispatch(() =>
        {
            var window = new TestWindow();
            var services = (window.Files, ((IViewHost)window).Clipboard);
            window.Close();

            return services;
        });

        Assert.NotNull(files);
        Assert.NotNull(clipboard);
    }

    [Fact]
    public void The_standard_ids_are_reserved()
    {
        Assert.Equal("core.about", ShellCommandIds.About);
        Assert.Equal("core.preferences", ShellCommandIds.Preferences);
        Assert.Equal("core.quit", ShellCommandIds.Quit);
    }

    private static List<string> Labels(CoreWindow window) =>
        window.PrimaryMenu.Items.OfType<MenuItem>().Select(LabelOf).ToList();

    private static string LabelOf(MenuItem item) => item.Header switch
    {
        TextBlock text => text.Text ?? string.Empty,
        Grid grid => ((TextBlock)grid.Children[0]).Text ?? string.Empty,
        _ => string.Empty,
    };

    private sealed class TestView : Control, IView
    {
        public void SetBusy(bool busy)
        {
        }

        public void ShowError(CoreError error)
        {
        }
    }

    private sealed class TestWindow : CoreWindow
    {
        public TestWindow()
            : base(new TrayService(icon: null, activated: _ => { }))
        {
        }
    }

    private sealed class FakeAppearanceService : IAppearanceService
    {
        public AppearanceSettings Current { get; private set; } = AppearanceSettings.Default;

        public void ChooseTheme(ThemePreference theme) => Current = Current with { Theme = theme };

        public void ChooseAccent(string key) => Current = Current with { AccentKey = key };

        public void SetRemember(bool remember) => Current = Current with { Remember = remember };

        public void Apply()
        {
        }
    }
}
