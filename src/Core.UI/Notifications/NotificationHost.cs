// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Core.Mvp.Services;
using Core.UI.Adwaita;

namespace Core.UI.Notifications;

/// <summary>
/// The feedback region of the shell: a persistent banner pinned under the toolbar
/// and transient toasts stacked in a corner. There is no status bar; everything
/// the operator must notice arrives here.
/// </summary>
internal class NotificationHost : Grid
{
    private readonly Border _banner;
    private readonly TextBlock _bannerText;
    private readonly StackPanel _toasts;
    private DispatcherTimer? _timer;
    private TimeSpan _toastDuration = TimeSpan.FromSeconds(4);
    private MessageSeverity _bannerSeverity = MessageSeverity.Information;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationHost"/> class.
    /// </summary>
    public NotificationHost()
    {
        RowDefinitions = new RowDefinitions("Auto,*");

        _bannerText = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center, Foreground = ThemeBrushes.Resolve("ForegroundBrush") };
        _banner = new Border
        {
            IsVisible = false,
            Padding = new Thickness(12, 6),
            BorderThickness = new Thickness(3, 0, 0, 1),
            Background = ThemeBrushes.Resolve("SurfaceRaisedBrush"),
            BorderBrush = ThemeBrushes.Resolve("BorderBrush"),
            Child = _bannerText,
        };

        _toasts = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(12),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom,
        };

        SetRow(_banner, 0);
        SetRow(_toasts, 1);
        Children.Add(_banner);
        Children.Add(_toasts);
    }

    /// <summary>Gets or sets how long a toast stays before it expires.</summary>
    public TimeSpan ToastDuration
    {
        get => _toastDuration;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _toastDuration = value;

            if (_timer is not null)
            {
                _timer.Interval = value;
            }
        }
    }

    /// <summary>Gets a value indicating whether a banner is showing.</summary>
    public bool IsBannerOpen => _banner.IsVisible;

    /// <summary>Gets the number of toasts currently showing.</summary>
    public int ToastCount => _toasts.Children.Count;

    /// <summary>Shows a transient toast.</summary>
    /// <param name="message">The message.</param>
    /// <param name="severity">The severity, which selects the tone.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    public void Show(string message, MessageSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(message);

        _toasts.Children.Add(CreateToast(message, severity));

        _timer ??= CreateTimer();
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Shows a persistent banner, replacing any current one.</summary>
    /// <param name="message">The message.</param>
    /// <param name="severity">The severity, which selects the tone.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    public void Banner(string message, MessageSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(message);

        _bannerSeverity = severity;
        _bannerText.Text = message;
        _banner.Background = ThemeBrushes.Resolve("SurfaceRaisedBrush");
        _banner.BorderBrush = Tone(severity);
        _banner.IsVisible = true;
    }

    /// <summary>Dismisses the current banner, if any.</summary>
    public void DismissBanner()
    {
        _banner.IsVisible = false;
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // A host removed while a toast is live must not keep a dispatcher timer
        // (and itself) referenced until the interval elapses.
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>Resolves the brush that tones a message by its severity.</summary>
    /// <param name="severity">The severity.</param>
    /// <returns>The brush, from the shared theme.</returns>
    internal static IBrush Tone(MessageSeverity severity)
    {
        return ThemeBrushes.Resolve(severity switch
        {
            MessageSeverity.Success => "SuccessBrush",
            MessageSeverity.Warning => "WarningBrush",
            MessageSeverity.Error => "ErrorBrush",
            _ => "PrimaryBrush",
        });
    }

    /// <summary>Re-resolves the brushes set once, so a live theme switch is reflected.</summary>
    internal void ApplyTheme()
    {
        _bannerText.Foreground = ThemeBrushes.Resolve("ForegroundBrush");
        _banner.Background = ThemeBrushes.Resolve("SurfaceRaisedBrush");
        _banner.BorderBrush = Tone(_bannerSeverity);
    }

    /// <summary>Gets the banner surface, for tests and the theme re-apply.</summary>
    internal Border BannerView => _banner;

    internal void Expire()
    {
        _timer?.Stop();
        _toasts.Children.Clear();
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer { Interval = ToastDuration };
        timer.Tick += (_, _) => Expire();
        return timer;
    }

    private static Border CreateToast(string message, MessageSeverity severity)
    {
        return new Border
        {
            Padding = new Thickness(12, 8),
            CornerRadius = new CornerRadius(6),
            Background = ThemeBrushes.Resolve("SurfaceRaisedBrush"),
            BorderThickness = new Thickness(1),
            BorderBrush = Tone(severity),
            Child = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = ThemeBrushes.Resolve("ForegroundBrush") },
        };
    }
}
