// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>
/// One file-type choice in a picker: the label the operator sees and the glob
/// patterns the platform matches.
/// </summary>
/// <param name="Name">The human-readable label (for example <c>WireGuard profiles</c>).</param>
/// <param name="Patterns">The glob patterns (for example <c>*.conf</c>).</param>
public sealed record FileTypeFilter(string Name, IReadOnlyList<string> Patterns);
