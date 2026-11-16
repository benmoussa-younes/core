// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Diagnostics;

/// <summary>
/// A logger that discards every entry. A host that has no diagnostic sink uses it,
/// so a caller never has to test for <see langword="null"/>.
/// </summary>
public sealed class NullApplicationLogger : IApplicationLogger
{
    private NullApplicationLogger()
    {
    }

    /// <summary>Gets the shared instance.</summary>
    public static NullApplicationLogger Instance { get; } = new();

    /// <inheritdoc />
    public void Log(LogLevel level, string message)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
