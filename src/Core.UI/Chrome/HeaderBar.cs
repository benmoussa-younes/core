// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Core.UI.Adwaita;

namespace Core.UI.Chrome;

/// <summary>
/// A GNOME header bar: window actions at the start, the identity (icon, title and
/// a contextual subtitle) in the centre, and the primary menu with the window
/// controls at the end.
/// </summary>
/// <remarks>
/// The bar is composed in code instead of a control template, so it renders
/// without a XAML theme and stays trimmable. The bar handles the window move and
/// double-click maximise itself, so a press on a child control (the back button,
/// the menu) is not swallowed by a drag and the whole bar is not advertised to the
/// platform as the native title bar. Its height matches
/// <see cref="GnomeMetrics.HeaderBarHeight"/> so the window's extended client area
/// can reserve exactly that strip.
/// </remarks>
public class HeaderBar : Border
{
    /// <summary>Identifies the <see cref="Title"/> property.</summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<HeaderBar, string?>(nameof(Title));

    /// <summary>Identifies the <see cref="Subtitle"/> property.</summary>
    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<HeaderBar, string?>(nameof(Subtitle));

    /// <summary>Identifies the <see cref="StartContent"/> property.</summary>
    public static readonly StyledProperty<Control?> StartContentProperty =
        AvaloniaProperty.Register<HeaderBar, Control?>(nameof(StartContent));

    /// <summary>Identifies the <see cref="CenterContent"/> property.</summary>
    public static readonly StyledProperty<Control?> CenterContentProperty =
        AvaloniaProperty.Register<HeaderBar, Control?>(nameof(CenterContent));

    /// <summary>Identifies the <see cref="EndContent"/> property.</summary>
    public static readonly StyledProperty<Control?> EndContentProperty =
        AvaloniaProperty.Register<HeaderBar, Control?>(nameof(EndContent));

    /// <summary>Identifies the <see cref="IsDirty"/> property.</summary>
    public static readonly StyledProperty<bool> IsDirtyProperty =
        AvaloniaProperty.Register<HeaderBar, bool>(nameof(IsDirty));

    /// <summary>Identifies the <see cref="Icon"/> property.</summary>
    public static readonly StyledProperty<IImage?> IconProperty =
        AvaloniaProperty.Register<HeaderBar, IImage?>(nameof(Icon));

    private readonly StackPanel _start = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = GnomeMetrics.HeaderBarSpacing,
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
    };

    private readonly StackPanel _end = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = GnomeMetrics.HeaderBarSpacing,
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
    };

    private readonly StackPanel _center = new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 0,
        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
    };

    private readonly Image _icon = new()
    {
        Width = 20,
        Height = 20,
        Margin = new Thickness(0, 0, 8, 0),
        IsVisible = false,
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
    };

    private readonly Border _dirty = new()
    {
        Width = 8,
        Height = 8,
        CornerRadius = new CornerRadius(4),
        Background = ThemeBrushes.Resolve("PrimaryBrush"),
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        IsVisible = false,
    };

    private readonly StackPanel _titles = new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 0,
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
    };

    private readonly StackPanel _identity = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 0,
        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
    };

    private readonly TextBlock _title = new()
    {
        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Classes = { "heading" },
    };

    private readonly TextBlock _subtitle = new()
    {
        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        IsVisible = false,
        Classes = { "subtitle" },
    };

    private readonly ContentPresenter _centerContent = new()
    {
        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        IsVisible = false,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="HeaderBar"/> class.
    /// </summary>
    public HeaderBar()
    {
        MinHeight = GnomeMetrics.HeaderBarHeight;
        Padding = new Thickness(GnomeMetrics.HeaderBarHorizontalPadding, 0);
        Background = ThemeBrushes.Resolve("SurfaceBrush");
        BorderBrush = ThemeBrushes.Resolve("BorderBrush");
        BorderThickness = new Thickness(0, 0, 0, 1);

        _titles.Children.Add(_title);
        _titles.Children.Add(_subtitle);
        _identity.Children.Add(_icon);
        _identity.Children.Add(_dirty);
        _identity.Children.Add(_titles);
        _center.Children.Add(_identity);
        _center.Children.Add(_centerContent);

        // One cell, three overlaid zones: the centre is centred on the bar, not on
        // the space left between the start and end content (a GNOME header bar
        // centres the title on the window).
        _start.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
        _end.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right;
        _center.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;

        var grid = new Grid();

        grid.Children.Add(_start);
        grid.Children.Add(_center);
        grid.Children.Add(_end);

        Child = grid;
    }

    /// <summary>
    /// Gets or sets the title shown in the centre of the bar.
    /// </summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the contextual line under the title; it is hidden when empty.
    /// </summary>
    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the controls at the start of the bar (left in left-to-right
    /// locales): the back affordance and the primary actions.
    /// </summary>
    public Control? StartContent
    {
        get => GetValue(StartContentProperty);
        set => SetValue(StartContentProperty, value);
    }

    /// <summary>
    /// Gets or sets a control shown in the centre instead of the title text (for
    /// example a view switcher); when set, the title and subtitle are hidden.
    /// </summary>
    public Control? CenterContent
    {
        get => GetValue(CenterContentProperty);
        set => SetValue(CenterContentProperty, value);
    }

    /// <summary>
    /// Gets or sets the controls at the end of the bar (right in left-to-right
    /// locales): the primary menu and the window controls.
    /// </summary>
    public Control? EndContent
    {
        get => GetValue(EndContentProperty);
        set => SetValue(EndContentProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the active view has unsaved changes. The bar shows a
    /// dot before the title, so a shell without tabs keeps the marker visible.
    /// </summary>
    public bool IsDirty
    {
        get => GetValue(IsDirtyProperty);
        set => SetValue(IsDirtyProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon shown before the title; it is hidden when
    /// <see langword="null"/>. A product supplies its window icon here as well as
    /// through <see cref="Window.Icon"/>, because the shell uses client-side
    /// decorations and draws its own title bar.
    /// </summary>
    public IImage? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleProperty)
        {
            _title.Text = Title;
        }
        else if (change.Property == IsDirtyProperty)
        {
            _dirty.IsVisible = IsDirty;
        }
        else if (change.Property == SubtitleProperty)
        {
            _subtitle.Text = Subtitle;
            _subtitle.IsVisible = !string.IsNullOrEmpty(Subtitle);
        }
        else if (change.Property == CenterContentProperty)
        {
            var hasCenter = CenterContent is not null;
            _centerContent.Content = CenterContent;
            _centerContent.IsVisible = hasCenter;
            _identity.IsVisible = !hasCenter;
        }
        else if (change.Property == IconProperty)
        {
            _icon.Source = Icon;
            _icon.IsVisible = Icon is not null;
        }
        else if (change.Property == StartContentProperty)
        {
            SetSlot(_start, StartContent);
        }
        else if (change.Property == EndContentProperty)
        {
            SetSlot(_end, EndContent);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // A press on a button, menu or toggle belongs to that control; starting a
        // window drag here would capture the pointer and swallow the click. The
        // event source can be the top level for synthesized input, so check the
        // hit-test as well.
        if (IsPointerOnInteractiveChild(e)) { return; }

        if (e.ClickCount == 2)
        {
            if (TopLevel.GetTopLevel(this) is Window window && window.CanMaximize)
            {
                window.WindowState = window.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }

            return;
        }

        (TopLevel.GetTopLevel(this) as Window)?.BeginMoveDrag(e);
    }

    /// <summary>Determines whether a press source is an interactive child of the bar.</summary>
    /// <param name="source">The visual that received the press, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the press belongs to a child control.</returns>
    internal static bool IsInteractive(Visual? source)
    {
        for (var current = source; current is not null && current is not HeaderBar; current = current.GetVisualParent())
        {
            if (current is Control { Focusable: true } or Button or ToggleButton or MenuItem)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether a press belongs to an interactive child of the bar.</summary>
    /// <param name="e">The pointer-pressed event.</param>
    /// <returns><see langword="true"/> when the press should go to a child control, not a window drag.</returns>
    internal bool IsPointerOnInteractiveChild(PointerPressedEventArgs e)
    {
        var hit = this.GetVisualsAt(e.GetPosition(this)).FirstOrDefault();

        return IsInteractive(e.Source as Visual) || IsInteractive(hit);
    }

    private static void SetSlot(Panel slot, Control? content)
    {
        slot.Children.Clear();

        if (content is not null)
        {
            slot.Children.Add(content);
        }
    }

    /// <summary>Re-resolves the brushes set once, so a live theme switch is reflected.</summary>
    internal void ApplyTheme()
    {
        Background = ThemeBrushes.Resolve("SurfaceBrush");
        BorderBrush = ThemeBrushes.Resolve("BorderBrush");
    }
}
