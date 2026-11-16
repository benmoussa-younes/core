// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Core.UI.Adwaita;
using Core.UI.Dialogs;
using Core.Mvp.Services;

namespace Core.UI.Preferences;

/// <summary>
/// The shared Preferences dialog: the appearance section (theme variant, accent
/// palette, remember) driven through <see cref="IAppearanceService"/>, plus any
/// product-specific <see cref="PreferencesSection"/>. A product adds its own
/// settings without rebuilding the appearance UI.
/// </summary>
public sealed class PreferencesDialog : CoreDialog
{
    private readonly IAppearanceService _service;
    private readonly List<(string Key, Button Button)> _accentButtons = [];
    private bool _initializing = true;

    /// <summary>Initializes the dialog for an appearance service and extra sections.</summary>
    /// <param name="service">The appearance service the dialog drives.</param>
    /// <param name="sections">The product-specific sections, appended after the appearance section.</param>
    /// <exception cref="ArgumentNullException"><paramref name="service"/> or <paramref name="sections"/> is <see langword="null"/>.</exception>
    public PreferencesDialog(IAppearanceService service, params PreferencesSection[] sections)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(sections);

        _service = service;

        Title = "Preferences";
        Width = 460;

        SystemRadio = ThemeRadio("System", ThemePreference.System);
        LightRadio = ThemeRadio("Light", ThemePreference.Light);
        DarkRadio = ThemeRadio("Dark", ThemePreference.Dark);

        RadioFor(service.Current.Theme).IsChecked = true;
        _initializing = false;

        RememberCheck = new CheckBox
        {
            Content = "Remember this choice",
            IsChecked = service.Current.Remember,
        };
        RememberCheck.IsCheckedChanged += (_, _) => _service.SetRemember(RememberCheck.IsChecked == true);

        var themeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children = { SystemRadio, LightRadio, DarkRadio },
        };

        var swatches = new WrapPanel();
        foreach (var entry in AccentPalette.Catalog)
        {
            var button = AccentButton(entry);
            _accentButtons.Add((entry.Key, button));
            swatches.Children.Add(button);
        }

        var close = new Button
        {
            Content = "Close",
            Classes = { "flat" },
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        close.Click += (_, _) => Close();

        var card = new StackPanel
        {
            Spacing = 18,
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = "Preferences", Classes = { "heading" } },
                Section("Appearance", themeRow),
                Section("Accent colour", swatches),
                RememberCheck,
            },
        };

        foreach (var section in sections)
        {
            card.Children.Add(Section(section.Title, section.Content));
        }

        card.Children.Add(close);
        DialogContent = card;
    }

    /// <summary>Gets the system-theme radio, for tests.</summary>
    internal RadioButton SystemRadio { get; }

    /// <summary>Gets the light-theme radio, for tests.</summary>
    internal RadioButton LightRadio { get; }

    /// <summary>Gets the dark-theme radio, for tests.</summary>
    internal RadioButton DarkRadio { get; }

    /// <summary>Gets the remember checkbox, for tests.</summary>
    internal CheckBox RememberCheck { get; }

    /// <summary>Gets the accent swatches in palette order, for tests.</summary>
    internal IReadOnlyList<(string Key, Button Button)> AccentButtons => _accentButtons;

    private RadioButton ThemeRadio(string label, ThemePreference theme)
    {
        var radio = new RadioButton { Content = label, GroupName = "AppearanceTheme" };
        radio.IsCheckedChanged += (_, _) =>
        {
            if (!_initializing && radio.IsChecked == true)
            {
                _service.ChooseTheme(theme);
            }
        };

        return radio;
    }

    private RadioButton RadioFor(ThemePreference theme) => theme switch
    {
        ThemePreference.Light => LightRadio,
        ThemePreference.Dark => DarkRadio,
        _ => SystemRadio,
    };

    private Button AccentButton(AccentDefinition entry)
    {
        var dot = new Ellipse
        {
            Width = 18,
            Height = 18,
            StrokeThickness = 1,
            Fill = new SolidColorBrush(entry.Color),
        };
        ThemeResources.Bind(dot, Shape.StrokeProperty, "BorderBrush");

        var button = new Button
        {
            Content = dot,
            Classes = { "flat" },
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(18),
        };
        ToolTip.SetTip(button, entry.Label);
        button.Click += (_, _) => _service.ChooseAccent(entry.Key);

        return button;
    }

    private static StackPanel Section(string title, Control content) => new()
    {
        Spacing = 10,
        Children =
        {
            new TextBlock { Text = title, Classes = { "muted" } },
            content,
        },
    };
}
