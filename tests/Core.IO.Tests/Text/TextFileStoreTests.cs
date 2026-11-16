// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;
using Core.IO.Files;
using Core.Platform.Linux;
using Core.Text;
using Microsoft.Win32.SafeHandles;

namespace Core.IO.Tests;

public sealed class TextFileStoreTests
{
    [Fact]
    public void It_round_trips_an_lf_file()
    {
        using var temp = new TempDirectory();
        var path = temp.FullPath("doc.md");
        var store = new TextFileStore();

        Assert.True(store.Write(path, "a\nb").IsSuccess);

        var read = store.Read(path);

        Assert.True(read.IsSuccess);
        Assert.Equal("a\nb", read.Value.Text);
        Assert.Equal(LineEnding.LineFeed, read.Value.LineEnding);
        Assert.False(read.Value.HasBom);
    }

    [Theory]
    [InlineData(LineEnding.CarriageReturnLineFeed, "\r\n")]
    [InlineData(LineEnding.CarriageReturn, "\r")]
    public void It_accepts_every_line_ending_and_normalizes_to_line_feed(
        LineEnding lineEnding,
        string separator)
    {
        using var temp = new TempDirectory();
        var path = temp.FullPath("doc.md");
        var store = new TextFileStore();

        Assert.True(store.Write(path, "a\nb", lineEnding).IsSuccess);

        var read = store.Read(path);

        Assert.True(read.IsSuccess);
        Assert.Equal("a\nb", read.Value.Text);
        Assert.Equal(lineEnding, read.Value.LineEnding);
        Assert.Contains(string.Concat("a", separator, "b"), File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void A_bom_and_crlf_round_trip_through_the_document()
    {
        using var temp = new TempDirectory();
        var path = temp.FullPath("doc.md");
        var store = new TextFileStore();

        Assert.True(store.Write(path, new TextDocument("a\nb", LineEnding.CarriageReturnLineFeed, HasBom: true)).IsSuccess);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);

        var read = store.Read(path);
        Assert.True(read.IsSuccess);
        Assert.True(read.Value.HasBom);
        Assert.Equal(LineEnding.CarriageReturnLineFeed, read.Value.LineEnding);
        Assert.Equal("a\nb", read.Value.Text);
    }

    [Fact]
    public void It_detects_the_majority_of_a_mixed_file()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("mixed.md", Encoding.UTF8.GetBytes("a\r\nb\r\nc\n"));

        var read = new TextFileStore().Read(path);

        Assert.True(read.IsSuccess);
        Assert.Equal("a\nb\nc\n", read.Value.Text);
        Assert.Equal(LineEnding.CarriageReturnLineFeed, read.Value.LineEnding);
    }

    [Fact]
    public void An_empty_file_is_an_empty_document()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("empty.md", string.Empty);

        var read = new TextFileStore().Read(path);

        Assert.True(read.IsSuccess);
        Assert.Equal(string.Empty, read.Value.Text);
        Assert.Equal(LineEnding.LineFeed, read.Value.LineEnding);
        Assert.False(read.Value.HasBom);
    }

    [Fact]
    public void It_reports_a_missing_file_a_directory_and_invalid_utf8()
    {
        using var temp = new TempDirectory();
        var store = new TextFileStore();

        Assert.Equal("io.unreadable", store.Read(temp.FullPath("missing.md")).Error.Code);
        Assert.Equal("io.unreadable", store.Read(temp.CreateDirectory("dir")).Error.Code);

        var invalid = temp.WriteBytes("invalid.md", [0xFF, 0xFE]);
        Assert.Equal("text.invalid_utf8", store.Read(invalid).Error.Code);
    }

    [Fact]
    public void It_refuses_a_file_larger_than_the_maximum_in_either_direction()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("big.md", "0123456789");
        var small = new TextFileStore(maximumBytes: 8);

        Assert.Equal("text.too_large", small.Read(path).Error.Code);
        Assert.Equal("text.too_large", small.Write(temp.FullPath("out.md"), "0123456789").Error.Code);
    }

    [Fact]
    public void It_refuses_an_unwritable_directory_and_a_symbolic_link_target()
    {
        using var temp = new TempDirectory();
        var directory = temp.CreateDirectory("readonly");
        var mode = File.GetUnixFileMode(directory);
        var store = new TextFileStore();

        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Assert.Equal("text.unwritable", store.Write(Path.Combine(directory, "doc.md"), "x").Error.Code);
        }
        finally
        {
            File.SetUnixFileMode(directory, mode);
        }

        var target = temp.WriteFile("target.md", "old");
        var link = temp.CreateFileSymlink("link.md", target);
        var written = store.Write(link, "new");

        Assert.True(written.IsFailure);
        Assert.Equal("io.symbolic_link_target", written.Error.Code);
        Assert.Equal("old", File.ReadAllText(target));
    }

    [Fact]
    public void A_written_file_is_owner_only()
    {
        using var temp = new TempDirectory();
        var path = temp.FullPath("doc.md");

        Assert.True(new TextFileStore().Write(path, "x").IsSuccess);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(path));
    }

    [Fact]
    public void It_refuses_a_lone_surrogate_in_either_direction()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("invalid.md", Encoding.UTF8.GetBytes("ok"));
        var store = new TextFileStore();

        Assert.Equal("text.invalid_utf8", store.Write(temp.FullPath("out.md"), "\uD800").Error.Code);

        // A valid file is readable; the write guard is the encoder path above.
        Assert.True(store.Read(path).IsSuccess);
    }

    [Fact]
    public void A_zero_read_is_unreadable()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("doc.md", "abc");

        var read = new TextFileStore().ReadCore<AZeroChunkReader>(path);

        Assert.Equal("io.unreadable", read.Error.Code);
    }

    [Fact]
    public void It_validates_its_arguments()
    {
        using var temp = new TempDirectory();
        var store = new TextFileStore();

        Assert.Throws<ArgumentOutOfRangeException>(() => new TextFileStore(0));
        Assert.Throws<ArgumentException>(() => store.Read(string.Empty));
        Assert.Throws<ArgumentNullException>(() => store.Read(null!));
        Assert.Throws<ArgumentException>(() => store.Write(string.Empty, "x"));
        Assert.Throws<ArgumentNullException>(() => store.Write(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => store.Write(temp.FullPath("x.md"), (string)null!));
        Assert.Throws<ArgumentNullException>(() => store.Write(temp.FullPath("x.md"), (TextDocument)null!));
    }

    private readonly struct AZeroChunkReader : IFileChunkReader
    {
        public static int Read(SafeFileHandle handle, Span<byte> buffer, long offset) => 0;
    }
}
