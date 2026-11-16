// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Documents;
using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class DataDocumentTests
{
    private static DataTable Table() =>
        new("root", new DataSchema([new DataField("a", DataKind.Integer)]));

    [Fact]
    public void It_has_a_root_and_no_sections_by_default()
    {
        var root = Table();
        var document = new DataDocument(root);

        Assert.Same(root, document.Root);
        Assert.Empty(document.Sections);
    }

    [Fact]
    public void It_keeps_the_sections_in_order()
    {
        var first = new DataTable("s1", DataSchema.Empty);
        var second = new DataTable("s2", DataSchema.Empty);
        var document = new DataDocument(Table(), [first, second]);

        Assert.Equal(2, document.Sections.Count);
        Assert.Same(first, document.Sections[0]);
        Assert.Same(second, document.Sections[1]);
    }

    [Fact]
    public void It_copies_the_sections_and_rejects_a_duplicate_name()
    {
        var sections = new List<DataTable> { new("s", DataSchema.Empty) };
        var document = new DataDocument(Table(), sections);
        sections.Add(new DataTable("t", DataSchema.Empty));
        Assert.Single(document.Sections);

        var duplicate = new List<DataTable> { new("s", DataSchema.Empty), new("s", DataSchema.Empty) };
        Assert.Throws<ArgumentException>(() => new DataDocument(Table(), duplicate));
    }

    [Fact]
    public void It_looks_a_section_up_by_name()
    {
        var section = new DataTable("s", DataSchema.Empty);
        var document = new DataDocument(Table(), [section]);

        Assert.True(document.TryGetSection("s", out var found));
        Assert.Same(section, found);
        Assert.False(document.TryGetSection("missing", out var missing));
        Assert.Null(missing);
        Assert.Throws<ArgumentNullException>(() => document.TryGetSection(null!, out _));
    }

    [Fact]
    public void It_refuses_a_null_root()
    {
        Assert.Throws<ArgumentNullException>(() => new DataDocument(null!));
    }

    [Fact]
    public void FromTable_wraps_a_single_root_table()
    {
        var table = Table();
        var document = DataDocument.FromTable(table);

        Assert.Same(table, document.Root);
        Assert.Empty(document.Sections);
        Assert.Throws<ArgumentNullException>(() => DataDocument.FromTable(null!));
    }
}
