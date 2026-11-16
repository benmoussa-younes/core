// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>
/// The folder gesture a presenter asks the desktop for, as a seam so the picker
/// is testable without a display.
/// </summary>
public interface IFolderPicker
{
    /// <summary>Asks for one existing folder, returning its local path or <see langword="null"/>.</summary>
    /// <param name="options">The dialog title.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The selected folder, or <see langword="null"/> when cancelled.</returns>
    ValueTask<string?> PickFolderAsync(
        FolderPickerOptions options,
        CancellationToken cancellationToken = default);
}
