// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Views;

/// <summary>
/// A view that reports its own busy state, so the shell can show its busy overlay
/// while the view runs an operation. A presenter that calls
/// <see cref="IView.SetBusy"/> on such a view raises <see cref="BusyChanged"/>,
/// which the Avalonia shell maps to the overlay; a presenter may instead set
/// <see cref="Presentation.ShellSnapshot.Busy"/>.
/// </summary>
public interface IBusyView : IView
{
    /// <summary>Raised when the view's own busy state changes.</summary>
    event Action<bool>? BusyChanged;
}
