// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers;
using Core.Results;
using Core.Serialization.Codecs.Csv;
using Core.Serialization.Codecs.Ini;
using Core.Serialization.Codecs.Json;
using Core.Serialization.Documents;

namespace Core.Serialization.Codecs;

/// <summary>
/// The one place the format switch lives: which formats are supported and how
/// each reads and writes a <see cref="DataDocument"/>. The store delegates here
/// instead of repeating the dispatch.
/// </summary>
internal static class DataCodec
{
    /// <summary>Whether a format has a codec.</summary>
    /// <param name="format">The format.</param>
    /// <returns><see langword="true"/> when the format is implemented.</returns>
    internal static bool IsSupported(DataFormat format) =>
        format is DataFormat.Json or DataFormat.Csv or DataFormat.Ini;

    /// <summary>Reads a document in a format.</summary>
    /// <param name="format">The format; must be supported.</param>
    /// <param name="utf8">The encoded bytes.</param>
    /// <returns>The document, or a format failure.</returns>
    internal static Result<DataDocument> Read(DataFormat format, ReadOnlySpan<byte> utf8) => format switch
    {
        DataFormat.Csv => CsvDataReader.Read(utf8),
        DataFormat.Ini => IniDataReader.Read(utf8),
        _ => JsonDataReader.Read(utf8),
    };

    /// <summary>Writes a document in a format.</summary>
    /// <param name="format">The format; must be supported.</param>
    /// <param name="document">The document.</param>
    /// <param name="writer">The destination buffer.</param>
    /// <returns>Success, or a format failure.</returns>
    internal static Result<bool> Write(DataFormat format, DataDocument document, IBufferWriter<byte> writer) => format switch
    {
        DataFormat.Csv => CsvDataWriter.Write(document, writer),
        DataFormat.Ini => IniDataWriter.Write(document, writer),
        _ => JsonDataWriter.Write(document, writer),
    };
}
