// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Core.UI.Chrome;
using Core.Mvp.Presentation;
using Core.Mvp.Presenters;
using Core.Mvp.Views;
using Core.Results;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class PresenterLifecycleTests
{
    [Fact]
    public async Task Opening_a_tab_initializes_and_activates_its_presenter()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var presenter = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", presenter);

            Assert.Equal(1, presenter.InitializeCount);
            Assert.Equal(1, presenter.ActivateCount);
            Assert.Equal(0, presenter.DeactivateCount);

            window.CloseTab("one");

            Assert.Equal(1, presenter.DeactivateCount);
            Assert.Equal(1, presenter.DisposeCount);

            window.Close();
        });
    }

    [Fact]
    public async Task Switching_tabs_deactivates_the_old_presenter_and_activates_the_new_one()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var first = new TrackingPresenter();
            var second = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", first);
            window.OpenTab("two", "Two", second);

            Assert.Equal(1, first.DeactivateCount);
            Assert.Equal(1, second.ActivateCount);

            Click(window.Tabs.SelectButtons[0]);

            Assert.Equal(2, first.ActivateCount);
            Assert.Equal(1, first.InitializeCount);
            Assert.Equal(1, second.DeactivateCount);

            window.Close();
        });
    }

    [Fact]
    public async Task Replacing_the_active_tab_presenter_with_the_same_view_activates_the_new_one()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var view = new TrackingView();
            var first = new TrackingPresenter(view);
            var second = new TrackingPresenter(view);

            window.Show();
            window.OpenTab("one", "One", first);
            window.OpenTab("one", "One", second);

            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(1, second.InitializeCount);
            Assert.Equal(1, second.ActivateCount);

            window.Close();
        });
    }

    [Fact]
    public async Task Reopening_a_tab_with_a_new_presenter_disposes_the_old_one()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var first = new TrackingPresenter();
            var second = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", first);
            window.OpenTab("one", "One", second);

            Assert.Equal(1, first.DeactivateCount);
            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(1, second.InitializeCount);
            Assert.Equal(1, second.ActivateCount);

            window.Close();
        });
    }

    [Fact]
    public async Task Reopening_the_same_tab_with_the_same_presenter_does_not_reactivate_it()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var presenter = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", presenter);
            window.OpenTab("one", "One revised", presenter);

            Assert.Equal(1, presenter.InitializeCount);
            Assert.Equal(1, presenter.ActivateCount);

            window.Close();
        });
    }

    [Fact]
    public async Task Popping_a_stack_page_disposes_the_popped_presenter()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var master = new TrackingPresenter();
            var detail = new TrackingPresenter();

            window.Show();
            window.Push("Master", "", master);
            window.Push("Detail", "", detail);

            Assert.Equal(1, master.DeactivateCount);
            Assert.Equal(1, detail.ActivateCount);

            window.Pop();

            Assert.Equal(1, detail.DeactivateCount);
            Assert.Equal(1, detail.DisposeCount);
            Assert.Equal(2, master.ActivateCount);

            window.Close();
        });
    }

    [Fact]
    public async Task A_throwing_presenter_reports_a_banner_instead_of_crashing()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var presenter = new TrackingPresenter { InitializeFailure = new InvalidOperationException("boom") };
            var second = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", presenter);

            Assert.True(window.NotificationsHost.IsBannerOpen);

            // A presenter whose initialization failed was never active, so
            // switching away must not deactivate it.
            window.OpenTab("two", "Two", second);
            Assert.Equal(0, presenter.DeactivateCount);

            window.Close();
        });
    }

    [Fact]
    public async Task A_lifecycle_failure_does_not_leak_a_secret_in_the_banner()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var presenter = new TrackingPresenter
            {
                InitializeFailure = new InvalidOperationException("token=abc123"),
            };

            window.Show();
            window.OpenTab("one", "One", presenter);

            var banner = Assert.IsType<TextBlock>(window.NotificationsHost.BannerView.Child);
            Assert.DoesNotContain("abc123", banner.Text ?? string.Empty, StringComparison.Ordinal);
            Assert.Contains("[redacted]", banner.Text ?? string.Empty, StringComparison.Ordinal);

            window.Close();
        });
    }

    [Fact]
    public async Task Closing_the_window_disposes_every_presenter()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var first = new TrackingPresenter();
            var second = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", first);
            window.OpenTab("two", "Two", second);

            window.Close();
            window.ShutdownAsync().AsTask().GetAwaiter().GetResult();

            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(1, second.DisposeCount);
        });
    }

    [Fact]
    public async Task A_background_snapshot_does_not_overwrite_the_active_tab()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var first = new TrackingPresenter();
            var second = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", first);
            window.OpenTab("two", "Two", second);

            window.SetSnapshot(second, new ShellSnapshot("Two", "", CommandSet.Empty));
            window.SetSnapshot(first, new ShellSnapshot("One", "", CommandSet.Empty) { IsDirty = true });

            Assert.Equal("Two", window.Header.Title);
            Assert.True(window.Tabs.IsDirty("one"));
            Assert.False(window.Tabs.IsDirty("two"));

            window.Close();
            window.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task Activating_a_view_without_a_snapshot_clears_the_previous_chrome()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var first = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", first);
            window.SetSnapshot(new ShellSnapshot(
                "One",
                "Subtitle",
                new CommandSet([new AppCommand("one", "One")]),
                Busy: true,
                CanGoBack: true));

            window.OpenTab("two", "Two", new TrackingView());

            Assert.Null(window.CurrentSnapshot);
            Assert.False(window.IsBusyVisible);
            Assert.Equal(string.Empty, window.Header.Title);
            Assert.Null(window.Actions.Commands);
            Assert.False(window.BackButton.IsVisible);

            window.Close();
            window.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task Switching_away_and_closing_deactivates_a_presenter_once()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var first = new TrackingPresenter();
            var second = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", first);
            window.OpenTab("two", "Two", second);

            Assert.Equal(1, first.DeactivateCount);

            window.CloseTab("one");

            Assert.Equal(1, first.DeactivateCount);
            Assert.Equal(1, first.DisposeCount);

            window.Close();
            window.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task A_view_or_presenter_cannot_be_bound_to_two_tabs()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var view = new TrackingView();
            var presenter = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", view);
            Assert.Throws<InvalidOperationException>(() => window.OpenTab("two", "Two", view));

            window.OpenTab("three", "Three", presenter);
            Assert.Throws<InvalidOperationException>(() => window.OpenTab("four", "Four", presenter));
            Assert.Throws<InvalidOperationException>(() => window.OpenTab("three", "Three", new TrackingView()));

            var shared = new TrackingView();
            window.OpenTab("five", "Five", shared);
            Assert.Throws<InvalidOperationException>(
                () => window.OpenTab("six", "Six", new TrackingPresenter(shared)));

            window.Close();
            window.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task Clicking_a_tab_with_the_mouse_activates_its_presenter()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var first = new TrackingPresenter();
            var second = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", first);
            window.OpenTab("two", "Two", second);
            window.UpdateLayout();
            window.Activate();

            // The tab strip was rendered before the presenters finished activating;
            // hit testing needs a frame that includes the current visual tree.
            _ = window.CaptureRenderedFrame();

            var button = window.Tabs.SelectButtons[0];
            var centre = button.TranslatePoint(
                new Point(button.Bounds.Width / 2, button.Bounds.Height / 2),
                window);

            Assert.NotNull(centre);
            var hit = window.InputHitTest(centre.Value) as Visual;
            Assert.True(
                hit is not null &&
                (ReferenceEquals(hit, button) || button.IsVisualAncestorOf(hit)));

            window.MouseDown(centre.Value, MouseButton.Left);
            window.MouseUp(centre.Value, MouseButton.Left);

            Assert.Equal(2, first.ActivateCount);

            window.Close();
            window.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task Switching_away_during_initialization_deactivates_once()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var pending = new PendingPresenter();
            var second = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", pending);
            window.OpenTab("two", "Two", second);

            pending.Complete();
            window.ShutdownAsync().AsTask().GetAwaiter().GetResult();

            Assert.Equal(1, pending.DeactivateCount);
            Assert.Equal(1, pending.DisposeCount);
        });
    }

    [Fact]
    public async Task A_background_completion_is_read_back_when_the_tab_returns()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var first = new TrackingPresenter { IsBusy = true };
            var second = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", first);
            Assert.True(window.IsBusyVisible);

            window.OpenTab("two", "Two", second);
            Assert.False(window.IsBusyVisible);

            Click(window.Tabs.SelectButtons[0]);
            Assert.True(window.IsBusyVisible);

            window.Close();
            window.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task Quit_can_be_cancelled_by_its_guard()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var asked = 0;
            var quit = 0;

            window.Show();
            window.QuitRequested += () => { quit++; return ValueTask.CompletedTask; };
            window.ConfirmQuit = () =>
            {
                asked++;
                return ValueTask.FromResult(asked > 1);
            };

            window.Quit();
            Assert.Equal(1, asked);
            Assert.Equal(0, quit);

            window.Quit();
            Assert.Equal(2, asked);
            Assert.Equal(1, quit);

            window.Close();
            window.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task A_throwing_quit_guard_reports_and_keeps_the_window_open()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            window.ConfirmQuit = () => throw new InvalidOperationException("boom");

            window.Quit();

            Assert.True(window.IsVisible);
            Assert.True(window.NotificationsHost.IsBannerOpen);

            window.ConfirmQuit = null;
            window.Close();
            window.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task Quit_is_idempotent_and_raises_one_request()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var count = 0;

            window.Show();
            window.QuitRequested += () => { count++; return ValueTask.CompletedTask; };
            window.Quit();
            window.Quit();

            Assert.Equal(1, count);

            window.Close();
            window.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task An_unbound_presenter_cannot_publish_and_a_tab_cannot_be_pushed()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var tab = new TrackingPresenter();
            var unbound = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", tab);

            Assert.Throws<ArgumentException>(
                () => window.SetSnapshot(unbound, new ShellSnapshot("X", "", CommandSet.Empty)));
            Assert.Throws<InvalidOperationException>(() => window.Push("Detail", "", tab));

            window.Close();
            window.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task Switching_tabs_disposes_a_pushed_page()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var first = new TrackingPresenter();
            var second = new TrackingPresenter();
            var detail = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", first);
            window.OpenTab("two", "Two", second);
            window.Push("Detail", "", detail);

            Assert.Equal(1, detail.InitializeCount);
            Assert.Equal(1, detail.ActivateCount);

            Click(window.Tabs.SelectButtons[0]);

            Assert.Equal(1, detail.DeactivateCount);
            Assert.Equal(1, detail.DisposeCount);

            window.Close();
            window.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task Switching_tabs_disposes_a_stack_of_pushed_pages()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var first = new TrackingPresenter();
            var second = new TrackingPresenter();
            var detailOne = new TrackingPresenter();
            var detailTwo = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", first);
            window.OpenTab("two", "Two", second);
            window.Push("First detail", "", detailOne);
            window.Push("Second detail", "", detailTwo);

            Click(window.Tabs.SelectButtons[0]);

            Assert.Equal(1, detailOne.DisposeCount);
            Assert.Equal(1, detailTwo.DisposeCount);

            window.Close();
            window.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task A_failing_close_guard_reports_and_keeps_the_window_open()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            window.ConfirmClose = () => throw new InvalidOperationException("boom");

            window.Close();

            Assert.True(window.IsVisible);
            Assert.True(window.NotificationsHost.IsBannerOpen);

            window.ConfirmClose = null;
            window.Close();
            window.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task A_snapshot_marks_the_active_tab_dirty_and_switching_keeps_each_state()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var first = new TrackingView();
            var second = new TrackingView();

            window.Show();
            window.OpenTab("one", "One", first);
            window.SetSnapshot(new ShellSnapshot("One", "", CommandSet.Empty) { IsDirty = true });
            Assert.True(window.Tabs.IsDirty("one"));

            window.OpenTab("two", "Two", second);
            window.SetSnapshot(new ShellSnapshot("Two", "", CommandSet.Empty));
            Assert.False(window.Tabs.IsDirty("two"));

            Click(window.Tabs.SelectButtons[0]);
            Assert.True(window.Tabs.IsDirty("one"));

            Click(window.Tabs.SelectButtons[1]);
            Assert.False(window.Tabs.IsDirty("two"));

            window.SetTabDirty("two", true);
            Assert.True(window.Tabs.IsDirty("two"));

            window.SetTabDirty("two", false);
            Assert.False(window.Tabs.IsDirty("two"));

            Assert.Throws<ArgumentNullException>(() => window.SetTabDirty(null!, true));

            window.Close();
        });
    }

    [Fact]
    public async Task A_placement_off_screen_is_brought_back_onto_the_primary_screen()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();
            window.UpdateLayout();

            var screen = window.Screens.Primary;
            Assert.NotNull(screen);

            window.ApplyPlacement(new Core.Mvp.Models.WindowPlacement(100_000, 100_000, 800, 600));

            Assert.InRange(window.Position.X, screen.WorkingArea.X, screen.WorkingArea.Right - (int)window.Width);
            Assert.InRange(window.Position.Y, screen.WorkingArea.Y, screen.WorkingArea.Bottom - (int)window.Height);

            window.Close();
        });
    }

    [Fact]
    public async Task Closing_the_last_tab_from_a_pushed_page_closes_the_page()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var tab = new TrackingPresenter();
            var detail = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", tab);
            window.Push("Detail", string.Empty, detail);
            window.CloseTab("one");

            Assert.Equal(1, detail.DeactivateCount);
            Assert.Equal(1, detail.DisposeCount);
            Assert.Null(window.Active);
            Assert.Null(window.ContentHost.Content);

            window.Close();
        });
    }

    [Fact]
    public async Task Pushing_the_same_view_twice_is_rejected()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var view = new TrackingView();

            window.Show();
            window.Push("First", string.Empty, view);

            Assert.Throws<InvalidOperationException>(() => window.Push("Second", string.Empty, view));

            window.Close();
        });
    }

    [Fact]
    public async Task Popping_a_page_restores_the_master_title()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            window.Show();

            window.Push("Master", "list", new TrackingView());
            window.Push("Detail", "detail", new TrackingView());
            window.Pop();

            Assert.Equal("Master", window.Header.Title);
            Assert.Equal("list", window.Header.Subtitle);

            window.Close();
        });
    }

    [Fact]
    public async Task A_blank_tab_id_does_not_poison_the_view()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var view = new TrackingView();

            window.Show();
            Assert.Throws<ArgumentException>(() => window.OpenTab(" ", "One", view));
            Assert.Equal(0, window.Tabs.TabCount);

            window.OpenTab("one", "One", view);
            Assert.Equal(1, window.Tabs.TabCount);

            window.Close();
        });
    }

    [Fact]
    public async Task A_late_snapshot_from_a_closed_presenter_is_ignored()
    {
        await Headless.Dispatch(() =>
        {
            var window = Create();
            var presenter = new TrackingPresenter();

            window.Show();
            window.OpenTab("one", "One", presenter);
            window.CloseTab("one");

            window.SetSnapshot(presenter, new ShellSnapshot("Late", string.Empty, CommandSet.Empty));

            Assert.Null(window.CurrentSnapshot);

            window.Close();
        });
    }

    [Fact]
    public async Task WhenIdle_waits_for_a_gated_initialization()
    {
        await Headless.Dispatch(async () =>
        {
            var window = Create();
            var presenter = new PendingPresenter();

            window.Show();
            window.OpenTab("one", "One", presenter);

            var idle = window.WhenIdleAsync();
            Assert.False(idle.IsCompleted);

            presenter.Complete();
            await idle;

            window.Close();
        });
    }

    [Fact]
    public async Task Quit_shows_a_hidden_window_before_asking_the_guard()
    {
        await Headless.Dispatch(async () =>
        {
            var window = Create();
            window.Show();
            window.Hide();

            var visibleDuringGuard = false;
            window.ConfirmQuit = () =>
            {
                visibleDuringGuard = window.IsVisible;
                return ValueTask.FromResult(false);
            };

            await window.QuitAsync();

            Assert.True(visibleDuringGuard);
            Assert.True(window.IsVisible);

            window.Close();
        });
    }

    private static CoreWindow Create() => new(new FakeTrayService());

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private sealed class TrackingView : Control, IView
    {
        public void SetBusy(bool busy)
        {
        }

        public void ShowError(CoreError error)
        {
        }
    }

    private sealed class PendingPresenter : PresenterBase<TrackingView>
    {
        private readonly TaskCompletionSource _initialization = new();

        public PendingPresenter()
            : base(new TrackingView())
        {
        }

        public int DeactivateCount { get; private set; }

        public int DisposeCount { get; private set; }

        public void Complete() => _initialization.TrySetResult();

        protected override async ValueTask OnInitializeAsync(CancellationToken cancellationToken)
        {
            await _initialization.Task;
        }

        protected override ValueTask OnDeactivateAsync(CancellationToken cancellationToken)
        {
            DeactivateCount++;
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnDisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingPresenter : IViewPresenter
    {
        public TrackingPresenter(TrackingView? view = null)
        {
            View = view ?? new TrackingView();
        }

        public TrackingView View { get; }

        IView IViewPresenter.View => View;

        public bool IsBusy { get; set; }

        public int InitializeCount { get; private set; }

        public int ActivateCount { get; private set; }

        public int DeactivateCount { get; private set; }

        public int DisposeCount { get; private set; }

        public Exception? InitializeFailure { get; init; }

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeCount++;

            if (InitializeFailure is { } failure)
            {
                throw failure;
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask ActivateAsync(CancellationToken cancellationToken = default)
        {
            ActivateCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DeactivateAsync(CancellationToken cancellationToken = default)
        {
            DeactivateCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
