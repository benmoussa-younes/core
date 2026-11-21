// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Services;
using Core.UI.Hosting;

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

        // UI-thread affine: fail fast rather than mutate the visual tree from a
        // pool thread once the await below resumes.
        UiServices.Dispatcher.VerifyAccess();

        return ConfirmAsyncCore(request, cancellationToken);
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

    private ValueTask<ConfirmResult> ConfirmAsyncCore(ConfirmRequest request, CancellationToken cancellationToken) =>
        DialogSession.RunAsync(
            _gate,
            () => _dialog.ShowAsync(request),
            task => _dialog.Dismiss(task),
            ConfirmResult.Cancel,
            cancellationToken);
}
