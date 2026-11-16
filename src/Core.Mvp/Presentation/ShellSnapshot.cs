// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Presentation;

/// <summary>
/// The state of the fixed chrome for the active view: the identity line, the
/// contextual commands and the busy state. The status bar is deliberately absent;
/// transient feedback is a toast and persistent feedback is a banner.
/// </summary>
/// <param name="Title">The view title, shown in the header centre.</param>
/// <param name="Subtitle">The contextual line under the title, or the empty string.</param>
/// <param name="Commands">The commands for the contextual toolbar.</param>
/// <param name="Busy">Whether the view is running an operation.</param>
/// <param name="CanGoBack">Whether the header shows a back affordance (stack navigation).</param>
public sealed record ShellSnapshot(
    string Title,
    string Subtitle,
    CommandSet Commands,
    bool Busy = false,
    bool CanGoBack = false)
{
    /// <summary>
    /// Gets a value indicating whether the active document has unsaved changes.
    /// The shell renders the marker on its tab; the presenter keeps the title
    /// clean instead of appending a character to it.
    /// </summary>
    public bool IsDirty { get; init; }

    /// <summary>
    /// Gets the progress of the running operation, from 0 to 1, or
    /// <see langword="null"/> for the indeterminate busy bar. A value outside the
    /// range is clamped by the shell. The bar is only visible while
    /// <see cref="Busy"/> is <see langword="true"/>.
    /// </summary>
    public double? Progress { get; init; }
}
