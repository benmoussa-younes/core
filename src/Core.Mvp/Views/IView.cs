// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;
namespace Core.Mvp.Views;

/// <summary>
/// A passive view: a presenter renders through it, and it raises the user's
/// intents. It holds no logic of its own.
/// </summary>
public interface IView
{
    /// <summary>
    /// Applies the busy state to the view.
    /// </summary>
    /// <param name="busy">Whether the presentation is busy.</param>
    void SetBusy(bool busy);

    /// <summary>
    /// Presents a failure to the user.
    /// </summary>
    /// <param name="error">The failure to present.</param>
    void ShowError(CoreError error);
}
