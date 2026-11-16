// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.UI.Adwaita;

/// <summary>
/// The GNOME Human Interface Guidelines metrics the header bar uses.
/// </summary>
public static class GnomeMetrics
{
    /// <summary>
    /// The minimum header bar height in device-independent pixels (48), matching
    /// the window's extended title-bar strip from the GNOME HIG.
    /// </summary>
    public const double HeaderBarHeight = 48;

    /// <summary>
    /// The spacing between the controls inside a header bar in
    /// device-independent pixels (6).
    /// </summary>
    public const double HeaderBarSpacing = 6;

    /// <summary>
    /// The horizontal padding inside a header bar in device-independent pixels
    /// (6).
    /// </summary>
    public const double HeaderBarHorizontalPadding = 6;

    /// <summary>
    /// The side of a square header bar button in device-independent pixels (34).
    /// </summary>
    public const double ButtonSize = 34;

    /// <summary>
    /// The corner radius of a header bar button in device-independent pixels (6).
    /// </summary>
    public const double ButtonCornerRadius = 6;
}
