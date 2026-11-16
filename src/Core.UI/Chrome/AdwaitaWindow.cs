// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Core.UI.Adwaita;
using Core.UI.Hosting;

namespace Core.UI.Chrome;

/// <summary>
/// A window painted with the Adwaita background, optionally using client-side
/// decorations.
/// </summary>
/// <remarks>
/// Client-side decorations are the Wayland convention; on X11 the window manager
/// keeps its decorations unless <c>CORE_FORCE_CSD</c> is set. Either way a
/// <see cref="HeaderBar"/> in the content still renders and stays usable.
/// </remarks>
public class AdwaitaWindow : Window
{
    /// <summary>Identifies the <see cref="ClientSideDecorations"/> property.</summary>
    public static readonly StyledProperty<bool> ClientSideDecorationsProperty =
        AvaloniaProperty.Register<AdwaitaWindow, bool>(nameof(ClientSideDecorations), defaultValue: true);

    private readonly GraphicsBackend _backend;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdwaitaWindow"/> class.
    /// </summary>
    public AdwaitaWindow()
    {
        // The backend is fixed for the process; resolving it once keeps a later
        // ClientSideDecorations change from re-reading the environment.
        _backend = PlatformSelection.FromEnvironment();
        Background = ThemeBrushes.Resolve("BackgroundBrush");
        ApplyDecorations(ClientDecorationSelection.FromEnvironment(ClientSideDecorations, _backend));
        ActualThemeVariantChanged += (_, _) => Background = ThemeBrushes.Resolve("BackgroundBrush");
    }

    /// <summary>
    /// Gets or sets a value indicating whether client-side decorations are
    /// requested. They are only used where the platform supports them.
    /// </summary>
    public bool ClientSideDecorations
    {
        get => GetValue(ClientSideDecorationsProperty);
        set => SetValue(ClientSideDecorationsProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ClientSideDecorationsProperty)
        {
            ApplyDecorations(ClientDecorationSelection.FromEnvironment(ClientSideDecorations, _backend));
        }
    }

    internal void ApplyDecorations(bool useClientSideDecorations)
    {
        if (useClientSideDecorations)
        {
            WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
            ExtendClientAreaToDecorationsHint = true;
        }
        else
        {
            WindowDecorations = global::Avalonia.Controls.WindowDecorations.Full;
            ExtendClientAreaToDecorationsHint = false;
        }
    }
}
