// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Core.Diagnostics;
using Core.Platform;
using Core.UI.Hosting;

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
    /// <param name="links">
    /// The URL launcher, or <see langword="null"/> to let the platform launcher handle
    /// the click. When supplied, every link goes through its scheme validation, so a
    /// <c>file:</c> or <c>smb:</c> URL never opens.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="info"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A link is not an absolute URI.</exception>
    public AboutDialog(AboutInfo info, ISystemLinkLauncher? links = null)
    {
        ArgumentNullException.ThrowIfNull(info);

        // WHY: a relative URI throws from AbsoluteUri inside the click handler, where
        // the operator sees nothing; the construction is the right place to fail.
        var licenseUrl = info.LicenseUrl ?? LegalNotice.LicenseUrl;

        if (!info.SourceUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("The source URL must be absolute.", nameof(info));
        }

        if (licenseUrl is { IsAbsoluteUri: false })
        {
            throw new ArgumentException("The licence URL must be absolute.", nameof(info));
        }

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

        LicenseLink = Link(info.LicenseName, licenseUrl, links);
        SourceLink = Link("Corresponding source", info.SourceUrl, links);

        var linkRow = new StackPanel
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
            Children = { identity, version, NoticeText, linkRow, close },
        };
    }

    // A link with a launcher is a plain click: the port decides whether the scheme
    // is allowed, so the platform launcher never sees an unvalidated URL.
    private static HyperlinkButton Link(string content, Uri target, ISystemLinkLauncher? links)
    {
        var link = new HyperlinkButton
        {
            Content = content,
            NavigateUri = links is null ? target : null,
            Padding = new Thickness(0),
        };

        if (links is not null)
        {
            link.Click += async (_, _) =>
            {
                var opened = await links.OpenAsync(target.AbsoluteUri).ConfigureAwait(true);

                if (opened.IsFailure)
                {
                    // WHY: the operator sees nothing when a refused scheme fails, so a
                    // refused or failed launch must at least leave a trace.
                    (Application.Current as CoreApplication)?.Diagnostics?.Logger.Log(
                        LogLevel.Warning,
                        string.Concat("The link could not open: ", opened.Error.Code, "."));
                }
            };
        }

        return link;
    }

    /// <summary>Gets the notice text, for tests.</summary>
    internal TextBlock NoticeText { get; }

    /// <summary>Gets the licence hyperlink, for tests.</summary>
    internal HyperlinkButton LicenseLink { get; }

    /// <summary>Gets the corresponding-source hyperlink, for tests.</summary>
    internal HyperlinkButton SourceLink { get; }
}
