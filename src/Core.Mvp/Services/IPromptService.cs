// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>
/// The text prompt the application shell renders as an overlay, so a presenter
/// asks for a value without building its own dialog.
/// </summary>
/// <remarks>
/// The shell shows one overlay at a time: a second concurrent prompt, or a
/// confirmation, waits for the current one instead of throwing. The
/// implementation builds and updates controls, so call it on the UI thread.
/// </remarks>
public interface IPromptService
{
    /// <summary>Asks for one text value.</summary>
    /// <param name="request">The title, label, value and button labels.</param>
    /// <param name="cancellationToken">Cancels the prompt; it answers <see langword="null"/>.</param>
    /// <returns>The typed value, or <see langword="null"/> when cancelled.</returns>
    ValueTask<string?> PromptAsync(PromptRequest request, CancellationToken cancellationToken = default);
}
