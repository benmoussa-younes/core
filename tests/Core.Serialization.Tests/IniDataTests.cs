// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;
using System.Text;
using Core.Serialization.Codecs.Ini;
using Core.Serialization.Documents;
using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class IniDataTests
{
    [Fact]
    public void An_oversized_input_is_rejected_before_splitting()
    {
        var input = Encoding.UTF8.GetBytes(new string('a', IniDataReader.MaxBytes + 1));

        var result = IniDataReader.Read(input);

        Assert.True(result.IsFailure);
        Assert.Equal(CoreErrorCodes.SerializationTooLarge, result.Error.Code);
    }

    [Fact]
    public void Too_many_lines_are_rejected()
    {
        var builder = new StringBuilder();

        for (var index = 0; index <= IniDataReader.MaxLines; index++)
        {
            builder.Append("a=b\n");
        }

        var result = IniDataReader.Read(Encoding.UTF8.GetBytes(builder.ToString()));

        Assert.True(result.IsFailure);
        Assert.Equal(CoreErrorCodes.SerializationTooLarge, result.Error.Code);
    }

    [Fact]
    public void It_writes_the_root_then_sections_and_keeps_repeated_keys()
    {
        var document = new DataDocument(
            KvTable(string.Empty, ("a", "1")),
            [
                KvTable("Peer", ("PublicKey", "k"), ("AllowedIPs", "0.0.0.0/0"), ("AllowedIPs", "::/0")),
            ]);

        Assert.Equal(
            "a = 1\n[Peer]\nPublicKey = k\nAllowedIPs = 0.0.0.0/0\nAllowedIPs = ::/0\n",
            Write(document));
    }

    [Fact]
    public void It_refuses_a_table_that_is_not_two_columns()
    {
        var single = new DataTable("root", new DataSchema([new DataField("only", DataKind.Text)]));
        Assert.Equal("serialization.invalid_document", IniDataWriter.WriteToUtf8Bytes(DataDocument.FromTable(single)).Error.Code);

        var node = new DataTable("root", new DataSchema([new DataField("key", DataKind.Text), new DataField("value", DataKind.Node)]));
        Assert.Equal("serialization.invalid_document", IniDataWriter.WriteToUtf8Bytes(DataDocument.FromTable(node)).Error.Code);

        var badSection = new DataTable("s", new DataSchema([new DataField("only", DataKind.Text)]));
        var document = new DataDocument(KvTable(string.Empty), [badSection]);
        Assert.Equal("serialization.invalid_document", IniDataWriter.WriteToUtf8Bytes(document).Error.Code);
    }

    [Fact]
    public void A_null_value_writes_as_an_empty_value()
    {
        var table = new DataTable(
            string.Empty,
            new DataSchema([new DataField("key", DataKind.Text), new DataField("value", DataKind.Text)]));
        table.AddRow([DataValue.From("a"), DataValue.Null]);

        Assert.Equal("a = \n", Write(DataDocument.FromTable(table)));
    }

    [Fact]
    public void It_reads_root_keys_sections_and_repeated_keys()
    {
        var document = Read("a = 1\n[Peer]\nPublicKey = k\nAllowedIPs = 1\nAllowedIPs = 2\n");

        Assert.Equal(DataValue.From("a"), document.Root[0, 0]);
        Assert.Equal(DataValue.From("1"), document.Root[0, 1]);
        Assert.Equal(DataKind.Text, document.Root.Schema[0].Kind);

        Assert.Single(document.Sections);
        var peer = document.Sections[0];
        Assert.Equal("Peer", peer.Name);
        Assert.Equal(3, peer.RowCount);
        Assert.Equal(DataValue.From("AllowedIPs"), peer[1, 0]);
        Assert.Equal(DataValue.From("1"), peer[1, 1]);
        Assert.Equal(DataValue.From("2"), peer[2, 1]);
    }

    [Fact]
    public void It_skips_comments_and_blank_lines()
    {
        var document = Read("# a comment\n; another\n\n  \na = 1\n");

        Assert.Equal(1, document.Root.RowCount);
        Assert.Empty(document.Sections);
    }

    [Fact]
    public void A_repeated_section_appends_to_the_first()
    {
        var document = Read("[P]\na = 1\n[P]\nb = 2\n");

        Assert.Single(document.Sections);
        Assert.Equal(2, document.Sections[0].RowCount);
    }

    [Fact]
    public void It_reads_crlf_and_a_byte_order_mark()
    {
        var document = Read("\uFEFFa = 1\r\n");

        Assert.Equal(DataValue.From("1"), document.Root[0, 1]);
    }

    [Fact]
    public void An_empty_file_is_an_empty_document()
    {
        var document = Read(string.Empty);

        Assert.Equal(0, document.Root.RowCount);
        Assert.Empty(document.Sections);
    }

    [Theory]
    [InlineData("[x\na = 1\n")]
    [InlineData("[]\n")]
    [InlineData("a\n")]
    public void Malformed_lines_report_a_syntax_failure(string ini)
    {
        var result = IniDataReader.Read(Encoding.UTF8.GetBytes(ini));

        Assert.True(result.IsFailure);
        Assert.Equal("serialization.syntax", result.Error.Code);
    }

    [Fact]
    public void It_refuses_a_key_or_value_that_breaks_the_ini_form()
    {
        var equals = KvTable(string.Empty, ("a=b", "1"));
        Assert.Equal("serialization.invalid_document", IniDataWriter.WriteToUtf8Bytes(DataDocument.FromTable(equals)).Error.Code);

        var newline = KvTable(string.Empty, ("a", "x\ny"));
        Assert.Equal("serialization.invalid_document", IniDataWriter.WriteToUtf8Bytes(DataDocument.FromTable(newline)).Error.Code);
    }

    [Theory]
    [InlineData("bad\n[Injected]")]
    [InlineData("bad]")]
    [InlineData("[bad")]
    [InlineData(" bad")]
    [InlineData("bad ")]
    [InlineData("bad=name")]
    public void It_refuses_a_section_name_that_breaks_the_ini_form(string name)
    {
        var document = new DataDocument(KvTable(string.Empty), [KvTable(name, ("k", "v"))]);

        var result = IniDataWriter.WriteToUtf8Bytes(document);

        Assert.True(result.IsFailure);
        Assert.Equal("serialization.invalid_document", result.Error.Code);
    }

    [Fact]
    public void An_ini_value_with_edge_whitespace_does_not_round_trip_silently()
    {
        var document = KvTable(string.Empty, ("k", " x "));
        var result = IniDataWriter.WriteToUtf8Bytes(DataDocument.FromTable(document));

        if (result.IsSuccess)
        {
            var back = IniDataReader.Read(result.Value).Value;
            Assert.Equal(" x ", back.Root[0, 1].ToInvariantText());
        }
        else
        {
            Assert.Equal("serialization.invalid_document", result.Error.Code);
        }
    }

    private static string Write(DataDocument document)
    {
        var result = IniDataWriter.WriteToUtf8Bytes(document);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);

        return Encoding.UTF8.GetString(result.Value);
    }

    private static DataDocument Read(string ini)
    {
        var result = IniDataReader.Read(Encoding.UTF8.GetBytes(ini));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);

        return result.Value;
    }

    private static DataTable KvTable(string name, params (string Key, string Value)[] rows)
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
}
