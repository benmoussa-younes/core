// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.UI.Notifications;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class NotificationServiceTests
{
    [Fact]
    public async Task It_drives_the_host()
    {
        await Headless.Dispatch(() =>
        {
            var host = new NotificationHost();
            var service = new NotificationService(host);

            service.Toast("hello");
            Assert.Equal(1, host.ToastCount);

            service.Banner("bad");
            Assert.True(host.IsBannerOpen);

            service.DismissBanner();
            Assert.False(host.IsBannerOpen);
        });
    }

    [Fact]
    public void A_null_host_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new NotificationService(null!));
    }
}
