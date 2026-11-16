// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Tables;

namespace Core.Serialization.Documents;

/// <summary>
/// The one canonical data structure: a root <see cref="DataTable"/> plus an
/// ordered set of named section tables. Every format is a projection of it —
/// CSV and NDJSON are a single root table; INI and TOML add sections; a JSON
/// object is a one-row root table over its scalar fields.
/// </summary>
public sealed class DataDocument
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataDocument"/> class.
    /// </summary>
    /// <param name="root">The root table; must not be <see langword="null"/>.</param>
    /// <param name="sections">The section tables, in order; <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public DataDocument(DataTable root, IReadOnlyList<DataTable>? sections = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        Root = root;
        Sections = sections is null ? [] : [.. sections];

        if (Sections.Count > 1)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var section in Sections)
            {
                if (!names.Add(section.Name))
                {
                    throw new ArgumentException($"A section named '{section.Name}' appears more than once.", nameof(sections));
                }
            }
        }
    }

    /// <summary>Gets the root table.</summary>
    public DataTable Root { get; }

    /// <summary>Gets the section tables, in order.</summary>
    public IReadOnlyList<DataTable> Sections { get; }

    /// <summary>Looks a section up by name, ordinally; the first match wins.</summary>
    /// <param name="name">The section name.</param>
    /// <param name="section">The section when present.</param>
    /// <returns><see langword="true"/> when the section is present.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGetSection(string name, out DataTable? section)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (var candidate in Sections)
        {
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                section = candidate;
                return true;
            }
        }

        section = null;
        return false;
    }

    /// <summary>Creates a document whose root is a single table and which has no sections.</summary>
    /// <param name="table">The root table; must not be <see langword="null"/>.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    public static DataDocument FromTable(DataTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        return new DataDocument(table);
    }
}
