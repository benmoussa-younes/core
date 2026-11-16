// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Presenters;

/// <summary>
/// The lifecycle of a presenter: it is initialized once, then activated and
/// deactivated as the view it drives is shown and hidden.
/// </summary>
public interface IPresenter : IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether the presenter is running an operation.
    /// </summary>
    bool IsBusy { get; }

    /// <summary>
    /// Initializes the presenter, wiring it to its view and services.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A value task that completes when the presenter is initialized.</returns>
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates the presenter, as when its view becomes visible.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A value task that completes when the presenter is active.</returns>
    ValueTask ActivateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates the presenter, as when its view is hidden.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A value task that completes when the presenter is inactive.</returns>
    ValueTask DeactivateAsync(CancellationToken cancellationToken = default);
}
