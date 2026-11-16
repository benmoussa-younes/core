// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;
namespace Core.Capabilities;

/// <summary>
/// A single, typed, surface-independent unit of logic.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type produced on success.</typeparam>
public interface ICapability<in TRequest, TResponse>
{
    /// <summary>
    /// Invokes the capability.
    /// </summary>
    /// <param name="request">The request to process.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The value produced on success or a <see cref="CoreError"/> on failure.</returns>
    ValueTask<Result<TResponse>> InvokeAsync(
        TRequest request,
        CancellationToken cancellationToken = default);
}
