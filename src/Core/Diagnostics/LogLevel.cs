// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Diagnostics;

/// <summary>The severity of a diagnostic log entry, from least to most urgent.</summary>
public enum LogLevel
{
    /// <summary>Development detail.</summary>
    Debug = 0,

    /// <summary>Normal lifecycle events.</summary>
    Information = 1,

    /// <summary>Something recoverable went wrong.</summary>
    Warning = 2,

    /// <summary>Something failed.</summary>
    Error = 3,
}
