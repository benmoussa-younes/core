// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Text;

/// <summary>The text after a replacement and the selection that replacement leaves.</summary>
/// <param name="Text">The edited text.</param>
/// <param name="SelectionStart">The index of the first selected character.</param>
/// <param name="SelectionLength">The number of selected characters; zero is a caret.</param>
public readonly record struct TextReplacement(string Text, int SelectionStart, int SelectionLength);
