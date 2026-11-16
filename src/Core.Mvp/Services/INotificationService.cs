// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>
/// The transient and persistent feedback the shell renders: a toast disappears on
/// its own, a banner stays until it is dismissed or replaced.
/// </summary>
public interface INotificationService
{
    /// <summary>Shows a transient toast.</summary>
    /// <param name="message">The message.</param>
    /// <param name="severity">The severity, which selects the tone.</param>
    void Toast(string message, MessageSeverity severity = MessageSeverity.Information);

    /// <summary>Shows a persistent banner, replacing any current one.</summary>
    /// <param name="message">The message.</param>
    /// <param name="severity">The severity, which selects the tone.</param>
    void Banner(string message, MessageSeverity severity = MessageSeverity.Error);

    /// <summary>Dismisses the current banner, if any.</summary>
    void DismissBanner();
}
