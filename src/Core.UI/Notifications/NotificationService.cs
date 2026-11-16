// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Services;

namespace Core.UI.Notifications;

/// <summary>
/// The <see cref="INotificationService"/> the shell exposes: it drives the
/// window's <see cref="NotificationHost"/>.
/// </summary>
internal sealed class NotificationService : INotificationService
{
    private readonly NotificationHost _host;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationService"/> class.
    /// </summary>
    /// <param name="host">The window's notification region.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    public NotificationService(NotificationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        _host = host;
    }

    /// <inheritdoc />
    public void Toast(string message, MessageSeverity severity = MessageSeverity.Information)
    {
        _host.Show(message, severity);
    }

    /// <inheritdoc />
    public void Banner(string message, MessageSeverity severity = MessageSeverity.Error)
    {
        _host.Banner(message, severity);
    }

    /// <inheritdoc />
    public void DismissBanner()
    {
        _host.DismissBanner();
    }
}
