// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;
using Core.Results;
using Core.Serialization.Documents;
using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Codecs.Ini;

/// <summary>
/// Reads a <see cref="DataDocument"/> from INI: the keys before any section form
/// the root table, and each <c>[section]</c> is a table of its <c>key = value</c>
/// rows. A repeated key is another row, so a WireGuard config with several
/// <c>Address</c> entries keeps every line.
/// </summary>
/// <remarks>
/// Blank lines and lines starting with <c>#</c> or <c>;</c> are skipped. A
/// repeated section header appends to the first table of that name. Every table
/// has exactly two text columns, <c>key</c> and <c>value</c>; the format is text
/// only, so a typed value is read back as text.
/// </remarks>
public static class IniDataReader
{
    /// <summary>The maximum INI input size a read accepts.</summary>
    public const int MaxBytes = 16 * 1024 * 1024;

    /// <summary>The maximum number of non-empty logical lines.</summary>
    public const int MaxLines = 1_000_000;

    /// <summary>Reads a document from UTF-8 INI.</summary>
    /// <param name="utf8">The UTF-8 INI bytes.</param>
    /// <returns>The document, or a failure with code <c>serialization.syntax</c>.</returns>
    public static Result<DataDocument> Read(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length > MaxBytes)
        {
            return Result<DataDocument>.Failure(
                new CoreError(CoreErrorCodes.SerializationTooLarge, $"The INI document is larger than {MaxBytes} bytes."));
        }

        var text = Encoding.UTF8.GetString(utf8);

        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var rootRows = new List<(string Key, string Value)>();
        var sections = new List<Section>();
        var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);

        List<(string Key, string Value)>? current = null;
        var lineNumber = 0;

        using var lines = new StringReader(text);
        string? raw;

        while ((raw = lines.ReadLine()) is not null)
        {
            lineNumber++;

            if (lineNumber > MaxLines)
            {
                return Result<DataDocument>.Failure(
                    new CoreError(CoreErrorCodes.SerializationTooLarge, $"The INI document has more than {MaxLines} lines."));
            }

            var line = raw.Trim();

            if (line.Length == 0 || line[0] == '#' || line[0] == ';')
            {
                continue;
            }

            if (line[0] == '[')
            {
                if (line[^1] != ']')
                {
                    return Syntax(lineNumber, "the section header is not closed");
                }

                var name = line[1..^1].Trim();

                if (name.Length == 0)
                {
                    return Syntax(lineNumber, "the section name is empty");
                }

                current = Resolve(sections, indexByName, name);
                continue;
            }

            var equals = line.IndexOf('=');

            if (equals <= 0)
            {
                return Syntax(lineNumber, "the line is not a 'key = value'");
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            (current ?? rootRows).Add((key, value));
        }

        var root = BuildTable(string.Empty, rootRows);
        var sectionTables = new List<DataTable>(sections.Count);

        foreach (var section in sections)
        {
            sectionTables.Add(BuildTable(section.Name, section.Rows));
        }

        return Result<DataDocument>.Success(new DataDocument(root, sectionTables));
    }

    private static List<(string Key, string Value)> Resolve(
        List<Section> sections,
        Dictionary<string, int> indexByName,
        string name)
    {
        if (indexByName.TryGetValue(name, out var index))
        {
            return sections[index].Rows;
        }

        var rows = new List<(string Key, string Value)>();
        indexByName.Add(name, sections.Count);
        sections.Add(new Section(name, rows));

        return rows;
    }

    private static DataTable BuildTable(string name, List<(string Key, string Value)> rows)
    {
        var table = new DataTable(
            name,
            new DataSchema([new DataField("key", DataKind.Text), new DataField("value", DataKind.Text)]));

        foreach (var (key, value) in rows)
        {
            table.AddRow([DataValue.From(key), DataValue.From(value)]);
        }

        return table;
    }

    private static Result<DataDocument> Syntax(int lineNumber, string reason) =>
        Result<DataDocument>.Failure(
            new CoreError("serialization.syntax", $"Line {lineNumber}: {reason}."));

    private sealed record Section(string Name, List<(string Key, string Value)> Rows);
}
