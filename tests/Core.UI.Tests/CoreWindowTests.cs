// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Core.Mvp.Composition;
using Core.Mvp.Presentation;
using Core.Mvp.Presenters;
using Core.Mvp.Services;
using Core.Mvp.Views;
using Core.UI.Chrome;
using Core.UI.Adwaita;
using Core.Results;
using Core.UI.Dialogs;
using Core.UI.Notifications;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class CoreWindowTests
{
    [Fact]
    public async Task It_applies_a_shell_snapshot()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var backRequested = 0;
            window.BackNavigationRequested += () => backRequested++;
            window.Show();

            window.SetSnapshot(new ShellSnapshot(
                "Title",
                "Subtitle",
                new CommandSet([new AppCommand("a", "A")]),
                Busy: true,
                CanGoBack: true));

            Assert.Equal("Title", window.Header.Title);
            Assert.Equal("Subtitle", window.Header.Subtitle);
            Assert.Single(window.Actions.Children);
            Assert.True(window.BackButton.IsVisible);
            Assert.True(window.IsBusyVisible);

            window.BackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, backRequested);

            window.Close();
        });
    }

    [Fact]
    public async Task A_snapshot_without_commands_is_rejected()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();

            Assert.Throws<ArgumentException>(
                () => window.SetSnapshot(new ShellSnapshot("Title", string.Empty, null!)));
            Assert.Throws<ArgumentException>(
                () => window.SetSnapshot(new ShellSnapshot("Title", string.Empty, new CommandSet(null!))));

            window.Close();
        });
    }

    [Fact]
    public async Task A_back_flag_without_a_handler_is_not_shown()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();

            window.SetSnapshot(new ShellSnapshot(
                "Title",
                string.Empty,
                CommandSet.Empty,
                CanGoBack: true));

            Assert.False(window.BackButton.IsVisible);

            window.Close();
        });
    }

    [Fact]
    public async Task The_window_can_be_hidden_and_shown_again()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            Assert.True(window.IsVisible);

            window.HideWindow();
            Assert.False(window.IsVisible);

            window.ShowWindow();
            Assert.True(window.IsVisible);

            window.Close();
        });
    }

    [Fact]
    public async Task The_close_guard_can_keep_the_window_open()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            window.ConfirmClose = () => ValueTask.FromResult(false);

            window.Close();
            Assert.True(window.IsVisible);

            window.ConfirmClose = null;
            window.Close();
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public async Task The_close_guard_can_approve_the_close()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            window.ConfirmClose = () => ValueTask.FromResult(true);

            window.Close();
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public async Task Quit_bypasses_the_close_guard()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            var asked = false;
            var quit = false;
            window.ConfirmClose = () => { asked = true; return ValueTask.FromResult(false); };
            window.QuitRequested += () => { quit = true; return ValueTask.CompletedTask; };

            window.Quit();

            Assert.False(asked);
            Assert.True(quit);
            Assert.True(window.IsVisible);

            window.Close();
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public async Task The_busy_overlay_covers_only_the_content_and_leaves_the_chrome_reachable()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();

            window.SetSnapshot(new ShellSnapshot("Title", "Subtitle", new CommandSet([new AppCommand("a", "A")]), Busy: true));

            Assert.True(window.IsBusyVisible);
            Assert.Same(window.ContentRegion, window.BusyOverlay.Parent);
            Assert.Same(window.ContentRegion.Parent, window.Header.Parent);
            Assert.DoesNotContain(window.Header, window.ContentRegion.Children);

            window.Close();
        });
    }

    [Fact]
    public async Task A_snapshot_can_show_determinate_progress()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            var bar = Assert.IsType<ProgressBar>(window.BusyOverlay.Child);

            window.SetSnapshot(new ShellSnapshot("T", "", CommandSet.Empty, Busy: true) { Progress = 0.5 });
            Assert.False(bar.IsIndeterminate);
            Assert.Equal(0.5, bar.Value);

            window.SetSnapshot(new ShellSnapshot("T", "", CommandSet.Empty, Busy: true) { Progress = 2.0 });
            Assert.Equal(1.0, bar.Value);

            window.SetSnapshot(new ShellSnapshot("T", "", CommandSet.Empty, Busy: true));
            Assert.True(bar.IsIndeterminate);

            window.Close();
        });
    }

    [Fact]
    public async Task It_opens_selects_and_closes_tabs()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var one = new FakeView();
            var two = new FakeView();

            window.Show();
            window.OpenTab("one", "One", one);
            window.OpenTab("two", "Two", two);

            Assert.Equal(2, window.Tabs.TabCount);
            Assert.Same(two, window.Active);
            Assert.Same(two, window.ContentHost.Content);

            Click(window.Tabs.SelectButtons[0]);
            Assert.Same(one, window.Active);

            window.CloseTab("one");
            Assert.Same(two, window.Active);

            window.CloseTab("two");
            Assert.Null(window.Active);
            Assert.Null(window.ContentHost.Content);
            Assert.Equal(0, window.Tabs.TabCount);

            window.Close();
        });
    }

    [Fact]
    public async Task It_pushes_and_pops_stack_pages()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var one = new FakeView();
            var two = new FakeView();

            window.Show();
            window.Push("One", "first", one);

            Assert.Same(one, window.Active);
            Assert.False(window.BackButton.IsVisible);

            window.Push("Two", "second", two);

            Assert.Same(two, window.Active);
            Assert.Equal("Two", window.Header.Title);
            Assert.True(window.BackButton.IsVisible);

            window.Pop();

            Assert.Same(one, window.Active);
            Assert.False(window.BackButton.IsVisible);

            window.Pop();

            Assert.Same(one, window.Active);

            window.Close();
        });
    }

    [Fact]
    public async Task It_forwards_command_activations()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            string? invoked = null;

            window.Show();
            window.OpenTab("one", "One", new FakeView());
            window.CommandInvoked += (_, id) => invoked = id;
            window.SetSnapshot(new ShellSnapshot("T", "", new CommandSet([new AppCommand("save", "Save")])));
            Click((Button)window.Actions.Children[0]);

            Assert.Equal("save", invoked);

            window.Close();
        });
    }

    [Fact]
    public async Task It_quits()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var quit = false;

            window.Show();
            window.QuitRequested += () => { quit = true; return ValueTask.CompletedTask; };

            window.Quit();

            Assert.True(quit);

            window.Close();
        });
    }

    [Fact]
    public async Task Showing_the_window_again_raises_the_activation()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var activated = false;

            window.Show();
            window.HideWindow();
            ((IApplicationHost)window).Activated += () => activated = true;

            window.ShowWindow();

            Assert.True(window.IsVisible);
            Assert.True(activated);

            window.Close();
        });
    }

    [Fact]
    public async Task Close_follows_the_close_behavior()
    {
        await Headless.Dispatch(() =>
        {
            var hidden = Create();
            var closed = false;
            hidden.Closed += (_, _) => closed = true;
            hidden.Show();

            hidden.CloseBehavior = CloseBehavior.HideToTray;
            hidden.RequestClose();

            Assert.False(hidden.IsVisible);
            Assert.False(closed);

            var quit = Create();
            quit.Show();
            quit.CloseBehavior = CloseBehavior.Quit;
            quit.RequestClose();

            Assert.False(quit.IsVisible);
        });
    }

    [Fact]
    public async Task Show_window_brings_it_up()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();

            window.ShowWindow();

            Assert.True(window.IsVisible);

            window.Close();
        });
    }

    [Fact]
    public async Task Null_arguments_are_rejected()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();

            Assert.Throws<ArgumentNullException>(() => window.SetSnapshot(null!));
            Assert.Throws<ArgumentNullException>(() => window.OpenTab("id", "title", (IView)null!));
            Assert.Throws<ArgumentNullException>(() => window.OpenTab("id", "title", (IViewPresenter)null!));
            Assert.Throws<ArgumentNullException>(() => window.CloseTab(null!));
            Assert.Throws<ArgumentNullException>(() => window.Push("t", "s", (IView)null!));
            Assert.Throws<ArgumentNullException>(() => window.Push("t", "s", (IViewPresenter)null!));
        });
    }

    [Fact]
    public async Task The_shell_buttons_have_accessible_names()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();

            Assert.Equal("Back", AutomationProperties.GetName(window.BackButton));

            window.Close();
        });
    }

    [Fact]
    public async Task It_exposes_its_services()
    {
        await Headless.Dispatch(() =>
        {
            var tray = new FakeTrayService();
            var window = new CoreWindow(tray);

            Assert.IsType<DialogService>(window.Dialogs);
            Assert.IsType<PromptService>(window.Prompts);
            Assert.IsType<NotificationService>(window.Notifications);
            Assert.Same(tray, window.Tray);
            Assert.Same(window, window.Navigation);
            Assert.Same(window.Files, window.Folders);

            Action handler = () => { };
            ((IApplicationHost)window).Activated += handler;
            ((IApplicationHost)window).Activated -= handler;
        });
    }

    [Fact]
    public async Task A_missing_service_is_rejected()
    {
        await Headless.Dispatch(() =>
        {
            Assert.Throws<ArgumentNullException>(() => new CoreWindow(null!));
        });
    }

    [Fact]
    public async Task The_window_controls_act_on_a_pointer_click()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            var controls = WindowControlsOf(window);

            ClickAt(window, controls.MaximizeButton);
            Assert.Equal(WindowState.Maximized, window.WindowState);

            ClickAt(window, controls.MaximizeButton);
            Assert.Equal(WindowState.Normal, window.WindowState);

            ClickAt(window, controls.MinimizeButton);
            Assert.Equal(WindowState.Minimized, window.WindowState);

            window.Close();
        });
    }

    [Fact]
    public async Task The_header_back_button_acts_on_a_pointer_click()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var master = new FakeView();

            window.Show();
            window.OpenTab("master", "Master", master);
            window.Push("Detail", "one item", new FakeView());

            ClickAt(window, window.BackButton);

            Assert.Same(master, window.Active);

            window.Close();
        });
    }

    [Fact]
    public async Task Returning_from_a_pushed_page_restores_the_previous_snapshot()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var master = new FakeView();
            var detail = new FakeView();

            window.Show();
            window.OpenTab("master", "Master", master);
            window.SetSnapshot(new ShellSnapshot("Master", "list", new CommandSet([new AppCommand("m", "M")])));

            window.Push("Detail", "one item", detail);
            window.SetSnapshot(new ShellSnapshot("Detail", "one item", new CommandSet([new AppCommand("d", "D")])));

            Assert.Equal("Detail", window.Header.Title);

            ClickAt(window, window.BackButton);

            Assert.Same(master, window.Active);
            Assert.Equal("Master", window.Header.Title);
            Assert.Equal("Master", window.CurrentSnapshot?.Title);
            Assert.False(window.BackButton.IsVisible);

            window.Close();
        });
    }

    [Fact]
    public async Task A_stacked_page_shows_the_dirty_marker_in_the_header()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var page = new FakeView();
            var detail = new FakeView();

            window.Show();
            window.Push("Page", "first", page);
            window.SetSnapshot(new ShellSnapshot("Page", "first", CommandSet.Empty) { IsDirty = true });

            Assert.True(window.Header.IsDirty);

            window.Push("Detail", "one item", detail);

            Assert.False(window.Header.IsDirty);

            ClickAt(window, window.BackButton);

            Assert.True(window.Header.IsDirty);

            window.Close();
        });
    }

    [Fact]
    public async Task A_tabbed_view_keeps_the_dirty_marker_on_its_tab()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var view = new FakeView();

            window.Show();
            window.OpenTab("master", "Master", view);
            window.SetSnapshot(new ShellSnapshot("Master", "list", CommandSet.Empty) { IsDirty = true });

            Assert.True(window.Tabs.IsDirty("master"));
            Assert.False(window.Header.IsDirty);

            window.Close();
        });
    }

    [Fact]
    public async Task A_pushed_page_over_a_tab_reports_its_own_marker()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var master = new FakeView();
            var detail = new FakeView();

            window.Show();
            window.OpenTab("master", "Master", master);
            window.SetSnapshot(new ShellSnapshot("Master", "list", CommandSet.Empty) { IsDirty = true });

            window.Push("Detail", "one item", detail);
            window.SetSnapshot(new ShellSnapshot("Detail", "one item", CommandSet.Empty) { IsDirty = true });

            Assert.True(window.Header.IsDirty);

            ClickAt(window, window.BackButton);

            Assert.False(window.Header.IsDirty);
            Assert.True(window.Tabs.IsDirty("master"));

            window.Close();
        });
    }

    [Fact]
    public async Task Clearing_the_chrome_removes_the_header_marker()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var page = new FakeView();

            window.Show();
            window.Push("Page", "first", page);
            window.SetSnapshot(new ShellSnapshot("Page", "first", CommandSet.Empty) { IsDirty = true });

            Assert.True(window.Header.IsDirty);

            window.OpenTab("other", "Other", new FakeView());

            Assert.Null(window.CurrentSnapshot);
            Assert.False(window.Header.IsDirty);

            window.Close();
        });
    }

    [Fact]
    public async Task Switching_tabs_restores_each_snapshot()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var one = new FakeView();
            var two = new FakeView();

            window.Show();
            window.OpenTab("one", "One", one);
            window.SetSnapshot(new ShellSnapshot("First", "", CommandSet.Empty));
            window.OpenTab("two", "Two", two);
            window.SetSnapshot(new ShellSnapshot("Second", "", CommandSet.Empty));

            Click(window.Tabs.SelectButtons[0]);
            Assert.Equal("First", window.Header.Title);

            Click(window.Tabs.SelectButtons[1]);
            Assert.Equal("Second", window.Header.Title);

            window.Close();
        });
    }

    [Fact]
    public async Task The_shell_uses_the_shared_tokens_and_marks_the_selected_tab()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            window.OpenTab("one", "One", new FakeView());
            window.OpenTab("two", "Two", new FakeView());

            var background = (ISolidColorBrush)ThemeBrushes.Resolve("BackgroundBrush");
            var surface = (ISolidColorBrush)ThemeBrushes.Resolve("SurfaceBrush");
            var selected = (ISolidColorBrush)ThemeBrushes.Resolve("SelectedBrush");

            Assert.Equal(background.Color, ((ISolidColorBrush)window.Background!).Color);
            Assert.Equal(surface.Color, ((ISolidColorBrush)window.Header.Background!).Color);

            var first = (Border)window.Tabs.Children[0];
            var second = (Border)window.Tabs.Children[1];

            Assert.Equal(selected.Color, ((ISolidColorBrush)second.Background!).Color);
            Assert.NotEqual(selected.Color, ((ISolidColorBrush)first.Background!).Color);

            window.Close();
        });
    }

    [Fact]
    public async Task Applying_the_theme_re_resolves_the_chrome()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            window.OpenTab("one", "One", new FakeView());

            window.ApplyTheme();

            var background = (ISolidColorBrush)ThemeBrushes.Resolve("BackgroundBrush");
            Assert.Equal(background.Color, ((ISolidColorBrush)window.Background!).Color);

            window.Close();
        });
    }

    [Fact]
    public async Task Replacing_the_commands_replaces_the_bindings()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            window.SetSnapshot(new ShellSnapshot("T", "", new CommandSet([new AppCommand("save", "Save", Shortcut: "Ctrl+S")])));
            Assert.Single(window.KeyBindings);

            window.SetSnapshot(new ShellSnapshot("T", "", new CommandSet([new AppCommand("open", "Open", Shortcut: "Ctrl+O")])));
            Assert.Single(window.KeyBindings);

            window.SetSnapshot(new ShellSnapshot("T", "", CommandSet.Empty));
            Assert.Empty(window.KeyBindings);

            window.Close();
        });
    }

    [Fact]
    public async Task Re_publishing_an_unchanged_snapshot_keeps_the_bindings()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            window.SetSnapshot(new ShellSnapshot("T", "", new CommandSet([new AppCommand("save", "Save", Shortcut: "Ctrl+S")])));
            var binding = window.KeyBindings[0];

            window.SetSnapshot(new ShellSnapshot("T", "", new CommandSet([new AppCommand("save", "Save", Shortcut: "Ctrl+S")])));

            Assert.Same(binding, window.KeyBindings[0]);

            window.Close();
        });
    }

    [Fact]
    public async Task A_shortcut_command_can_execute_and_query()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            string? invoked = null;
            window.Show();
            window.OpenTab("one", "One", new FakeView());
            window.CommandInvoked += (_, id) => invoked = id;
            window.SetSnapshot(new ShellSnapshot("T", "", new CommandSet([new AppCommand("save", "Save", Shortcut: "Ctrl+S")])));

            var command = window.KeyBindings[0].Command!;
            EventHandler handler = (_, _) => { };
            command.CanExecuteChanged += handler;
            command.CanExecuteChanged -= handler;

            Assert.True(command.CanExecute(null));
            command.Execute(null);

            Assert.Equal("save", invoked);

            window.Close();
        });
    }

    [Fact]
    public async Task A_view_can_drive_the_busy_overlay()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var view = new BusyFakeView();
            window.Show();
            window.Push("Busy", string.Empty, view);

            Assert.False(window.IsBusyVisible);

            view.SetBusy(true);
            Assert.True(window.IsBusyVisible);

            view.SetBusy(false);
            Assert.False(window.IsBusyVisible);

            window.Close();
        });
    }

    [Fact]
    public async Task Switching_tabs_unsubscribes_the_previous_busy_view()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var first = new BusyFakeView();
            var second = new BusyFakeView();
            window.Show();
            window.OpenTab("a", "A", first);
            window.OpenTab("b", "B", second);

            first.SetBusy(true);
            Assert.False(window.IsBusyVisible);

            second.SetBusy(true);
            Assert.True(window.IsBusyVisible);

            window.Close();
        });
    }

    [Fact]
    public async Task Closing_a_busy_tab_unsubscribes_it()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var view = new BusyFakeView();
            window.Show();
            window.OpenTab("a", "A", view);
            view.SetBusy(true);
            Assert.True(window.IsBusyVisible);

            window.CloseTab("a");
            view.SetBusy(false);

            Assert.False(window.IsBusyVisible);

            window.Close();
        });
    }

    [Fact]
    public async Task The_header_icon_is_forwarded()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var image = new DrawingImage { Drawing = new GeometryDrawing() };

            window.HeaderIcon = image;

            Assert.Same(image, window.HeaderIcon);
            Assert.Same(image, window.Header.Icon);
        });
    }

    [Fact]
    public async Task A_view_that_is_not_a_control_is_rejected()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();

            Assert.Throws<ArgumentException>(() => window.OpenTab("id", "title", new HeadlessView()));
            Assert.Throws<ArgumentException>(() => window.Push("title", "subtitle", new HeadlessView()));
        });
    }

    [Fact]
    public async Task A_user_tab_close_can_be_cancelled()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            window.OpenTab("one", "One", new FakeView());

            window.TabCloseRequested += (_, args) => args.Cancel = args.Id == "one";

            Click(window.Tabs.CloseButtons[0]);

            Assert.Equal(1, window.Tabs.TabCount);
            Assert.Same(window.Active, window.ContentHost.Content as IView);

            window.Close();
        });
    }

    [Fact]
    public async Task A_user_tab_close_proceeds_when_not_cancelled()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            window.OpenTab("one", "One", new FakeView());

            Click(window.Tabs.CloseButtons[0]);

            Assert.Equal(0, window.Tabs.TabCount);
            Assert.Null(window.ContentHost.Content);

            window.Close();
        });
    }

    [Fact]
    public async Task A_command_shortcut_becomes_a_window_key_binding()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            window.SetSnapshot(new ShellSnapshot(
                "T",
                "",
                new CommandSet([new AppCommand("save", "Save", Shortcut: "Ctrl+S")])));

            Assert.Single(window.KeyBindings);

            window.Close();
        });
    }

    [Fact]
    public async Task An_unparsable_shortcut_is_ignored()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            window.SetSnapshot(new ShellSnapshot(
                "T",
                "",
                new CommandSet([new AppCommand("save", "Save", Shortcut: "not-a-gesture")])));

            Assert.Empty(window.KeyBindings);

            window.Close();
        });
    }

    [Fact]
    public async Task Content_only_mode_shows_no_tab_strip()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();

            // A single screen enters through Push and never opens a tab.
            window.Push("Document", "", new FakeView());

            Assert.False(window.Tabs.IsVisible);
            Assert.Equal(0, window.Tabs.TabCount);
            Assert.NotNull(window.ContentHost.Content);

            window.Close();
        });
    }

    [Fact]
    public async Task The_close_control_closes_the_window()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();

            ClickAt(window, WindowControlsOf(window).CloseButton);

            Assert.False(window.IsVisible);
        });
    }

    private static CoreWindow Create()
    {
        return new CoreWindow(new FakeTrayService());
    }

    private static WindowControls WindowControlsOf(CoreWindow window)
    {
        var end = (StackPanel)window.Header.EndContent!;
        return end.Children.OfType<WindowControls>().Single();
    }

    private static void ClickAt(Window window, Control control)
    {
        // A control just added to the tree is not arranged yet; without a layout
        // pass it is 0x0 and the click lands on nothing.
        window.UpdateLayout();

        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(centre);
        window.MouseDown(centre.Value, MouseButton.Left);
        window.MouseUp(centre.Value, MouseButton.Left);
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }

    private sealed class FakeView : Control, IView
    {
        public void SetBusy(bool busy)
        {
        }

        public void ShowError(CoreError error)
        {
        }
    }

    private sealed class HeadlessView : IView
    {
        public void SetBusy(bool busy)
        {
        }

        public void ShowError(CoreError error)
        {
        }
    }

    private sealed class BusyFakeView : Control, IBusyView
    {
        public event Action<bool>? BusyChanged;

        public void SetBusy(bool busy) => BusyChanged?.Invoke(busy);

        public void ShowError(CoreError error)
        {
        }
    }
}
