// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>The operator's answer to a confirmation.</summary>
public enum ConfirmResult
{
    /// <summary>The confirming verb was chosen.</summary>
    Accept,

    /// <summary>The alternative verb was chosen.</summary>
    Alternative,

    /// <summary>The prompt was dismissed.</summary>
    Cancel,
}
