// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform.Linux;

/// <summary>A received Unix socket message.</summary>
/// <param name="Length">The number of payload bytes copied into the caller's buffer.</param>
/// <param name="DescriptorCount">The number of descriptors written into the caller's buffer.</param>
/// <param name="Truncated">Whether a datagram payload was longer than the buffer and was truncated.</param>
public readonly record struct UnixMessage(int Length, int DescriptorCount, bool Truncated);
