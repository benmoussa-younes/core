// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Core.UI.Adwaita;
using Core.Mvp.Services;

namespace Core.UI.Controls;

/// <summary>
/// A small state token: a coloured dot and the state word, toned by a
/// <see cref="MessageSeverity"/> so it follows the same colour language as the
/// rest of the window (success, warning, error, muted).
/// </summary>
public sealed class StatusChip : UserControl
{
    private readonly Ellipse _dot;
    private readonly TextBlock _text;

    /// <summary>Initializes the chip in its cleared state.</summary>
    public StatusChip()
    {
        _dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
        _text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };

        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _dot, _text },
        };

        Clear();
    }

    /// <summary>Gets the state the chip currently shows.</summary>
    public string State { get; private set; } = string.Empty;

    /// <summary>Gets the dot, for tests.</summary>
    internal Ellipse Dot => _dot;

    /// <summary>Shows a state word; an empty word hides the chip.</summary>
    /// <param name="state">The state word.</param>
    /// <param name="severity">The tone the dot takes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is <see langword="null"/>.</exception>
    public void Show(string state, MessageSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(state);

        State = state;
        _text.Text = state;

        // A dynamic resource, so the dot follows a live light/dark switch.
        ThemeResources.Bind(_dot, Shape.FillProperty, severity switch
        {
            MessageSeverity.Success => "SuccessBrush",
            MessageSeverity.Warning => "WarningBrush",
            MessageSeverity.Error => "ErrorBrush",
            _ => "MutedForegroundBrush",
        });

        IsVisible = state.Length > 0;
    }

    /// <summary>Hides the chip and drops its state.</summary>
    public void Clear()
    {
        State = string.Empty;
        _text.Text = string.Empty;
        IsVisible = false;
    }
}
