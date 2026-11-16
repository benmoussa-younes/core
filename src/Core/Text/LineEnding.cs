// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Text;

/// <summary>The line-ending style of a text file.</summary>
public enum LineEnding
{
    /// <summary><c>\n</c> — the Unix style.</summary>
    LineFeed = 0,

    /// <summary><c>\r\n</c> — the Windows style.</summary>
    CarriageReturnLineFeed = 1,

    /// <summary><c>\r</c> — the classic Mac style.</summary>
    CarriageReturn = 2,
}
