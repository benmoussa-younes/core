// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Services;
using Core.UI.Adwaita;
using Core.UI.Notifications;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class NotificationHostTests
{
    [Theory]
    [InlineData(MessageSeverity.Information, "PrimaryBrush")]
    [InlineData(MessageSeverity.Success, "SuccessBrush")]
    [InlineData(MessageSeverity.Warning, "WarningBrush")]
    [InlineData(MessageSeverity.Error, "ErrorBrush")]
    public async Task The_tone_follows_the_severity(MessageSeverity severity, string token)
    {
        var (tone, expected) = await Headless.Dispatch(() =>
            (NotificationHost.Tone(severity), ThemeBrushes.Resolve(token)));

        Assert.Same(expected, tone);
    }

    [Fact]
    public void A_fresh_host_is_quiet()
    {
        var host = new NotificationHost();

        Assert.Equal(0, host.ToastCount);
        Assert.False(host.IsBannerOpen);
        Assert.Equal(TimeSpan.FromSeconds(4), host.ToastDuration);

        host.Expire();

        Assert.Equal(0, host.ToastCount);
    }

    [Fact]
    public void It_shows_and_dismisses_a_banner()
    {
        var host = new NotificationHost();

        host.Banner("failed", MessageSeverity.Error);
        Assert.True(host.IsBannerOpen);

        host.DismissBanner();
        Assert.False(host.IsBannerOpen);
    }

    [Fact]
    public async Task The_banner_sits_on_the_surface_with_a_severity_accent()
    {
        var (background, border, surface, tone) = await Headless.Dispatch(() =>
        {
            var host = new NotificationHost();
            host.Banner("failed", MessageSeverity.Error);

            return (
                host.BannerView.Background,
                host.BannerView.BorderBrush,
                ThemeBrushes.Resolve("SurfaceRaisedBrush"),
                NotificationHost.Tone(MessageSeverity.Error));
        });

        Assert.Same(surface, background);
        Assert.Same(tone, border);
    }

    [Fact]
    public async Task Applying_the_theme_keeps_the_severity_accent()
    {
        await Headless.Dispatch(() =>
        {
            var host = new NotificationHost();
            host.Banner("warned", MessageSeverity.Warning);
            host.ApplyTheme();

            Assert.Same(ThemeBrushes.Resolve("SurfaceRaisedBrush"), host.BannerView.Background);
            Assert.Same(NotificationHost.Tone(MessageSeverity.Warning), host.BannerView.BorderBrush);
        });
    }

    [Fact]
    public async Task A_detached_host_stops_its_toast_timer()
    {
        await Headless.Dispatch(() =>
        {
            var host = new NotificationHost();
            var window = new global::Avalonia.Controls.Window { Content = host };
            window.Show();

            host.Show("saved", MessageSeverity.Success);
            window.Close();

            Assert.Equal(1, host.ToastCount);
        });
    }

    [Fact]
    public void Null_messages_are_rejected()
    {
        var host = new NotificationHost();

        Assert.Throws<ArgumentNullException>(() => host.Show(null!, MessageSeverity.Information));
        Assert.Throws<ArgumentNullException>(() => host.Banner(null!, MessageSeverity.Error));
    }

    [Fact]
    public async Task It_shows_and_expires_toasts()
    {
        await Headless.Dispatch(() =>
        {
            var host = new NotificationHost();

            host.Show("saved", MessageSeverity.Success);
            host.Show("warned", MessageSeverity.Warning);

            Assert.Equal(2, host.ToastCount);

            host.Expire();

            Assert.Equal(0, host.ToastCount);
        });
    }
}
