// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers.Binary;
using Core.Platform.Linux;


namespace Core.Platform.Linux.Tests;

public sealed class DirectoryRecordParserTests
{
    [Fact]
    public void ReadsAValidRecord()
    {
        var block = Record("a.txt"u8.ToArray(), LinuxFileType.RegularFile);
        var offset = 0;

        Assert.True(DirectoryRecordParser.TryReadRecord(block, ref offset, out var entry));
        Assert.Equal("a.txt", entry.Name);
        Assert.Equal(LinuxFileType.RegularFile, entry.Type);
        Assert.True(entry.IsNameValidUtf8);
        Assert.Equal(block.Length, offset);
    }

    [Theory]
    [InlineData((byte)1, LinuxFileType.Fifo)]
    [InlineData((byte)2, LinuxFileType.CharacterDevice)]
    [InlineData((byte)4, LinuxFileType.Directory)]
    [InlineData((byte)6, LinuxFileType.BlockDevice)]
    [InlineData((byte)8, LinuxFileType.RegularFile)]
    [InlineData((byte)10, LinuxFileType.SymbolicLink)]
    [InlineData((byte)12, LinuxFileType.Socket)]
    [InlineData((byte)0, LinuxFileType.Unknown)]
    public void MapsTheRawType(byte raw, LinuxFileType expected)
    {
        var block = Record("x"u8.ToArray(), (LinuxFileType)raw);
        var offset = 0;

        Assert.True(DirectoryRecordParser.TryReadRecord(block, ref offset, out var entry));
        Assert.Equal(expected, entry.Type);
    }

    [Fact]
    public void FlagsAnInvalidUtf8Name()
    {
        var block = Record([0xFF, (byte)'b', (byte)'a', (byte)'d'], LinuxFileType.RegularFile);
        var offset = 0;

        Assert.True(DirectoryRecordParser.TryReadRecord(block, ref offset, out var entry));
        Assert.False(entry.IsNameValidUtf8);
    }

    [Fact]
    public void StopsTheNameAtTheNulTerminator()
    {
        var block = Record("abc\0def"u8.ToArray(), LinuxFileType.RegularFile);
        var offset = 0;

        Assert.True(DirectoryRecordParser.TryReadRecord(block, ref offset, out var entry));
        Assert.Equal("abc", entry.Name);
    }

    [Fact]
    public void AcceptsANameWithoutATerminator()
    {
        var block = Record("abc"u8.ToArray(), LinuxFileType.RegularFile, pad: false);
        var offset = 0;

        Assert.True(DirectoryRecordParser.TryReadRecord(block, ref offset, out var entry));
        Assert.Equal("abc", entry.Name);
    }

    [Fact]
    public void SkipsTheDotEntriesAndReadsTheNextRecord()
    {
        var first = Record("."u8.ToArray(), LinuxFileType.Directory);
        var second = Record(".."u8.ToArray(), LinuxFileType.Directory);
        var third = Record("real"u8.ToArray(), LinuxFileType.RegularFile);
        var block = new byte[first.Length + second.Length + third.Length];
        first.CopyTo(block, 0);
        second.CopyTo(block, first.Length);
        third.CopyTo(block, first.Length + second.Length);
        var offset = 0;

        Assert.True(DirectoryRecordParser.TryReadRecord(block, ref offset, out var entry));
        Assert.Equal("real", entry.Name);
    }

    [Fact]
    public void ReturnsFalseForAnEmptyBlock()
    {
        var offset = 0;

        Assert.False(DirectoryRecordParser.TryReadRecord([], ref offset, out var entry));
        Assert.Equal(default, entry);
    }

    [Fact]
    public void SkipsARecordWithAnEmptyName()
    {
        var offset = 0;

        Assert.False(DirectoryRecordParser.TryReadRecord(Record([], LinuxFileType.RegularFile), ref offset, out _));
    }

    [Fact]
    public void RejectsATruncatedHeader()
    {
        var offset = 0;
        var exception = Assert.Throws<IOException>(() => DirectoryRecordParser.TryReadRecord(new byte[10], ref offset, out _));

        Assert.Contains("truncated", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsALengthBelowTheHeaderSize()
    {
        var block = new byte[32];
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(16), 10);
        var offset = 0;
        var exception = Assert.Throws<IOException>(() => DirectoryRecordParser.TryReadRecord(block, ref offset, out _));

        Assert.Contains("below the header", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsALengthThatExceedsTheBlock()
    {
        var block = new byte[32];
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(16), 40);
        var offset = 0;
        var exception = Assert.Throws<IOException>(() => DirectoryRecordParser.TryReadRecord(block, ref offset, out _));

        Assert.Contains("exceeds", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TreatsTheRecordLengthAsUnsigned()
    {
        var block = new byte[32];
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(16), 0xFFFF);
        var offset = 0;

        Assert.Throws<IOException>(() => DirectoryRecordParser.TryReadRecord(block, ref offset, out _));
    }

    [Fact]
    public void ReadsMultipleRecordsInSequence()
    {
        var first = Record("a"u8.ToArray(), LinuxFileType.RegularFile);
        var second = Record("b"u8.ToArray(), LinuxFileType.Directory);
        var block = new byte[first.Length + second.Length];
        first.CopyTo(block, 0);
        second.CopyTo(block, first.Length);
        var offset = 0;

        Assert.True(DirectoryRecordParser.TryReadRecord(block, ref offset, out var firstEntry));
        Assert.True(DirectoryRecordParser.TryReadRecord(block, ref offset, out var secondEntry));
        Assert.Equal("a", firstEntry.Name);
        Assert.Equal("b", secondEntry.Name);
        Assert.False(DirectoryRecordParser.TryReadRecord(block, ref offset, out _));
    }

    private static byte[] Record(byte[] name, LinuxFileType type, bool pad = true)
    {
        var length = DirectoryRecordParser.HeaderSize + name.Length + (pad ? 1 : 0);
        var block = new byte[length];
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(16), (ushort)length);
        block[18] = (byte)type;
        name.CopyTo(block, DirectoryRecordParser.HeaderSize);
        return block;
    }
}
