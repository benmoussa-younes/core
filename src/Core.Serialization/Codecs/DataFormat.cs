// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Serialization.Codecs;

/// <summary>
/// An on-disk format a <see cref="Documents.DataDocument"/> can be read from or
/// written to.
/// </summary>
public enum DataFormat
{
    /// <summary>JavaScript Object Notation.</summary>
    Json,

    /// <summary>Comma-separated values; a single root table.</summary>
    Csv,

    /// <summary>INI <c>key = value</c> sections.</summary>
    Ini,

    /// <summary>
    /// Tom's Obvious Minimal Language; recognized by extension and refused with
    /// <c>serialization.unsupported_format</c>. The codec was dropped (no consumer,
    /// no third-party dependency).
    /// </summary>
    Toml,
}
