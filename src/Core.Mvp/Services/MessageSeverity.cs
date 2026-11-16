// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>The tone of a notification.</summary>
public enum MessageSeverity
{
    /// <summary>Neutral information.</summary>
    Information,

    /// <summary>The operation succeeded.</summary>
    Success,

    /// <summary>The operation succeeded with a caveat.</summary>
    Warning,

    /// <summary>The operation failed.</summary>
    Error,
}
