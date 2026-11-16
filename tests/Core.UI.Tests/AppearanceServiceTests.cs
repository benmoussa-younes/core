// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Core.UI.Hosting;
using Core.UI.Preferences;
using Core.Mvp.Services;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class AppearanceServiceTests
{
    [Fact]
    public void It_loads_the_saved_settings()
    {
        var store = new FakeStore { Initial = new AppearanceSettings(ThemePreference.Dark, "AccentBlue", true) };
        var service = new AppearanceService(store);

        Assert.Equal(ThemePreference.Dark, service.Current.Theme);
        Assert.Equal("AccentBlue", service.Current.AccentKey);
        Assert.True(service.Current.Remember);
    }

    [Fact]
    public void A_null_store_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new AppearanceService(null!));
    }

    [Fact]
    public async Task Choosing_a_theme_applies_it_and_persists_when_remembered()
    {
        await Headless.Dispatch(() =>
        {
            var store = new FakeStore { Initial = new AppearanceSettings(Remember: true) };
            var service = new AppearanceService(store);

            service.ChooseTheme(ThemePreference.Light);
            Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);

            service.ChooseTheme(ThemePreference.Dark);
            Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);

            service.ChooseTheme(ThemePreference.System);
            Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
            Assert.Equal(3, store.Saves);

            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        });
    }

    [Fact]
    public async Task A_theme_is_not_persisted_until_remembered()
    {
        await Headless.Dispatch(() =>
        {
            var store = new FakeStore();
            var service = new AppearanceService(store);

            service.ChooseTheme(ThemePreference.Dark);
            Assert.Equal(0, store.Saves);

            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        });
    }

    [Fact]
    public async Task Choosing_an_accent_applies_it_and_validates_the_key()
    {
        await Headless.Dispatch(() =>
        {
            var store = new FakeStore { Initial = new AppearanceSettings(Remember: true) };
            var service = new AppearanceService(store);

            service.ChooseAccent("AccentAmber");

            Assert.Equal("AccentAmber", service.Current.AccentKey);
            Assert.Equal(
                Color.Parse("#F59E0B"),
                Assert.IsType<SolidColorBrush>(Application.Current!.Resources["PrimaryBrush"]).Color);
            Assert.Equal(1, store.Saves);

            Assert.Throws<ArgumentException>(() => service.ChooseAccent("AccentNope"));
            Assert.Throws<ArgumentException>(() => service.ChooseAccent(string.Empty));

            ((CoreApplication)Application.Current!).ApplyAccent("AccentEmerald");
        });
    }

    [Fact]
    public void Remember_saves_or_clears_the_store()
    {
        var store = new FakeStore();
        var service = new AppearanceService(store);

        service.SetRemember(true);
        Assert.Equal(1, store.Saves);
        Assert.True(service.Current.Remember);

        service.SetRemember(false);
        Assert.Equal(1, store.Clears);
        Assert.False(service.Current.Remember);
    }

    [Fact]
    public async Task Apply_applies_the_stored_theme_and_accent()
    {
        await Headless.Dispatch(() =>
        {
            var store = new FakeStore { Initial = new AppearanceSettings(ThemePreference.Light, "AccentRose", true) };
            var service = new AppearanceService(store);

            service.Apply();

            Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
            Assert.Equal(
                Color.Parse("#F43F5E"),
                Assert.IsType<SolidColorBrush>(Application.Current!.Resources["PrimaryBrush"]).Color);

            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            ((CoreApplication)Application.Current!).ApplyAccent("AccentEmerald");
        });
    }

    [Fact]
    public async Task Apply_without_an_accent_keeps_the_default()
    {
        await Headless.Dispatch(() =>
        {
            var store = new FakeStore { Initial = AppearanceSettings.Default };
            var service = new AppearanceService(store);

            service.Apply();

            Assert.Null(service.Current.AccentKey);
        });
    }

    [Fact]
    public async Task Apply_ignores_an_unknown_accent_key()
    {
        await Headless.Dispatch(() =>
        {
            var store = new FakeStore
            {
                Initial = new AppearanceSettings(ThemePreference.Dark, "AccentGone", true),
            };
            var service = new AppearanceService(store);

            service.Apply();

            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        });
    }

    [Fact]
    public async Task A_save_failure_does_not_escape_a_theme_change()
    {
        await Headless.Dispatch(() =>
        {
            var store = new FakeStore
            {
                Initial = new AppearanceSettings(Remember: true),
                ThrowOnSave = true,
            };
            var service = new AppearanceService(store);

            service.ChooseTheme(ThemePreference.Dark);

            Assert.True(store.ThrowOnSave);
            Assert.Equal(1, store.Saves);

            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        });
    }

    [Fact]
    public async Task An_out_of_range_theme_is_rejected()
    {
        await Headless.Dispatch(() =>
        {
            var service = new AppearanceService(new FakeStore());

            Assert.Throws<ArgumentOutOfRangeException>(() => service.ChooseTheme((ThemePreference)99));
        });
    }

    private sealed class FakeStore : IAppearanceStore
    {
        public AppearanceSettings? Initial { get; set; }

        public int Saves { get; private set; }

        public int Clears { get; private set; }

        public bool ThrowOnSave { get; set; }

        public AppearanceSettings? Load() => Initial;

        public void Save(AppearanceSettings settings)
        {
            Saves++;

            if (ThrowOnSave)
            {
                throw new IOException("disk full");
            }
        }

        public void Clear() => Clears++;
    }
}
