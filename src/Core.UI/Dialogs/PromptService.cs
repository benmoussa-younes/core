// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Services;
using Core.UI.Hosting;

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

        // UI-thread affine: fail fast rather than mutate the visual tree from a
        // pool thread once the await below resumes.
        UiServices.Dispatcher.VerifyAccess();

        return PromptAsyncCore(request, cancellationToken);
    }

    private ValueTask<string?> PromptAsyncCore(PromptRequest request, CancellationToken cancellationToken) =>
        DialogSession.RunAsync(
            _gate,
            () => _dialog.ShowAsync(request),
            task => _dialog.Dismiss(task),
            null,
            cancellationToken);
}
