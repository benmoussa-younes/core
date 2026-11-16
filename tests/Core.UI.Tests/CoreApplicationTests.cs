// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Core.UI.Adwaita;
using Core.UI.Hosting;
using Core.Mvp.Services;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class CoreApplicationTests
{
    [Fact]
    public async Task It_names_the_app_and_loads_fluent()
    {
        await Headless.Dispatch(() =>
        {
            var application = new TestApplication();

            application.Initialize();

            Assert.Equal("core-test", application.Name);
            Assert.Contains(application.Styles, style => style is FluentTheme);
        });
    }

    [Fact]
    public void The_builder_sets_the_application_id()
    {
        Assert.NotNull(CoreAppBuilder.Configure<TestApplication>("core-test"));
        Assert.Throws<ArgumentException>(() => CoreAppBuilder.Configure<TestApplication>(string.Empty));
        Assert.Throws<ArgumentException>(() => CoreAppBuilder.Configure<TestApplication>(" "));
    }

    [Fact]
    public void The_startup_variant_is_dark_when_the_platform_cannot_report_one()
    {
        Assert.Equal(ThemeVariant.Dark, CoreApplication.StartupVariant(null));
        Assert.Equal(ThemeVariant.Default, CoreApplication.StartupVariant(PlatformThemeVariant.Light));
        Assert.Equal(ThemeVariant.Default, CoreApplication.StartupVariant(PlatformThemeVariant.Dark));
    }

    [Fact]
    public async Task Initialization_selects_the_theme_variant()
    {
        var (requested, platformVariant) = await Headless.Dispatch(() =>
        {
            var application = new TestApplication();
            application.Initialize();
            application.OnFrameworkInitializationCompleted();

            return (application.RequestedThemeVariant, application.PlatformSettings?.GetColorValues().ThemeVariant);
        });

        var expected = platformVariant is null ? ThemeVariant.Dark : ThemeVariant.Default;

        Assert.Equal(expected, requested);
    }

    [Fact]
    public async Task The_accent_can_be_overridden()
    {
        var (primary, onPrimary, selected) = await Headless.Dispatch(() =>
        {
            var application = new TestApplication();
            application.Initialize();
            application.SetAccent(Color.Parse("#10B981"));

            return (
                application.Resources["PrimaryBrush"],
                application.Resources["PrimaryForegroundBrush"],
                application.Resources["SelectedBrush"]);
        });

        Assert.Equal(Color.Parse("#10B981"), Assert.IsType<SolidColorBrush>(primary).Color);
        Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(onPrimary).Color);
        Assert.Equal(0x40, Assert.IsType<SolidColorBrush>(selected).Color.A);
    }

    [Fact]
    public async Task Every_palette_accent_has_an_accessible_foreground()
    {
        var pairs = await Headless.Dispatch(() =>
        {
            var application = new TestApplication();
            application.Initialize();
            var result = new List<(Color Accent, Color Foreground)>();

            foreach (var entry in AccentPalette.Catalog)
            {
                application.ApplyAccent(entry.Key);

                result.Add((
                    Assert.IsType<SolidColorBrush>(application.Resources["PrimaryBrush"]).Color,
                    Assert.IsType<SolidColorBrush>(application.Resources["PrimaryForegroundBrush"]).Color));
            }

            application.ApplyAccent("AccentEmerald");
            return result;
        });

        Assert.All(pairs, pair =>
            Assert.True(
                ContrastRatio(pair.Accent, pair.Foreground) >= 4.5,
                $"The accent {pair.Accent} and its foreground {pair.Foreground} are below 4.5:1."));
    }

    [Fact]
    public async Task The_accent_does_not_replace_the_theme_text_or_leave_the_hover_green()
    {
        var (textBefore, textAfter, hoverBefore, hoverAfter) = await Headless.Dispatch(() =>
        {
            // The theme tokens live in theme dictionaries, so resolve them through
            // the same path a control uses; a direct Resources indexer does not
            // search the Light/Dark dictionaries.
            var application = Assert.IsAssignableFrom<CoreApplication>(Application.Current);

            var textBefore = Assert.IsType<SolidColorBrush>(ThemeResources.ResolveBrush("AccentTextBrush")).Color;
            var hoverBefore = Assert.IsType<SolidColorBrush>(ThemeResources.ResolveBrush("HoverBrush")).Color;

            application.SetAccent(ThemeResources.ResolveColor("AccentBlue"));

            var textAfter = Assert.IsType<SolidColorBrush>(ThemeResources.ResolveBrush("AccentTextBrush")).Color;
            var hoverAfter = Assert.IsType<SolidColorBrush>(ThemeResources.ResolveBrush("HoverBrush")).Color;

            application.SetAccent(ThemeResources.ResolveColor("AccentEmerald"));
            return (textBefore, textAfter, hoverBefore, hoverAfter);
        });

        Assert.Equal(textBefore, textAfter);
        Assert.NotEqual(hoverBefore, hoverAfter);
    }

    [Fact]
    public void System_resolves_to_dark_when_the_platform_cannot_report_a_variant()
    {
        Assert.Equal(ThemeVariant.Dark, CoreApplication.ResolveVariant(ThemePreference.System, null));
        Assert.Equal(ThemeVariant.Light, CoreApplication.ResolveVariant(ThemePreference.Light, null));
        Assert.Equal(ThemeVariant.Dark, CoreApplication.ResolveVariant(ThemePreference.Dark, null));
        Assert.Equal(ThemeVariant.Default, CoreApplication.ResolveVariant(ThemePreference.System, PlatformThemeVariant.Light));
    }

    [Fact]
    public async Task A_palette_accent_can_be_applied()
    {
        var primary = await Headless.Dispatch(() =>
        {
            var application = new TestApplication();
            application.Initialize();
            application.ApplyAccent("AccentBlue");

            return Assert.IsType<SolidColorBrush>(application.Resources["PrimaryBrush"]).Color;
        });

        Assert.Equal(Color.Parse("#3B82F6"), primary);
    }

    [Fact]
    public async Task An_unknown_accent_is_rejected()
    {
        await Headless.Dispatch(() =>
        {
            var application = new TestApplication();
            application.Initialize();

            Assert.Throws<ArgumentException>(() => application.ApplyAccent(string.Empty));
            Assert.Throws<ArgumentException>(() => application.ApplyAccent("AccentNope"));
        });
    }

    [Fact]
    public async Task Theme_overrides_replace_the_typography_tokens()
    {
        var (font, mono, size) = await Headless.Dispatch(() =>
        {
            var application = new OverridingApplication();
            application.OnFrameworkInitializationCompleted();

            return (
                application.Resources["FontFamily"],
                application.Resources["MonospaceFont"],
                application.Resources["FontSize"]);
        });

        Assert.Equal("Overridden UI", Assert.IsType<FontFamily>(font).Name);
        Assert.Equal("Overridden Mono", Assert.IsType<FontFamily>(mono).Name);
        Assert.Equal(15.0, size);
    }

    private static double ContrastRatio(Color first, Color second)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color color) =>
            (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

        var firstLuminance = Luminance(first);
        var secondLuminance = Luminance(second);

        return (Math.Max(firstLuminance, secondLuminance) + 0.05)
            / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private sealed class TestApplication : CoreApplication
    {
        protected override string ApplicationId => "core-test";
    }

    private sealed class OverridingApplication : CoreApplication
    {
        protected override string ApplicationId => "core-overriding";

        protected override ThemeOptions? ThemeOverrides => new()
        {
            UiFont = new FontFamily("Overridden UI"),
            MonospaceFont = new FontFamily("Overridden Mono"),
            BaseFontSize = 15,
        };
    }
}
