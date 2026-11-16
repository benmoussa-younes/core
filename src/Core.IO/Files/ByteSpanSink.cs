// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.IO.Files;

/// <summary>
/// Receives a span of bytes read from a file.
/// </summary>
/// <param name="bytes">The bytes read. The span is valid only for the call.</param>
public delegate void ByteSpanSink(ReadOnlySpan<byte> bytes);
