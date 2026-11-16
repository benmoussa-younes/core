// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>
/// The modal prompts the application shell renders as overlays, so every product
/// asks the operator the same way and no view builds its own dialog.
/// </summary>
/// <remarks>
/// The shell shows one overlay at a time: a confirmation and a prompt are
/// serialized, so a caller can await a second request without racing the first.
/// The implementation builds and updates controls, so call it on the UI thread.
/// </remarks>
public interface IDialogService
{
    /// <summary>Asks a confirm / alternative / cancel question.</summary>
    /// <param name="request">The question, its body and the button labels.</param>
    /// <param name="cancellationToken">The token used to cancel the prompt.</param>
    /// <returns>The operator's answer.</returns>
    ValueTask<ConfirmResult> ConfirmAsync(ConfirmRequest request, CancellationToken cancellationToken = default);

    /// <summary>Asks the save / discard / cancel question for unsaved changes.</summary>
    /// <param name="documentName">The document the question is about.</param>
    /// <param name="cancellationToken">The token used to cancel the prompt.</param>
    /// <returns>The operator's answer.</returns>
    ValueTask<SavePromptChoice> ConfirmSaveAsync(string documentName, CancellationToken cancellationToken = default);

    /// <summary>Asks the quit question.</summary>
    /// <param name="cancellationToken">The token used to cancel the prompt.</param>
    /// <returns>The operator's answer.</returns>
    ValueTask<QuitChoice> ConfirmQuitAsync(CancellationToken cancellationToken = default);
}
