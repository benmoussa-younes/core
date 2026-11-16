// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>
/// The options of the folder picker: the dialog title. The shell picks one folder
/// at a time; a multi-folder selection is not part of the contract.
/// </summary>
/// <param name="Title">The dialog title.</param>
public sealed record FolderPickerOptions(string Title);
