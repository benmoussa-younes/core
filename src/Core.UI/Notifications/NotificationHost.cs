// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Core.Mvp.Services;
using Core.UI.Adwaita;

namespace Core.UI.Notifications;

// The feedback region of the shell: a persistent banner pinned under the toolbar
// and transient toasts stacked in a corner. There is no status bar; everything the
// operator must notice arrives here.
internal class NotificationHost : Grid
{
    private const int MaxToasts = 10;

    // WHY: a zero delay would re-arm the timer in a busy loop when a deadline is
    // already past, which happens under a fake clock or a stalled dispatcher.
    private static readonly TimeSpan MinimumTick = TimeSpan.FromMilliseconds(10);

    private readonly Border _banner;
    private readonly TextBlock _bannerText;
    private readonly StackPanel _toasts;
    private readonly TimeProvider _timeProvider;
    private readonly List<Toast> _pending = [];
    private DispatcherTimer? _timer;
    private TimeSpan _toastDuration = TimeSpan.FromSeconds(4);
    private MessageSeverity _bannerSeverity = MessageSeverity.Information;

    public NotificationHost()
        : this(TimeProvider.System)
    {
    }

    internal NotificationHost(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
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

    // Applies to the toasts shown after the change; a live toast keeps its deadline.
    public TimeSpan ToastDuration
    {
        get => _toastDuration;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _toastDuration = value;
        }
    }

    public bool IsBannerOpen => _banner.IsVisible;

    public int ToastCount => _toasts.Children.Count;

    internal bool IsTimerArmed => _timer?.IsEnabled == true;

    // The delay to the earliest deadline, for the tests: a shared timer restarted
    // on every Show would report the latest toast's duration instead.
    internal TimeSpan? TimerInterval => _timer?.Interval;

    // The toasts still showing, oldest first, for the tests and the theme re-apply.
    internal IReadOnlyList<Border> ToastViews => [.. _pending.Select(toast => toast.Control)];

    public void Show(string message, MessageSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(message);

        var control = CreateToast(message, severity);
        _toasts.Children.Add(control);

        // Each toast carries its own deadline: a shared timer restarted on every
        // Show never fires while toasts arrive faster than the duration.
        _pending.Add(new Toast(control, severity, _timeProvider.GetUtcNow() + _toastDuration));

        while (_pending.Count > MaxToasts)
        {
            RemoveAt(0);
        }

        ArmTimer();
    }

    public void Banner(string message, MessageSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(message);

        _bannerSeverity = severity;
        _bannerText.Text = message;
        _banner.Background = ThemeBrushes.Resolve("SurfaceRaisedBrush");
        _banner.BorderBrush = Tone(severity);
        _banner.IsVisible = true;
    }

    public void DismissBanner()
    {
        _banner.IsVisible = false;
    }

    // WHY: a host removed while a toast is live must not keep a dispatcher timer
    // (and itself) referenced until the interval elapses.
    protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _timer?.Stop();
        _timer = null;
    }

    // WHY: the detach stops the timer without dropping the pending toasts, so a
    // re-attached host must arm their deadlines again or they never expire.
    protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        ArmTimer();
    }

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

    internal void ApplyTheme()
    {
        _bannerText.Foreground = ThemeBrushes.Resolve("ForegroundBrush");
        _banner.Background = ThemeBrushes.Resolve("SurfaceRaisedBrush");
        _banner.BorderBrush = Tone(_bannerSeverity);

        foreach (var toast in _pending)
        {
            toast.Control.Background = ThemeBrushes.Resolve("SurfaceRaisedBrush");
            toast.Control.BorderBrush = Tone(toast.Severity);

            if (toast.Control.Child is TextBlock text)
            {
                text.Foreground = ThemeBrushes.Resolve("ForegroundBrush");
            }
        }
    }

    internal Border BannerView => _banner;

    internal void Expire()
    {
        _timer?.Stop();
        _toasts.Children.Clear();
        _pending.Clear();
    }

    internal void ExpireDue()
    {
        var now = _timeProvider.GetUtcNow();

        for (var index = _pending.Count - 1; index >= 0; index--)
        {
            if (_pending[index].ExpiresAt <= now)
            {
                RemoveAt(index);
            }
        }

        ArmTimer();
    }

    private void ArmTimer()
    {
        if (_pending.Count == 0)
        {
            _timer?.Stop();
            return;
        }

        var delay = _pending.Min(toast => toast.ExpiresAt) - _timeProvider.GetUtcNow();

        if (delay < MinimumTick)
        {
            delay = MinimumTick;
        }

        _timer ??= CreateTimer();
        _timer.Stop();
        _timer.Interval = delay;
        _timer.Start();
    }

    private void RemoveAt(int index)
    {
        var toast = _pending[index];
        _pending.RemoveAt(index);
        _toasts.Children.Remove(toast.Control);
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer();
        timer.Tick += (_, _) => ExpireDue();
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

    private sealed record Toast(Border Control, MessageSeverity Severity, DateTimeOffset ExpiresAt);
}
