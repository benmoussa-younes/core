// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>The operator's answer to the unsaved-changes prompt.</summary>
public enum SavePromptChoice
{
    /// <summary>Save before continuing.</summary>
    Save,

    /// <summary>Continue and lose the changes.</summary>
    Discard,

    /// <summary>Abandon the action.</summary>
    Cancel,
}
