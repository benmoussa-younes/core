// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.UI.Chrome;

/// <summary>
/// The request to close a tab, raised before the shell closes it. A handler sets
/// <see cref="Cancel"/> to keep the tab open while it prompts the operator (for
/// example to save unsaved changes).
/// </summary>
public sealed class TabCloseRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TabCloseRequestedEventArgs"/> class.
    /// </summary>
    /// <param name="id">The tab id the operator asked to close.</param>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
    public TabCloseRequestedEventArgs(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        Id = id;
    }

    /// <summary>Gets the tab id the operator asked to close.</summary>
    public string Id { get; }

    /// <summary>Gets or sets a value indicating whether the close is cancelled.</summary>
    public bool Cancel { get; set; }
}
