// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Text;

/// <summary>
/// One text file's content and the facts needed to write it back byte-identically:
/// the editor works on <see cref="LineEnding.LineFeed"/> text, and the file's own
/// line ending and byte-order mark are restored on save.
/// </summary>
/// <param name="Text">The text, normalized to line feeds.</param>
/// <param name="LineEnding">The file's original line-ending style.</param>
/// <param name="HasBom">Whether the file started with a UTF-8 byte-order mark.</param>
public sealed record TextDocument(string Text, LineEnding LineEnding, bool HasBom)
{
    /// <summary>Gets an empty UTF-8 document with line-feed endings.</summary>
    public static TextDocument Empty { get; } = new(string.Empty, LineEnding.LineFeed, false);
}
