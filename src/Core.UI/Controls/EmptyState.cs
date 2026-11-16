// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Core.UI.Controls;

/// <summary>
/// A centred placeholder for an empty list or table: a heading that says what is
/// missing and a muted detail that says what to do about it, with an optional
/// action button.
/// </summary>
public sealed class EmptyState : UserControl
{
    private readonly TextBlock _heading;
    private readonly TextBlock _detail;
    private readonly Button _action;

    /// <summary>Initializes the placeholder in its cleared state.</summary>
    public EmptyState()
    {
        _heading = new TextBlock
        {
            Classes = { "heading" },
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        _detail = new TextBlock
        {
            Classes = { "muted" },
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };

        _action = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
            IsVisible = false,
        };
        _action.Click += (_, _) => ActionRequested?.Invoke();

        Content = new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _heading, _detail, _action },
        };
    }

    /// <summary>Raised when the operator activates the optional action.</summary>
    public event Action? ActionRequested;

    /// <summary>Gets the heading line.</summary>
    public string Heading => _heading.Text ?? string.Empty;

    /// <summary>Gets the detail line.</summary>
    public string Detail => _detail.Text ?? string.Empty;

    /// <summary>Gets the action label, or an empty string when there is no action.</summary>
    public string ActionLabel => _action.IsVisible ? _action.Content?.ToString() ?? string.Empty : string.Empty;

    /// <summary>Gets the action button, for tests.</summary>
    internal Button ActionButton => _action;

    /// <summary>Sets the two lines of the placeholder and an optional action.</summary>
    /// <param name="heading">What is missing.</param>
    /// <param name="detail">What the operator can do about it.</param>
    /// <param name="actionLabel">The action button label, or empty for none.</param>
    /// <exception cref="ArgumentNullException">A string argument is <see langword="null"/>.</exception>
    public void Configure(string heading, string detail, string actionLabel = "")
    {
        ArgumentNullException.ThrowIfNull(heading);
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(actionLabel);

        _heading.Text = heading;
        _detail.Text = detail;
        _action.Content = actionLabel;
        _action.IsVisible = actionLabel.Length > 0;
    }
}
