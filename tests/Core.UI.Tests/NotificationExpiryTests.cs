// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Controls;
using Core.Mvp.Services;
using Core.UI.Notifications;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class NotificationExpiryTests
{
    [Fact]
    public async Task Each_toast_expires_on_its_own_deadline()
    {
        // A shared timer restarted by every Show never fires while toasts arrive
        // faster than the duration, so the first toast outlives its own deadline.
        await Headless.Dispatch(() =>
        {
            var clock = new FakeTimeProvider();
            var host = new NotificationHost(clock) { ToastDuration = TimeSpan.FromSeconds(4) };

            host.Show("first", MessageSeverity.Information);
            clock.Advance(TimeSpan.FromSeconds(3));
            host.Show("second", MessageSeverity.Information);

            clock.Advance(TimeSpan.FromSeconds(1));
            host.ExpireDue();

            Assert.Equal(1, host.ToastCount);
        });
    }

    [Fact]
    public async Task A_toast_before_its_deadline_stays()
    {
        await Headless.Dispatch(() =>
        {
            var clock = new FakeTimeProvider();
            var host = new NotificationHost(clock) { ToastDuration = TimeSpan.FromSeconds(4) };

            host.Show("first", MessageSeverity.Information);
            clock.Advance(TimeSpan.FromSeconds(3));
            host.ExpireDue();

            Assert.Equal(1, host.ToastCount);
        });
    }

    [Fact]
    public async Task A_burst_keeps_only_the_most_recent_toasts()
    {
        await Headless.Dispatch(() =>
        {
            var clock = new FakeTimeProvider();
            var host = new NotificationHost(clock) { ToastDuration = TimeSpan.FromSeconds(4) };

            for (var index = 0; index < 12; index++)
            {
                host.Show(string.Concat("toast ", index), MessageSeverity.Information);
            }

            Assert.Equal(10, host.ToastCount);
            Assert.Equal("toast 2", Assert.IsType<TextBlock>(host.ToastViews[0].Child).Text);
            Assert.Equal("toast 11", Assert.IsType<TextBlock>(host.ToastViews[^1].Child).Text);
        });
    }

    [Fact]
    public async Task Re_attaching_the_host_re_arms_the_pending_deadlines()
    {
        await Headless.Dispatch(() =>
        {
            var clock = new FakeTimeProvider();
            var host = new NotificationHost(clock) { ToastDuration = TimeSpan.FromSeconds(4) };
            var window = new Window { Width = 200, Height = 200, Content = host };
            window.Show();

            host.Show("first", MessageSeverity.Information);
            Assert.True(host.IsTimerArmed);

            window.Content = null;
            Assert.False(host.IsTimerArmed);

            window.Content = host;
            Assert.True(host.IsTimerArmed);

            window.Close();
        });
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
