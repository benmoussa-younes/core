// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.UI.Hosting;

namespace Core.UI.Dialogs;

// Runs one overlay through the shared gate. Both services had the same fifty
// lines: acquire, show, answer or cancel, close, release.
internal static class DialogSession
{
    internal static async ValueTask<T> RunAsync<T>(
        DialogGate gate,
        Func<Task<T>> show,
        Action<Task<T>> dismiss,
        T cancelledResult,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return cancelledResult;
        }

        DialogGate.Lease lease;

        try
        {
            lease = await gate.EnterAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return cancelledResult;
        }

        using (lease)
        {
            var task = show();
            var cancelled = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? dismissal = null;

            // The overlay is not itself cancellable. On cancellation, answer the
            // cancellation value, dismiss on the UI thread, and wait for that
            // dismissal before releasing the gate: otherwise the next caller can
            // see the old overlay still open and hit InvalidOperationException.
            using var registration = cancellationToken.Register(() =>
            {
                var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                dismissal = dismissed.Task;

                void Close()
                {
                    dismiss(task);
                    dismissed.TrySetResult();
                }

                // WHY: the await below waits for this dismissal, so a cancellation
                // raised on the UI thread must run it inline; a Post is only for a
                // cancellation raised on another thread.
                if (UiServices.Dispatcher.CheckAccess())
                {
                    Close();
                }
                else if (UiServices.Dispatcher.IsAvailable)
                {
                    UiServices.Dispatcher.Post(Close);
                }
                else
                {
                    // WHY: the dispatcher has started shutting down, so a posted
                    // dismissal never runs and the await below would never return;
                    // the overlay dies with the loop.
                    dismissed.TrySetResult();
                }

                cancelled.TrySetResult(cancelledResult);
            });

            var completed = await Task.WhenAny(task, cancelled.Task).ConfigureAwait(true);
            var result = completed == task ? await task.ConfigureAwait(true) : cancelledResult;

            if (completed != task && dismissal is not null)
            {
                await dismissal.ConfigureAwait(true);
            }

            return result;
        }
    }
}
