// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Threading;
using Core.Mvp.Services;

namespace Core.UI.Dialogs;

/// <summary>
/// The <see cref="IPromptService"/> the shell exposes: one <see cref="PromptDialog"/>
/// and a cancellation path that dismisses it and answers <see langword="null"/>.
/// A shared <see cref="DialogGate"/> keeps a prompt and a confirmation from
/// overlapping.
/// </summary>
internal sealed class PromptService : IPromptService
{
    private readonly PromptDialog _dialog;
    private readonly DialogGate _gate;

    /// <summary>Initializes the service over the window's prompt overlay.</summary>
    /// <param name="dialog">The prompt overlay.</param>
    /// <param name="gate">The gate shared with the dialog service, or a private one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dialog"/> is <see langword="null"/>.</exception>
    public PromptService(PromptDialog dialog, DialogGate? gate = null)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        _dialog = dialog;
        _gate = gate ?? new DialogGate();
    }

    /// <inheritdoc />
    public ValueTask<string?> PromptAsync(PromptRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ValueTask<string?>(PromptAsyncCore(request, cancellationToken));
    }

    private async Task<string?> PromptAsyncCore(PromptRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        DialogGate.Lease lease;

        try
        {
            lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        using (lease)
        {
            var task = _dialog.ShowAsync(request);
            var cancelled = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? dismissal = null;

            using var registration = cancellationToken.Register(() =>
            {
                var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                dismissal = dismissed.Task;

                Dispatcher.UIThread.Post(() =>
                {
                    _dialog.Dismiss(task);
                    dismissed.TrySetResult();
                });

                cancelled.TrySetResult(null);
            });

            var completed = await Task.WhenAny(task, cancelled.Task).ConfigureAwait(true);
            var result = completed == task ? await task.ConfigureAwait(true) : null;

            if (completed != task && dismissal is not null)
            {
                await dismissal.ConfigureAwait(true);
            }

            return result;
        }
    }
}
