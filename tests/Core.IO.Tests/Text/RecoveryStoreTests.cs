// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform.Linux;
using Core.Text;

namespace Core.IO.Tests;

public sealed class RecoveryStoreTests
{
    [Fact]
    public void It_saves_loads_and_clears_the_recovery_beside_the_document()
    {
        using var temp = new TempDirectory();
        var document = temp.FullPath("doc.md");
        var store = new RecoveryStore();

        Assert.Equal(string.Concat(document, RecoveryStore.Suffix), RecoveryStore.PathFor(document));
        Assert.True(RecoveryStore.Clear(document).IsSuccess);

        var missing = store.Load(document);
        Assert.True(missing.IsSuccess);
        Assert.Null(missing.Value);

        Assert.True(store.Save(document, new TextDocument("draft", LineEnding.LineFeed, HasBom: false)).IsSuccess);
        var loaded = store.Load(document);
        Assert.True(loaded.IsSuccess);
        Assert.NotNull(loaded.Value);
        Assert.Equal("draft", loaded.Value.Text);

        Assert.True(RecoveryStore.Clear(document).IsSuccess);
        Assert.False(File.Exists(RecoveryStore.PathFor(document)));
    }

    [Fact]
    public void It_reports_an_unreadable_recovery_and_a_failed_clear()
    {
        using var temp = new TempDirectory();
        var document = temp.FullPath("doc.md");
        var recovery = RecoveryStore.PathFor(document);
        var store = new RecoveryStore();

        File.WriteAllBytes(recovery, [0xFF, 0xFE]);
        Assert.Equal("text.invalid_utf8", store.Load(document).Error.Code);

        File.Delete(recovery);
        Directory.CreateDirectory(recovery);
        Assert.Equal("text.unwritable", RecoveryStore.Clear(document).Error.Code);
    }

    [Fact]
    public void A_symbolic_link_recovery_is_not_followed()
    {
        using var temp = new TempDirectory();
        var document = temp.FullPath("doc.md");
        var target = temp.FullPath("target.txt");
        var recovery = RecoveryStore.PathFor(document);
        Assert.True(new TextFileStore().Write(target, "secret").IsSuccess);
        File.CreateSymbolicLink(recovery, target);

        var loaded = new RecoveryStore().Load(document);

        Assert.True(loaded.IsFailure);
        Assert.Equal("io.unreadable", loaded.Error.Code);
    }

    [Fact]
    public void It_validates_its_arguments()
    {
        var store = new RecoveryStore();

        Assert.Throws<ArgumentException>(() => RecoveryStore.PathFor(string.Empty));
        Assert.Throws<ArgumentNullException>(() => RecoveryStore.PathFor(null!));
        Assert.Throws<ArgumentNullException>(() => store.Save("doc.md", null!));
        Assert.Throws<ArgumentException>(() => store.Load(string.Empty));
        Assert.Throws<ArgumentException>(() => RecoveryStore.Clear(string.Empty));
    }
}
