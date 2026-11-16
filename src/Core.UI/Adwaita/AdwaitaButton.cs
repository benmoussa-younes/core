// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Core.UI.Adwaita;

/// <summary>
/// A flat Adwaita button sized for a header bar: transparent, borderless, 34
/// pixels square with the 6-pixel corner radius, as the GNOME HIG asks for the
/// toolbar style.
/// </summary>
internal class AdwaitaButton : Button
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AdwaitaButton"/> class.
    /// </summary>
    public AdwaitaButton()
    {
        MinWidth = GnomeMetrics.ButtonSize;
        MinHeight = GnomeMetrics.ButtonSize;
        Padding = new Thickness(GnomeMetrics.HeaderBarSpacing);
        CornerRadius = new CornerRadius(GnomeMetrics.ButtonCornerRadius);
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Foreground = ThemeBrushes.Resolve("ForegroundBrush");
        HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
        VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
    }
}

