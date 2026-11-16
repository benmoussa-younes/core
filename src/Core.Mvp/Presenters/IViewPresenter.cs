// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Views;

namespace Core.Mvp.Presenters;

/// <summary>
/// A presenter that owns a passive view, so the shell can bind the presenter's
/// lifecycle to navigation without the view naming the presenter.
/// </summary>
/// <remarks>
/// <see cref="IPresenter{TView}"/> already exposes its typed view; this
/// non-generic shape lets the shell store and activate presenters by their
/// navigation entry.
/// </remarks>
public interface IViewPresenter : IPresenter
{
    /// <summary>Gets the passive view the presenter drives.</summary>
    IView View { get; }
}
