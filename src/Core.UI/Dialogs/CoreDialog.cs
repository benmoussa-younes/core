// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Core.UI.Adwaita;
using Core.UI.Chrome;

namespace Core.UI.Dialogs;

/// <summary>
/// The content-dialog base: a client-side-decorated window with the same
/// <see cref="HeaderBar"/> chrome as the main window, a close-only set of window
/// controls and a body the product fills. A product derives from it instead of
/// rebuilding the chrome for every About, Preferences or settings dialog.
/// </summary>
public class CoreDialog : AdwaitaWindow
{
    private readonly ContentPresenter _body = new();
    private readonly WindowControls _controls = new();

    /// <summary>Initializes a new instance of the <see cref="CoreDialog"/> class.</summary>
    public CoreDialog()
    {
        // The dialog always carries the shell's own title bar, like the main
        // window, so it does not get a second OS decoration on top of the header.
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = GnomeMetrics.HeaderBarHeight;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Width = 600;

        _controls.IsMinimizeVisible = false;
        _controls.IsMaximizeVisible = false;
        _controls.CloseRequested += (_, _) => Close();

        Header = new HeaderBar { EndContent = _controls };

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(Header, 0);
        Grid.SetRow(_body, 1);
        grid.Children.Add(Header);
        grid.Children.Add(_body);

        var frame = new Border { BorderThickness = new Thickness(1), Child = grid };
        ThemeResources.Bind(frame, Border.BorderBrushProperty, "BorderBrush");
        ThemeResources.Bind(frame, Border.BackgroundProperty, "BackgroundBrush");
        Content = frame;

        // The main window re-applies its own header on a live theme switch; a
        // dialog is a separate window and must do the same for its header and
        // caption buttons.
        ActualThemeVariantChanged += (_, _) =>
        {
            Header.ApplyTheme();
            _controls.ApplyTheme();
        };
    }

    /// <summary>Gets the dialog header bar.</summary>
    public HeaderBar Header { get; }

    /// <summary>Gets or sets the body shown under the header.</summary>
    public Control? DialogContent
    {
        get => _body.Content as Control;
        set => _body.Content = value;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // The header follows the window title, so a dialog only names its title.
        if (change.Property == TitleProperty)
        {
            Header.Title = Title;
        }
    }
}
