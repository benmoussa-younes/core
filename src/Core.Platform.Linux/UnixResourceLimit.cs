// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform.Linux;

/// <summary>A soft and hard resource limit.</summary>
/// <param name="Soft">The limit the kernel enforces.</param>
/// <param name="Hard">The ceiling a later raise may not exceed.</param>
public readonly record struct UnixResourceLimit(ulong Soft, ulong Hard)
{
    /// <summary>Gets the kernel's <c>RLIM_INFINITY</c>.</summary>
    public static UnixResourceLimit Unlimited => new(ulong.MaxValue, ulong.MaxValue);
}
