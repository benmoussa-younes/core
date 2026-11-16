// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Serialization.Tables;

/// <summary>How a join treats a left row with no matching right row.</summary>
public enum JoinKind
{
    /// <summary>Only rows with a match are emitted.</summary>
    Inner = 0,

    /// <summary>A left row with no match is emitted with null right cells.</summary>
    Left = 1,
}
