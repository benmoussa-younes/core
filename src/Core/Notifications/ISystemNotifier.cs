// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Notifications;

/// <summary>
/// The desktop notification boundary: the domain asks for a notification and the
/// host decides how the desktop shows it. A notification is best effort; a
/// missing notification daemon must never fail the operation that raised it.
/// </summary>
public interface ISystemNotifier
{
    /// <summary>Shows one desktop notification.</summary>
    /// <param name="title">The title; it must never carry a secret.</param>
    /// <param name="body">The body; it must never carry a secret.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A value task that completes when the desktop accepted or ignored the notification.</returns>
    ValueTask NotifyAsync(string title, string body, CancellationToken cancellationToken = default);
}
