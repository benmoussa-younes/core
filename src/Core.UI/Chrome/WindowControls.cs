// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Core.UI.Adwaita;

namespace Core.UI.Chrome;

/// <summary>
/// The window buttons of the header bar: minimise, maximise/restore and close.
/// </summary>
/// <remarks>
/// The close button does not close the window directly: it raises
/// <see cref="CloseRequested"/> so the shell can hide the window to the tray
/// instead. When nothing handles the event, the button closes the window. The
/// maximise glyph follows the window state.
/// <para>
/// The glyphs are vector shapes drawn symmetrically around the same centre, not
/// text characters: the Unicode dash, square and cross sit at different heights
/// for their font metrics, so a text glyph row never lines up.
/// </para>
/// </remarks>
internal class WindowControls : StackPanel
{
    private readonly Button _minimize;
    private readonly Button _maximize;
    private readonly Button _close;
    private Window? _window;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowControls"/> class.
    /// </summary>
    public WindowControls()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 0;

        _minimize = CreateButton(MinimizeGlyph(), WindowDecorationsElementRole.MinimizeButton, "Minimize");
        _maximize = CreateButton(MaximizedGlyph(), WindowDecorationsElementRole.MaximizeButton, "Maximize");
        _close = CreateButton(CloseGlyph(), WindowDecorationsElementRole.CloseButton, "Close");
        _close.Name = "CloseButton";

        _minimize.Click += (_, _) => Minimize();
        _maximize.Click += (_, _) => ToggleMaximize();
        _close.Click += (_, _) => RequestClose();

        Children.Add(_minimize);
        Children.Add(_maximize);
        Children.Add(_close);
    }

    /// <summary>Raised when the operator presses close; the shell decides what closing means.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Gets or sets a value indicating whether the minimise button is shown.</summary>
    public bool IsMinimizeVisible
    {
        get => _minimize.IsVisible;
        set => _minimize.IsVisible = value;
    }

    /// <summary>Gets or sets a value indicating whether the maximise button is shown.</summary>
    public bool IsMaximizeVisible
    {
        get => _maximize.IsVisible;
        set => _maximize.IsVisible = value;
    }

    /// <summary>Gets or sets a value indicating whether the close button is shown.</summary>
    public bool IsCloseVisible
    {
        get => _close.IsVisible;
        set => _close.IsVisible = value;
    }

    internal Button MinimizeButton => _minimize;

    internal Button MaximizeButton => _maximize;

    internal Button CloseButton => _close;

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _window = TopLevel.GetTopLevel(this) as Window;

        if (_window is not null)
        {
            _window.PropertyChanged += OnWindowPropertyChanged;
        }

        UpdateMaximizeGlyph();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
        }

        _window = null;
    }

    internal void Minimize()
    {
        if (_window is not null)
        {
            _window.WindowState = WindowState.Minimized;
        }
    }

    internal void ToggleMaximize()
    {
        if (_window is null)
        {
            return;
        }

        _window.WindowState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    internal void RequestClose()
    {
        if (CloseRequested is { } handler)
        {
            handler(this, EventArgs.Empty);
            return;
        }

        _window?.Close();
    }

    private static Button CreateButton(Panel glyph, WindowDecorationsElementRole role, string toolTip)
    {
        var button = new Button { Content = glyph, Classes = { "caption" } };
        ToolTip.SetTip(button, toolTip);
        AutomationProperties.SetName(button, toolTip);

        // The platform must treat these as decoration controls, not as the title
        // bar: otherwise a press is consumed as a window drag and the button never
        // sees the click.
        WindowDecorationProperties.SetElementRole(button, role);
        return button;
    }

    private static Panel MinimizeGlyph()
    {
        return Box(new Border
        {
            Width = 10,
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Background = Ink(),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        });
    }

    private static Panel MaximizedGlyph()
    {
        return Box(new Border
        {
            Width = 9,
            Height = 9,
            BorderThickness = new Thickness(1.5),
            BorderBrush = Ink(),
            CornerRadius = new CornerRadius(1),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        });
    }

    private static Panel RestoredGlyph()
    {
        var panel = Box(null);

        panel.Children.Add(new Border
        {
            Width = 7,
            Height = 7,
            BorderThickness = new Thickness(1.5),
            BorderBrush = Ink(),
            CornerRadius = new CornerRadius(1),
            Margin = new Thickness(0, 1, 1, 0),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        });

        panel.Children.Add(new Border
        {
            Width = 7,
            Height = 7,
            BorderThickness = new Thickness(1.5),
            BorderBrush = Ink(),
            CornerRadius = new CornerRadius(1),
            Margin = new Thickness(1, 0, 0, 1),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom,
        });

        return panel;
    }

    private static Panel CloseGlyph()
    {
        var panel = Box(null);

        panel.Children.Add(Bar(45));
        panel.Children.Add(Bar(-45));

        return panel;

        static Border Bar(double angle)
        {
            return new Border
            {
                Width = 11,
                Height = 2,
                CornerRadius = new CornerRadius(1),
                Background = Ink(),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                RenderTransform = new RotateTransform(angle),
            };
        }
    }

    private static Panel Box(Control? glyph)
    {
        // A transparent background makes the whole glyph box hit-testable: an
        // outline glyph (maximise) has no fill, so without it the centre of the
        // button is dead and a click there reaches nothing.
        var panel = new Panel
        {
            Width = 12,
            Height = 12,
            Background = Brushes.Transparent,
        };

        if (glyph is not null)
        {
            panel.Children.Add(glyph);
        }

        return panel;
    }

    private static IBrush Ink()
    {
        return ThemeBrushes.Resolve("ForegroundBrush");
    }

    /// <summary>Re-creates the glyphs so they follow a live theme switch.</summary>
    internal void ApplyTheme()
    {
        _minimize.Content = MinimizeGlyph();
        UpdateMaximizeGlyph();
        _close.Content = CloseGlyph();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            UpdateMaximizeGlyph();
        }
    }

    private void UpdateMaximizeGlyph()
    {
        _maximize.Content = _window?.WindowState == WindowState.Maximized
            ? RestoredGlyph()
            : MaximizedGlyph();
    }
}
