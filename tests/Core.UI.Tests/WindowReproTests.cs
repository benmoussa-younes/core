// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Core.Mvp.Models;
using Core.Mvp.Presenters;
using Core.Mvp.Views;
using Core.Results;
using Core.UI.Chrome;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class WindowReproTests
{
    [Fact]
    public async Task ApplyPlacement_refuses_a_NaN_size()
    {
        // NaN passes `placement.Width <= 0` because every comparison with NaN is
        // false, so the guard the code documents does not fire.
        await Headless.Dispatch(() =>
        {
            var window = new CoreWindow(new FakeTrayService()) { Width = 800, Height = 600 };
            window.Show();

            window.ApplyPlacement(new WindowPlacement(0, 0, double.NaN, double.NaN, false));

            Assert.True(double.IsFinite(window.Width), $"width={window.Width}");
            Assert.True(double.IsFinite(window.Height), $"height={window.Height}");

            window.Close();
        });
    }

    [Fact]
    public async Task A_closed_tab_releases_its_presenter()
    {
        // The assertion runs while the window is reachable, so a presenter the
        // window still references cannot be collected.
        await Headless.Dispatch(() =>
        {
            var window = new CoreWindow(new FakeTrayService());
            window.Show();

            var closed = CloseATab(window);

            Dispatcher.UIThread.RunJobs();
            Collect();

            Assert.False(closed.IsAlive, "the window still references a closed presenter");

            GC.KeepAlive(window);
            window.Close();
        });
    }

    // Not inlined: a local of the caller would keep the presenter reachable on its
    // own stack frame and hide the retention under test.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CloseATab(CoreWindow window)
    {
        var presenter = new PlainPresenter();
        window.OpenTab("one", "One", presenter);
        window.CloseTab("one");
        return new WeakReference(presenter);
    }

    private static void Collect()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private sealed class PlainView : Control, IView
    {
        public void SetBusy(bool busy)
        {
        }

        public void ShowError(CoreError error)
        {
        }
    }

    private sealed class PlainPresenter : IViewPresenter
    {
        private readonly PlainView _view = new();

        public IView View => _view;

        public bool IsBusy { get; set; }

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ActivateAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DeactivateAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
