// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Views;

namespace Core.Mvp.Presenters;

/// <summary>
/// A presenter bound to the view type it drives, shaped so the shell can bind
/// its lifecycle to a navigation entry without the view naming the presenter.
/// </summary>
/// <typeparam name="TView">The view type the presenter drives.</typeparam>
public interface IPresenter<out TView> : IViewPresenter
    where TView : class, IView
{
    /// <summary>
    /// Gets the view the presenter drives.
    /// </summary>
    new TView View { get; }
}
