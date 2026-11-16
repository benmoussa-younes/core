// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;
namespace Core.Mvp.Services;

/// <summary>
/// A contract for a piece of business logic that a presenter depends on.
/// </summary>
/// <remarks>
/// Implementations are expected to live outside the presentation layer and to
/// report expected failures as a <see cref="Result{T}"/> rather than an exception.
/// </remarks>
public interface IService
{
}
