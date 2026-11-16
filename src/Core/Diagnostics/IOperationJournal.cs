// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Diagnostics;

/// <summary>
/// The append-only record of operator-visible operations: what was attempted,
/// when, and with what outcome. It is separate from the diagnostic log, which is
/// for developers; a journal is for the operator and for an audit.
/// </summary>
public interface IOperationJournal : IDisposable
{
    /// <summary>Records one operation.</summary>
    /// <param name="operation">The operation name, for example <c>tunnel.up</c>; it must never carry a secret.</param>
    /// <param name="message">The outcome detail; it must never carry a secret.</param>
    void Record(string operation, string message);
}
