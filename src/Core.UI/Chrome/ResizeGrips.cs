// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Core.UI.Chrome;

/// <summary>
/// The resize handles of a client-side-decorated window: eight thin transparent
/// strips along the border that begin a platform resize drag with the matching
/// cursor.
/// </summary>
/// <remarks>
/// The shell hides the native decorations, so the platform draws no resize
/// border. The strips only cover the outermost pixels (the middle is left empty
/// and is not hit-testable), so the content and the header bar keep receiving
/// their input.
/// </remarks>
internal class ResizeGrips : Grid
{
    private const double Grip = 5;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResizeGrips"/> class.
    /// </summary>
    public ResizeGrips()
    {
        ColumnDefinitions = new ColumnDefinitions($"{Grip},*,{Grip}");
        RowDefinitions = new RowDefinitions($"{Grip},*,{Grip}");

        Add(WindowEdge.NorthWest, 0, 0, StandardCursorType.TopLeftCorner);
        Add(WindowEdge.North, 0, 1, StandardCursorType.TopSide);
        Add(WindowEdge.NorthEast, 0, 2, StandardCursorType.TopRightCorner);
        Add(WindowEdge.West, 1, 0, StandardCursorType.LeftSide);
        Add(WindowEdge.East, 1, 2, StandardCursorType.RightSide);
        Add(WindowEdge.SouthWest, 2, 0, StandardCursorType.BottomLeftCorner);
        Add(WindowEdge.South, 2, 1, StandardCursorType.BottomSide);
        Add(WindowEdge.SouthEast, 2, 2, StandardCursorType.BottomRightCorner);
    }

    private void Add(WindowEdge edge, int row, int column, StandardCursorType cursor)
    {
        var grip = new Border
        {
            Background = Brushes.Transparent,
            Cursor = new Cursor(cursor),
        };

        grip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (TopLevel.GetTopLevel(this) is Window window)
            {
                window.BeginResizeDrag(edge, e);
                e.Handled = true;
            }
        };

        SetRow(grip, row);
        SetColumn(grip, column);
        Children.Add(grip);
    }
}
