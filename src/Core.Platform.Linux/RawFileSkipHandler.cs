// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.IO.Walking;

namespace Core.Platform.Linux;

/// <summary>
/// Receives one path a <see cref="RawFileWalker"/> did not read.
/// </summary>
/// <param name="relativePath">The root-relative path as UTF-8; valid only during the call.</param>
/// <param name="reason">Why the path was skipped.</param>
public delegate void RawFileSkipHandler(ReadOnlySpan<byte> relativePath, WalkSkipReason reason);

