// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Serialization.Tables;

/// <summary>One key column pair of a join.</summary>
/// <param name="LeftColumn">The key column in the left table.</param>
/// <param name="RightColumn">The key column in the right table.</param>
public readonly record struct JoinKey(string LeftColumn, string RightColumn);
