// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Core.UI.Dialogs;

/// <summary>
/// The shared About dialog: the product identity, its version, the legal notice and
/// the links to the licence and the corresponding source, on the shell's
/// <see cref="CoreDialog"/> chrome.
/// </summary>
public sealed class AboutDialog : CoreDialog
{
    /// <summary>Initializes the dialog from a product's identity.</summary>
    /// <param name="info">The product identity.</param>
    /// <exception cref="ArgumentNullException"><paramref name="info"/> is <see langword="null"/>.</exception>
    public AboutDialog(AboutInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        Title = string.Concat("About ", info.ProductName);
        Header.Icon = info.Icon;
        Width = 600;

        var identity = new TextBlock { Text = info.ProductName, Classes = { "heading" } };
        var version = new TextBlock { Text = string.Concat("Version ", info.Version), Classes = { "muted" } };
        NoticeText = new TextBlock
        {
            Text = info.Notice,
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" },
        };

        LicenseLink = new HyperlinkButton
        {
            Content = info.LicenseName,
            NavigateUri = info.LicenseUrl ?? LegalNotice.LicenseUrl,
            Padding = new Thickness(0),
        };
        SourceLink = new HyperlinkButton
        {
            Content = "Corresponding source",
            NavigateUri = info.SourceUrl,
            Padding = new Thickness(0),
        };

        var links = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children = { LicenseLink, SourceLink },
        };

        var close = new Button
        {
            Content = "Close",
            Classes = { "flat" },
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        close.Click += (_, _) => Close();

        DialogContent = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(20),
            Children = { identity, version, NoticeText, links, close },
        };
    }

    /// <summary>Gets the notice text, for tests.</summary>
    internal TextBlock NoticeText { get; }

    /// <summary>Gets the licence hyperlink, for tests.</summary>
    internal HyperlinkButton LicenseLink { get; }

    /// <summary>Gets the corresponding-source hyperlink, for tests.</summary>
    internal HyperlinkButton SourceLink { get; }
}
