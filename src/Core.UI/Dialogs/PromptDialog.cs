// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Core.Mvp.Services;
using Core.UI.Adwaita;

namespace Core.UI.Dialogs;

/// <summary>
/// The text prompt the shell draws as a scrim over its host. Enter confirms and
/// Escape cancels, so a keyboard-first product never needs a second window.
/// </summary>
internal class PromptDialog : Grid
{
    private readonly TextBlock _title;
    private readonly TextBlock _label;
    private readonly TextBox _input;
    private readonly Button _confirm;
    private readonly Button _cancel;
    private readonly Border _card;
    private readonly Action<Control>? _beginModal;
    private readonly Action? _endModal;
    private TaskCompletionSource<string?>? _completion;

    /// <summary>Initializes the prompt in its hidden state.</summary>
    public PromptDialog()
        : this(null, null)
    {
    }

    internal PromptDialog(Action<Control>? beginModal, Action? endModal)
    {
        _beginModal = beginModal;
        _endModal = endModal;
        IsVisible = false;
        Background = ThemeBrushes.Resolve("ScrimBrush");

        _title = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemeBrushes.Resolve("ForegroundBrush"),
        };
        _label = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 4),
            Foreground = ThemeBrushes.Resolve("MutedForegroundBrush"),
        };
        _input = new TextBox { MinWidth = 360 };

        _cancel = new Button { Classes = { "flat" } };
        _cancel.Click += (_, _) => Complete(null);

        _confirm = new Button { Classes = { "primary" } };
        _confirm.Click += (_, _) => Complete(_input.Text ?? string.Empty);

        _input.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                Complete(_input.Text ?? string.Empty);
                args.Handled = true;
            }
            else if (args.Key == Key.Escape)
            {
                Complete(null);
                args.Handled = true;
            }
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(_cancel);
        buttons.Children.Add(_confirm);

        var content = new StackPanel();
        content.Children.Add(_title);
        content.Children.Add(_label);
        content.Children.Add(_input);
        content.Children.Add(buttons);

        _card = new Border
        {
            Background = ThemeBrushes.Resolve("BackgroundBrush"),
            BorderBrush = ThemeBrushes.Resolve("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            MinWidth = 360,
            MaxWidth = 520,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            Child = content,
        };

        Children.Add(_card);

        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                Complete(null);
                args.Handled = true;
            }
        };
    }

    /// <summary>Gets a value indicating whether the prompt is showing.</summary>
    public bool IsOpen => IsVisible;

    internal TextBox Input => _input;

    internal Button ConfirmButton => _confirm;

    internal Button CancelButton => _cancel;

    /// <summary>Shows the prompt and returns the typed value, or null on cancel.</summary>
    /// <param name="request">The prompt request.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A prompt is already showing.</exception>
    public Task<string?> ShowAsync(PromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_completion is not null)
        {
            throw new InvalidOperationException("A prompt is already showing; answer or dismiss it first.");
        }

        _title.Text = request.Title;
        _label.Text = request.Label;
        _input.Text = request.Value;
        _input.PasswordChar = request.IsPassword ? '\u25CF' : '\0';
        _confirm.Content = request.ConfirmLabel;
        _cancel.Content = request.CancelLabel;

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _completion = completion;
        IsVisible = true;

        _beginModal?.Invoke(this);
        _input.Focus();
        _input.SelectAll();

        return completion.Task;
    }

    /// <summary>Re-resolves the brushes set once, so a live theme switch is reflected.</summary>
    internal void ApplyTheme()
    {
        Background = ThemeBrushes.Resolve("ScrimBrush");
        _card.Background = ThemeBrushes.Resolve("BackgroundBrush");
        _card.BorderBrush = ThemeBrushes.Resolve("BorderBrush");
        _title.Foreground = ThemeBrushes.Resolve("ForegroundBrush");
        _label.Foreground = ThemeBrushes.Resolve("MutedForegroundBrush");
    }

    /// <summary>Dismisses the prompt as a cancel; a no-op when none is showing.</summary>
    internal void Dismiss() => Complete(null);

    /// <summary>Dismisses the prompt only when it is still the one identified by <paramref name="task"/>.</summary>
    /// <param name="task">The task returned by <see cref="ShowAsync"/>.</param>
    internal void Dismiss(Task<string?> task)
    {
        if (ReferenceEquals(_completion?.Task, task))
        {
            Complete(null);
        }
    }

    private void Complete(string? result)
    {
        IsVisible = false;
        _endModal?.Invoke();

        var completion = _completion;
        _completion = null;
        completion?.TrySetResult(result);
    }
}
