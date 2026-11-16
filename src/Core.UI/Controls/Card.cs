// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Controls;

namespace Core.UI.Controls;

/// <summary>
/// A raised content surface: the card a form, a settings group or a detail block
/// sits on. The look comes from the shared <c>Border.card</c> style, so a card
/// follows the theme.
/// </summary>
public sealed class Card : Border
{
    /// <summary>Initializes an empty card.</summary>
    public Card()
    {
        Classes.Add("card");
    }

    /// <summary>Initializes a card around one control.</summary>
    /// <param name="content">The card content.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    public Card(Control content)
        : this()
    {
        ArgumentNullException.ThrowIfNull(content);

        Child = content;
    }

    /// <summary>Initializes a card with a heading and a stack of rows.</summary>
    /// <param name="heading">The heading shown at the top.</param>
    /// <param name="rows">The rows, stacked with the shared spacing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="heading"/> or <paramref name="rows"/> is <see langword="null"/>.</exception>
    public Card(string heading, params Control[] rows)
        : this()
    {
        ArgumentNullException.ThrowIfNull(heading);
        ArgumentNullException.ThrowIfNull(rows);

        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = heading, Classes = { "heading" } });

        foreach (var row in rows)
        {
            stack.Children.Add(row);
        }

        Child = stack;
    }
}
