// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;
namespace Core.Mvp.Models;

/// <summary>
/// The value of an operation that produces nothing, so a value-less service can
/// return a <see cref="Result{T}"/> like any other.
/// </summary>
public readonly struct Unit
{
    /// <summary>
    /// Gets the single <see cref="Unit"/> value.
    /// </summary>
    public static Unit Value => default;
}
