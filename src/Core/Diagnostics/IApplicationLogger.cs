// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Diagnostics;

/// <summary>
/// The diagnostic log boundary: a domain or presentation layer depends on this
/// contract, never on a file or a logging framework, so the host can substitute a
/// null logger or a test double.
/// </summary>
public interface IApplicationLogger : IDisposable
{
    /// <summary>Writes one entry.</summary>
    /// <param name="level">The severity.</param>
    /// <param name="message">The message; it must never carry a secret.</param>
    void Log(LogLevel level, string message);
}
