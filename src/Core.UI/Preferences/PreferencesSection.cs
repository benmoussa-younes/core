// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Controls;

namespace Core.UI.Preferences;

/// <summary>
/// One product-specific section appended to the shared <see cref="PreferencesDialog"/>:
/// a title and the control that holds its settings.
/// </summary>
/// <param name="Title">The section heading.</param>
/// <param name="Content">The section content.</param>
public sealed record PreferencesSection(string Title, Control Content);
