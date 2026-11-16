// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Diagnostics;

/// <summary>
/// The last-chance crash sink: the process-level handler writes one entry and the
/// host owns the file. A platform that cannot persist a crash log supplies a
/// no-op implementation instead of making the UI layer know the file format.
/// </summary>
public interface ICrashLog : IDisposable
{
    /// <summary>Gets the current crash file path.</summary>
    string Path { get; }

    /// <summary>Writes one crash entry. It is best effort and must never throw.</summary>
    /// <param name="exception">The exception.</param>
    /// <param name="context">An optional context, for example the operation that was running.</param>
    void Write(Exception exception, string? context = null);
}
