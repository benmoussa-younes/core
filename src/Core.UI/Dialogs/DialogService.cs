// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Threading;
using Core.Mvp.Services;

namespace Core.UI.Dialogs;

/// <summary>
/// The <see cref="IDialogService"/> the shell exposes: every product asks the
/// operator the same way through one <see cref="ConfirmDialog"/>. A shared
/// <see cref="DialogGate"/> keeps a confirmation and a prompt from overlapping.
/// </summary>
internal sealed class DialogService : IDialogService
{
    private readonly ConfirmDialog _dialog;
    private readonly DialogGate _gate;

    /// <summary>
    /// Initializes a new instance of the <see cref="DialogService"/> class.
    /// </summary>
    /// <param name="dialog">The window's confirmation overlay.</param>
    /// <param name="gate">The gate shared with the prompt service, or a private one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dialog"/> is <see langword="null"/>.</exception>
    public DialogService(ConfirmDialog dialog, DialogGate? gate = null)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        _dialog = dialog;
        _gate = gate ?? new DialogGate();
    }

    /// <inheritdoc />
    public ValueTask<ConfirmResult> ConfirmAsync(ConfirmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ValueTask<ConfirmResult>(ConfirmAsyncCore(request, cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask<SavePromptChoice> ConfirmSaveAsync(string documentName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentName);

        var answer = await ConfirmAsync(
            new ConfirmRequest(
                $"Save changes to {documentName}?",
                "Your changes are lost otherwise.",
                "Save",
                "Discard",
                "Cancel"),
            cancellationToken).ConfigureAwait(false);

        return answer switch
        {
            ConfirmResult.Accept => SavePromptChoice.Save,
            ConfirmResult.Alternative => SavePromptChoice.Discard,
            _ => SavePromptChoice.Cancel,
        };
    }

    /// <inheritdoc />
    public async ValueTask<QuitChoice> ConfirmQuitAsync(CancellationToken cancellationToken = default)
    {
        var answer = await ConfirmAsync(
            new ConfirmRequest("Quit?", string.Empty, "Quit"),
            cancellationToken).ConfigureAwait(false);

        return answer == ConfirmResult.Accept ? QuitChoice.Quit : QuitChoice.Cancel;
    }

    private async Task<ConfirmResult> ConfirmAsyncCore(ConfirmRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ConfirmResult.Cancel;
        }

        DialogGate.Lease lease;

        try
        {
            lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return ConfirmResult.Cancel;
        }

        using (lease)
        {
            var task = _dialog.ShowAsync(request);
            var cancelled = new TaskCompletionSource<ConfirmResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? dismissal = null;

            // The confirmation is not itself cancellable. On cancellation, answer
            // Cancel, dismiss the dialog on the UI thread, and wait for that
            // dismissal before releasing the gate: otherwise the next caller can
            // see the old dialog still open and hit InvalidOperationException.
            using var registration = cancellationToken.Register(() =>
            {
                var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                dismissal = dismissed.Task;

                Dispatcher.UIThread.Post(() =>
                {
                    _dialog.Dismiss(task);
                    dismissed.TrySetResult();
                });

                cancelled.TrySetResult(ConfirmResult.Cancel);
            });

            var completed = await Task.WhenAny(task, cancelled.Task).ConfigureAwait(true);
            var result = completed == task ? await task.ConfigureAwait(true) : ConfirmResult.Cancel;

            if (completed != task && dismissal is not null)
            {
                await dismissal.ConfigureAwait(true);
            }

            return result;
        }
    }
}
