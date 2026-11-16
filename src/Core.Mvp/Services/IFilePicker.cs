// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>
/// The shell's file picker: a presenter asks for file paths and never names an
/// Avalonia storage type, so it stays headless and testable.
/// </summary>
public interface IFilePicker : IService
{
    /// <summary>Asks the operator to choose one or more files.</summary>
    /// <param name="options">The title, the multiple-selection flag and the filters.</param>
    /// <param name="cancellationToken">The token used to cancel the request.</param>
    /// <returns>The chosen paths; empty when the operator cancelled.</returns>
    ValueTask<IReadOnlyList<string>> OpenFilesAsync(FilePickerOptions options, CancellationToken cancellationToken = default);

    /// <summary>Asks the operator to choose a save destination.</summary>
    /// <param name="options">The title, the suggested name and the filters.</param>
    /// <param name="cancellationToken">The token used to cancel the request.</param>
    /// <returns>The chosen path, or <see langword="null"/> when the operator cancelled.</returns>
    ValueTask<string?> SaveFileAsync(SaveFilePickerOptions options, CancellationToken cancellationToken = default);
}
