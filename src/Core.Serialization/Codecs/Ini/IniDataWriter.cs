// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers;
using Core.Results;
using Core.Serialization.Documents;
using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Codecs.Ini;

/// <summary>
/// Writes a <see cref="DataDocument"/> as INI: each table is a block of
/// <c>key = value</c> lines, the root table first and each section under its
/// <c>[name]</c> header.
/// </summary>
/// <remarks>
/// INI is text only: every table must have exactly two non-node columns, a key
/// and a value. A repeated key is simply another row, so a WireGuard config with
/// several <c>Address</c> entries round-trips. A table has no INI form and is
/// refused when a key contains <c>=</c>, a key or value contains a newline, or a
/// key or value has leading or trailing whitespace — the reader trims, so those
/// would be lost silently. Comments are not preserved — the output is canonical.
/// </remarks>
public static class IniDataWriter
{
    /// <summary>Writes a document to a buffer.</summary>
    /// <param name="document">The document; must not be <see langword="null"/>.</param>
    /// <param name="writer">The destination buffer; must not be <see langword="null"/>.</param>
    /// <returns>A successful result whose value is always <see langword="true"/>,
    /// or a failure with code <c>serialization.invalid_document</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="writer"/> is <see langword="null"/>.</exception>
    public static Result<bool> Write(DataDocument document, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);

        if (!IsWritable(document.Root))
        {
            return Result<bool>.Failure(Invalid(WritableReason("The root table")));
        }

        foreach (var section in document.Sections)
        {
            if (!IsWritableSectionName(section.Name))
            {
                return Result<bool>.Failure(Invalid(
                    "A section name must be non-empty, trimmed, and contain no '[', ']', '=' or control character."));
            }

            if (!IsWritable(section))
            {
                return Result<bool>.Failure(Invalid(WritableReason($"The section '{section.Name}'")));
            }
        }

        WriteTable(writer, document.Root);

        foreach (var section in document.Sections)
        {
            Utf8Buffer.Write(writer, "[");
            Utf8Buffer.Write(writer, section.Name);
            Utf8Buffer.Write(writer, "]\n");
            WriteTable(writer, section);
        }

        return Result<bool>.Success(true);
    }

    /// <summary>Writes a document to a new UTF-8 byte array.</summary>
    /// <param name="document">The document; must not be <see langword="null"/>.</param>
    /// <returns>The UTF-8 bytes, or a failure from <see cref="Write"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static Result<byte[]> WriteToUtf8Bytes(DataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Utf8Buffer.ToUtf8Bytes(document, Write);
    }

    private static bool IsKeyValueTable(DataTable table) =>
        table.ColumnCount == 2 &&
        table.Column(0).Kind != DataKind.Node &&
        table.Column(1).Kind != DataKind.Node;

    // A section header is written verbatim between '[' and ']': a bracket, an
    // '=' or a newline in the name would forge another header or corrupt the
    // document, so the name is validated before any byte is written.
    private static bool IsWritableSectionName(string name)
    {
        if (name.Length == 0 || name != name.Trim())
        {
            return false;
        }

        foreach (var character in name)
        {
            if (character is '[' or ']' or '=' || char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWritable(DataTable table)
    {
        if (!IsKeyValueTable(table))
        {
            return false;
        }

        for (var row = 0; row < table.RowCount; row++)
        {
            var key = Text(table[row, 0]);
            var value = Text(table[row, 1]);

            if (key.Contains('=') || HasNewline(key) || HasNewline(value) ||
                key != key.Trim() || value != value.Trim())
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasNewline(string text) => text.Contains('\n') || text.Contains('\r');

    private static string WritableReason(string subject) =>
        $"{subject} must have exactly two non-node columns, a key without '=', no newline in a key or value, and no leading or trailing whitespace.";

    private static void WriteTable(IBufferWriter<byte> writer, DataTable table)
    {
        for (var row = 0; row < table.RowCount; row++)
        {
            Utf8Buffer.Write(writer, Text(table[row, 0]));
            Utf8Buffer.Write(writer, " = ");
            Utf8Buffer.Write(writer, Text(table[row, 1]));
            Utf8Buffer.Write(writer, "\n");
        }
    }

    private static string Text(DataValue value) => value.IsNull ? string.Empty : value.ToInvariantText();

    private static CoreError Invalid(string reason) => new("serialization.invalid_document", reason);
}
