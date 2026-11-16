// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Core.UI.Controls;

/// <summary>
/// A labelled field row: a muted caption in a fixed column and the field in the
/// remaining width. It is the two-column layout a settings or editor form repeats.
/// </summary>
public sealed class FieldRow : Grid
{
    /// <summary>The default caption column width.</summary>
    public const double DefaultLabelWidth = 160;

    /// <summary>Initializes a row with the default caption width.</summary>
    /// <param name="label">The caption.</param>
    /// <param name="field">The field control.</param>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> or <paramref name="field"/> is <see langword="null"/>.</exception>
    public FieldRow(string label, Control field)
        : this(label, field, DefaultLabelWidth)
    {
    }

    /// <summary>Initializes a row with an explicit caption width.</summary>
    /// <param name="label">The caption.</param>
    /// <param name="field">The field control.</param>
    /// <param name="labelWidth">The caption column width; must be positive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> or <paramref name="field"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="labelWidth"/> is not positive.</exception>
    public FieldRow(string label, Control field, double labelWidth)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(labelWidth);

        ColumnDefinitions = new ColumnDefinitions
        {
            new ColumnDefinition(new GridLength(labelWidth)),
            new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
        };

        var caption = new TextBlock
        {
            Text = label,
            Classes = { "muted" },
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        SetColumn(caption, 0);
        SetColumn(field, 1);

        Children.Add(caption);
        Children.Add(field);
    }
}
