// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>The options of a save-file request.</summary>
/// <param name="Title">The dialog title.</param>
/// <param name="SuggestedFileName">The prefilled file name, or <see langword="null"/>.</param>
/// <param name="Filters">The file-type choices, or <see langword="null"/> for none.</param>
public sealed record SaveFilePickerOptions(
    string Title,
    string? SuggestedFileName = null,
    IReadOnlyList<FileTypeFilter>? Filters = null);
