// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

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
        });
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
