// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Core.UI.Chrome;
using Core.UI.Dialogs;
using Core.UI.Preferences;
using Core.Mvp.Services;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class DialogTests
{
    [Fact]
    public async Task The_dialog_header_follows_the_title_and_holds_the_body()
    {
        await Headless.Dispatch(() =>
        {
            var dialog = new CoreDialog();
            dialog.Title = "Preferences";

            Assert.Equal("Preferences", dialog.Header.Title);
            Assert.Equal(global::Avalonia.Controls.WindowDecorations.None, dialog.WindowDecorations);

            var controls = Assert.IsType<WindowControls>(dialog.Header.EndContent);
            Assert.False(controls.IsMinimizeVisible);
            Assert.False(controls.IsMaximizeVisible);
            Assert.True(controls.IsCloseVisible);

            controls.IsMinimizeVisible = true;
            controls.IsMaximizeVisible = true;
            controls.IsCloseVisible = false;
            Assert.True(controls.IsMinimizeVisible);
            Assert.True(controls.IsMaximizeVisible);
            Assert.False(controls.IsCloseVisible);

            var body = new TextBlock { Text = "body" };
            dialog.DialogContent = body;
            Assert.Same(body, dialog.DialogContent);
        });
    }

    [Fact]
    public async Task A_dialog_header_does_not_maximize_on_a_double_click()
    {
        await Headless.Dispatch(() =>
        {
            var dialog = new CoreDialog { Width = 400 };
            dialog.Show();
            dialog.UpdateLayout();

            var point = new Point(10, 10);
            dialog.MouseDown(point, MouseButton.Left);
            dialog.MouseUp(point, MouseButton.Left);
            dialog.MouseDown(point, MouseButton.Left);
            dialog.MouseUp(point, MouseButton.Left);

            Assert.Equal(WindowState.Normal, dialog.WindowState);

            dialog.Close();
        });
    }

    [Fact]
    public async Task The_dialog_header_follows_a_live_theme_change()
    {
        await Headless.Dispatch(() =>
        {
            var dialog = new CoreDialog();
            dialog.Show();
            dialog.UpdateLayout();

            var before = Assert.IsType<SolidColorBrush>(dialog.Header.Background).Color;
            var current = Application.Current!.ActualThemeVariant;
            Application.Current!.RequestedThemeVariant =
                current == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
            dialog.UpdateLayout();

            var after = Assert.IsType<SolidColorBrush>(dialog.Header.Background).Color;

            Assert.NotEqual(before, after);

            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            dialog.Close();
        });
    }

    [Fact]
    public void The_legal_notice_has_the_copyright_and_the_lgpl_terms()
    {
        var notice = LegalNotice.Lgpl(2026, "Younes BENMOUSSA", "younes.benmoussa@pm.me");

        Assert.Contains("Copyright (C) 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>", notice, StringComparison.Ordinal);
        Assert.Contains("GNU Lesser General Public License", notice, StringComparison.Ordinal);
        Assert.DoesNotContain("GNU General Public License", notice, StringComparison.Ordinal);
        Assert.Equal("GNU LGPL v3.0 or later", LegalNotice.LicenseName);
        Assert.Equal(new Uri("https://www.gnu.org/licenses/lgpl-3.0.html"), LegalNotice.LicenseUrl);
        Assert.Throws<ArgumentNullException>(() => LegalNotice.Copyright(2026, null!, "x"));
        Assert.Throws<ArgumentNullException>(() => LegalNotice.Lgpl(2026, "x", null!));
    }

    [Fact]
    public void The_gpl_entry_point_keeps_working_as_the_lgpl_alias()
    {
        Assert.Equal(
            LegalNotice.Lgpl(2026, "Holder", "holder@example.test"),
            LegalNotice.Gpl(2026, "Holder", "holder@example.test"));
        Assert.Throws<ArgumentNullException>(() => LegalNotice.Gpl(2026, "x", null!));
    }

    [Fact]
    public async Task The_about_dialog_renders_the_identity_and_links()
    {
        await Headless.Dispatch(() =>
        {
            var info = new AboutInfo(
                "Core Widget",
                "2.0.0",
                "NOTICE TEXT",
                new Uri("https://example.test/source"));

            var dialog = new AboutDialog(info);

            Assert.Equal("About Core Widget", dialog.Title);
            Assert.Equal("About Core Widget", dialog.Header.Title);
            Assert.Equal("NOTICE TEXT", dialog.NoticeText.Text);
            Assert.Equal(new Uri("https://www.gnu.org/licenses/lgpl-3.0.html"), dialog.LicenseLink.NavigateUri);
            Assert.Equal(new Uri("https://example.test/source"), dialog.SourceLink.NavigateUri);

            Assert.Throws<ArgumentNullException>(() => new AboutDialog(null!));
        });
    }

    [Theory]
    [InlineData(ThemePreference.System)]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public async Task The_preferences_dialog_reflects_and_drives_the_service(ThemePreference theme)
    {
        await Headless.Dispatch(() =>
        {
            var service = new FakeAppearanceService { Current = new AppearanceSettings(theme, "AccentBlue", true) };
            var dialog = new PreferencesDialog(service, new PreferencesSection("Advanced", new TextBlock { Text = "extra" }));

            Assert.NotNull(dialog.DialogContent);
            Assert.Equal(6, dialog.AccentButtons.Count);
            Assert.True(dialog.RememberCheck.IsChecked);

            var expected = theme switch
            {
                ThemePreference.Light => dialog.LightRadio,
                ThemePreference.Dark => dialog.DarkRadio,
                _ => dialog.SystemRadio,
            };
            Assert.True(expected.IsChecked);
            Assert.Null(service.LastTheme);

            // The constructor must not persist the initial radio; only a real
            // operator change drives the service. Select the other variant.
            var changed = theme == ThemePreference.Light ? dialog.DarkRadio : dialog.LightRadio;
            changed.IsChecked = true;
            Assert.Equal(
                theme == ThemePreference.Light ? ThemePreference.Dark : ThemePreference.Light,
                service.LastTheme);
        });
    }

    [Fact]
    public async Task The_remember_checkbox_drives_the_service()
    {
        await Headless.Dispatch(() =>
        {
            var service = new FakeAppearanceService { Current = AppearanceSettings.Default };
            var dialog = new PreferencesDialog(service);

            dialog.RememberCheck.IsChecked = true;

            Assert.True(service.LastRemember);
        });
    }

    [Fact]
    public async Task Clicking_an_accent_swatch_drives_the_service()
    {
        var chosen = await Headless.Dispatch(() =>
        {
            var service = new FakeAppearanceService { Current = AppearanceSettings.Default };
            var dialog = new PreferencesDialog(service);
            dialog.Show();
            dialog.UpdateLayout();

            var button = dialog.AccentButtons[1].Button;
            var centre = button.TranslatePoint(
                new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), dialog);
            Assert.NotNull(centre);
            dialog.MouseDown(centre.Value, MouseButton.Left);
            dialog.MouseUp(centre.Value, MouseButton.Left);

            var accent = service.LastAccent;
            dialog.Close();

            return accent;
        });

        Assert.Equal("AccentBlue", chosen);
    }

    [Fact]
    public async Task The_preferences_dialog_validates_its_arguments()
    {
        await Headless.Dispatch(() =>
        {
            var service = new FakeAppearanceService();

            Assert.Throws<ArgumentNullException>(() => new PreferencesDialog(null!));
            Assert.Throws<ArgumentNullException>(() => new PreferencesDialog(service, null!));
        });
    }

    private sealed class FakeAppearanceService : IAppearanceService
    {
        public AppearanceSettings Current { get; set; } = AppearanceSettings.Default;

        public ThemePreference? LastTheme { get; private set; }

        public string? LastAccent { get; private set; }

        public bool? LastRemember { get; private set; }

        public void ChooseTheme(ThemePreference theme)
        {
            LastTheme = theme;
            Current = Current with { Theme = theme };
        }

        public void ChooseAccent(string key)
        {
            LastAccent = key;
            Current = Current with { AccentKey = key };
        }

        public void SetRemember(bool remember)
        {
            LastRemember = remember;
            Current = Current with { Remember = remember };
        }

        public void Apply()
        {
        }
    }
}
