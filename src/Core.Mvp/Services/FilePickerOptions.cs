// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>The options of an open-file request.</summary>
/// <param name="Title">The dialog title.</param>
/// <param name="AllowMultiple">Whether several files may be selected.</param>
/// <param name="Filters">The file-type choices, or <see langword="null"/> for none.</param>
public sealed record FilePickerOptions(
    string Title,
    bool AllowMultiple = false,
    IReadOnlyList<FileTypeFilter>? Filters = null);
