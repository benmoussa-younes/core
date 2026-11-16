// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Paths;

namespace Core.Tests;

public sealed class ApplicationPathsTests
{
    [Fact]
    public void Directories_are_namespaced_by_vendor_and_application()
    {
        var paths = Paths();

        Assert.Equal("/home/user/.config/core/myapp", paths.ConfigDirectory);
        Assert.Equal("/home/user/.local/state/core/myapp", paths.StateDirectory);
        Assert.Equal("/home/user/.local/share/core/myapp", paths.DataDirectory);
        Assert.Equal("/home/user/.cache/core/myapp", paths.CacheDirectory);
    }

    [Fact]
    public void File_paths_are_below_their_directory()
    {
        var paths = Paths();

        Assert.Equal("/home/user/.config/core/myapp/settings.json", paths.ConfigFile("settings.json"));
        Assert.Equal("/home/user/.local/state/core/myapp/window.json", paths.StateFile("window.json"));
        Assert.Equal("/home/user/.local/share/core/myapp/data.bin", paths.DataFile("data.bin"));
        Assert.Equal("/home/user/.cache/core/myapp/thumb.png", paths.CacheFile("thumb.png"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void An_invalid_vendor_is_rejected(string vendor)
    {
        var result = ApplicationPaths.Create(Directories(), vendor, "myapp");

        Assert.True(result.IsFailure);
        Assert.Equal("paths.invalid_component", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void An_invalid_application_is_rejected(string application)
    {
        var result = ApplicationPaths.Create(Directories(), "core", application);

        Assert.True(result.IsFailure);
        Assert.Equal("paths.invalid_component", result.Error.Code);
    }

    [Fact]
    public void A_null_vendor_is_rejected()
    {
        var result = ApplicationPaths.Create(Directories(), null!, "myapp");

        Assert.True(result.IsFailure);
        Assert.Equal("paths.invalid_component", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_a_null_directory_set()
    {
        Assert.Throws<ArgumentNullException>(
            () => ApplicationPaths.Create(null!, "core", "myapp"));
    }

    [Fact]
    public void A_file_name_must_not_be_blank()
    {
        var paths = Paths();

        Assert.Throws<ArgumentException>(() => paths.ConfigFile(string.Empty));
        Assert.Throws<ArgumentException>(() => paths.StateFile(" "));
        Assert.Throws<ArgumentNullException>(() => paths.DataFile(null!));
        Assert.Throws<ArgumentException>(() => paths.CacheFile("\t"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData(".")]
    [InlineData("..")]
    public void A_file_name_cannot_escape_its_directory(string name)
    {
        var paths = Paths();

        Assert.Throws<ArgumentException>(() => paths.ConfigFile(name));
        Assert.Throws<ArgumentException>(() => paths.StateFile(name));
        Assert.Throws<ArgumentException>(() => paths.DataFile(name));
        Assert.Throws<ArgumentException>(() => paths.CacheFile(name));
    }

    private static ApplicationPaths Paths()
    {
        var result = ApplicationPaths.Create(Directories(), "core", "myapp");

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static PathSet Directories() =>
        new(
            "/home/user/.config",
            "/home/user/.local/state",
            "/home/user/.local/share",
            "/home/user/.cache",
            runtimeDirectory: null,
            ["/etc/xdg"],
            ["/usr/local/share", "/usr/share"]);
}
