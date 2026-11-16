// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Presentation;

/// <summary>
/// The ordered commands the contextual toolbar renders for the active view.
/// </summary>
/// <param name="Commands">The commands, in render order.</param>
public sealed record CommandSet(IReadOnlyList<AppCommand> Commands)
{
    /// <summary>Gets an empty command set.</summary>
    public static CommandSet Empty { get; } = new([]);
}
