// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Serialization.Tables;

/// <summary>One key of a stable multi-column sort.</summary>
/// <param name="Column">The column name.</param>
/// <param name="Descending">Whether the key sorts descending.</param>
public readonly record struct SortKey(string Column, bool Descending = false);
