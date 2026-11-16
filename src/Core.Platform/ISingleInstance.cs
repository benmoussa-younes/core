// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform;

/// <summary>
/// Keeps one instance of an application per user. The first process acquires the
/// claim; a later launch hands its activation payload to the owner and exits.
/// </summary>
/// <remarks>
/// The port carries only what the host consumes: the claim and the payload event.
/// A Unix socket and a Windows named mutex implement the same contract; the host
/// never sees the transport.
/// </remarks>
public interface ISingleInstance : IDisposable
{
    /// <summary>
    /// Raised with the payload a later launch handed over. It runs on a background
    /// thread; a UI host must marshal it to its own thread.
    /// </summary>
    event Action<ReadOnlyMemory<byte>>? Activated;

    /// <summary>
    /// Takes the single-instance claim. Returns <see langword="true"/> when this is
    /// the running instance; <see langword="false"/> after asking the running
    /// instance to surface, in which case the caller must exit.
    /// </summary>
    /// <returns>Whether this process became the running instance.</returns>
    bool TryAcquire();
}
