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
/// The modal-looking confirmation the shell draws as a scrim over its host rather
/// than a second native window. It completes a task with the operator's answer,
/// so it can be driven in a headless test.
/// </summary>
internal class ConfirmDialog : Grid
{
    private readonly TextBlock _title;
    private readonly TextBlock _body;
    private readonly Button _confirm;
    private readonly Button _alternative;
    private readonly Button _cancel;
    private readonly Border _card;
    private readonly Action<Control>? _beginModal;
    private readonly Action? _endModal;
    private TaskCompletionSource<ConfirmResult>? _completion;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfirmDialog"/> class in its
    /// hidden state.
    /// </summary>
    public ConfirmDialog()
        : this(null, null)
    {
    }

    internal ConfirmDialog(Action<Control>? beginModal, Action? endModal)
    {
        _beginModal = beginModal;
        _endModal = endModal;
        IsVisible = false;
        Background = ThemeBrushes.Resolve("ScrimBrush");

        _title = new TextBlock { FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = ThemeBrushes.Resolve("ForegroundBrush") };
        _body = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = ThemeBrushes.Resolve("MutedForegroundBrush") };

        _cancel = new Button();
        _cancel.Classes.Add("flat");
        _cancel.Click += (_, _) => Complete(ConfirmResult.Cancel);

        _alternative = new Button { IsVisible = false };
        _alternative.Classes.Add("flat");
        _alternative.Click += (_, _) => Complete(ConfirmResult.Alternative);

        _confirm = new Button();
        _confirm.Classes.Add("primary");
        _confirm.Click += (_, _) => Complete(ConfirmResult.Accept);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(_alternative);
        buttons.Children.Add(_cancel);
        buttons.Children.Add(_confirm);

        var content = new StackPanel();
        content.Children.Add(_title);
        content.Children.Add(_body);
        content.Children.Add(buttons);

        _card = new Border
        {
            Background = ThemeBrushes.Resolve("BackgroundBrush"),
            BorderBrush = ThemeBrushes.Resolve("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            MinWidth = 320,
            MaxWidth = 480,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            Child = content,
        };

        Children.Add(_card);

        // The scrim blocks the pointer; Tab must stay inside the dialog and
        // Escape must answer Cancel even when focus is on a button. The shell
        // suspends its accelerators while this overlay is open.
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                Complete(ConfirmResult.Cancel);
                args.Handled = true;
            }
        };
    }

    /// <summary>Gets a value indicating whether the prompt is showing.</summary>
    public bool IsOpen => IsVisible;

    internal Button ConfirmButton => _confirm;

    internal Button AlternativeButton => _alternative;

    internal Button CancelButton => _cancel;

    internal TextBlock BodyBlock => _body;

    /// <summary>Shows the prompt and returns a task that completes with the answer.</summary>
    /// <param name="request">The question, its body and the button labels.</param>
    /// <returns>The operator's answer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A prompt is already showing.</exception>
    public Task<ConfirmResult> ShowAsync(ConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_completion is not null)
        {
            throw new InvalidOperationException("A confirmation is already showing; answer or dismiss it first.");
        }

        _title.Text = request.Title;
        _body.Text = request.Body;
        _body.IsVisible = request.Body.Length > 0;
        _confirm.Content = request.ConfirmLabel;
        _alternative.Content = request.AlternativeLabel;
        _alternative.IsVisible = request.AlternativeLabel.Length > 0;
        _cancel.Content = request.CancelLabel;

        _confirm.Classes.Set("destructive", request.Destructive);

        // RunContinuationsAsynchronously: Complete runs inside the click handler, so
        // the awaiting presenter must not resume on this stack (reentrancy).
        var completion = new TaskCompletionSource<ConfirmResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _completion = completion;
        IsVisible = true;

        _beginModal?.Invoke(this);
        _cancel.Focus();

        return completion.Task;
    }

    /// <summary>Re-resolves the brushes set once, so a live theme switch is reflected.</summary>
    internal void ApplyTheme()
    {
        Background = ThemeBrushes.Resolve("ScrimBrush");
        _card.Background = ThemeBrushes.Resolve("BackgroundBrush");
        _card.BorderBrush = ThemeBrushes.Resolve("BorderBrush");
        _title.Foreground = ThemeBrushes.Resolve("ForegroundBrush");
        _body.Foreground = ThemeBrushes.Resolve("MutedForegroundBrush");
    }

    private void Complete(ConfirmResult result)
    {
        var completion = _completion;
        _completion = null;

        // Nothing is showing: a stale click, a second Escape or a dismissal that
        // raced the answer must not release the modal layer another dialog owns.
        if (completion is null)
        {
            return;
        }

        IsVisible = false;
        _endModal?.Invoke();
        completion.TrySetResult(result);
    }

    /// <summary>Dismisses an open prompt as a cancel; a no-op when none is showing.</summary>
    internal void Dismiss() => Complete(ConfirmResult.Cancel);

    /// <summary>Dismisses the prompt only when it is still the one identified by <paramref name="task"/>.</summary>
    /// <param name="task">The task returned by <see cref="ShowAsync"/>.</param>
    internal void Dismiss(Task<ConfirmResult> task)
    {
        if (ReferenceEquals(_completion?.Task, task))
        {
            Complete(ConfirmResult.Cancel);
        }
    }
}
