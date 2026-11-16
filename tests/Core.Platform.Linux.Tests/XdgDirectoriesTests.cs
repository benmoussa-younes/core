// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Paths;

namespace Core.Platform.Linux.Tests;

public sealed class XdgDirectoriesTests
{
    [Fact]
    public void Defaults_follow_the_specification_when_nothing_is_set()
    {
        var result = XdgDirectories.Create(Environment());

        Assert.True(result.IsSuccess);

        var directories = result.Value;
        Assert.Equal("/home/user/.config", directories.ConfigHome);
        Assert.Equal("/home/user/.local/state", directories.StateHome);
        Assert.Equal("/home/user/.local/share", directories.DataHome);
        Assert.Equal("/home/user/.cache", directories.CacheHome);
        Assert.Null(directories.RuntimeDirectory);
        Assert.Equal(["/etc/xdg"], directories.ConfigDirectories);
        Assert.Equal(["/usr/local/share", "/usr/share"], directories.DataDirectories);
    }

    [Fact]
    public void Absolute_overrides_are_used()
    {
        var result = XdgDirectories.Create(Environment(
            config: "/custom/config",
            state: "/custom/state",
            data: "/custom/data",
            cache: "/custom/cache",
            runtime: "/run/user/1000",
            configDirectories: "/first:/second",
            dataDirectories: "/one:/two"));

        Assert.True(result.IsSuccess);

        var directories = result.Value;
        Assert.Equal("/custom/config", directories.ConfigHome);
        Assert.Equal("/custom/state", directories.StateHome);
        Assert.Equal("/custom/data", directories.DataHome);
        Assert.Equal("/custom/cache", directories.CacheHome);
        Assert.Equal("/run/user/1000", directories.RuntimeDirectory);
        Assert.Equal(["/first", "/second"], directories.ConfigDirectories);
        Assert.Equal(["/one", "/two"], directories.DataDirectories);
    }

    [Fact]
    public void Empty_and_relative_values_fall_back_to_the_defaults()
    {
        var result = XdgDirectories.Create(Environment(
            config: string.Empty,
            state: "relative/state",
            data: " ",
            cache: "/custom/cache",
            runtime: "relative/run",
            configDirectories: "relative:/valid",
            dataDirectories: " :/also-valid"));

        Assert.True(result.IsSuccess);

        var directories = result.Value;
        Assert.Equal("/home/user/.config", directories.ConfigHome);
        Assert.Equal("/home/user/.local/state", directories.StateHome);
        Assert.Equal("/home/user/.local/share", directories.DataHome);
        Assert.Equal("/custom/cache", directories.CacheHome);
        Assert.Null(directories.RuntimeDirectory);
        Assert.Equal(["/valid"], directories.ConfigDirectories);
        Assert.Equal(["/also-valid"], directories.DataDirectories);
    }

    [Fact]
    public void Directory_lists_fall_back_when_no_entry_is_absolute()
    {
        var result = XdgDirectories.Create(Environment(
            configDirectories: "relative:also-relative",
            dataDirectories: ":"));

        Assert.True(result.IsSuccess);
        Assert.Equal(["/etc/xdg"], result.Value.ConfigDirectories);
        Assert.Equal(["/usr/local/share", "/usr/share"], result.Value.DataDirectories);
    }

    [Fact]
    public void A_missing_home_is_a_failure()
    {
        var result = XdgDirectories.Create(Environment(home: null));

        Assert.True(result.IsFailure);
        Assert.Equal("xdg.home_unset", result.Error.Code);
    }

    [Fact]
    public void A_relative_home_is_a_failure()
    {
        var result = XdgDirectories.Create(Environment(home: "home/user"));

        Assert.True(result.IsFailure);
        Assert.Equal("xdg.home_unset", result.Error.Code);
    }

    [Fact]
    public void The_root_home_is_accepted()
    {
        var result = XdgDirectories.Create(Environment(home: "/"));

        Assert.True(result.IsSuccess);
        Assert.Equal("/.config", result.Value.ConfigHome);
    }

    [Fact]
    public void From_environment_resolves_the_process_environment()
    {
        var result = XdgDirectories.FromEnvironment();

        Assert.True(result.IsSuccess);
        Assert.True(Path.IsPathRooted(result.Value.ConfigHome));
        Assert.True(Path.IsPathRooted(result.Value.StateHome));
        Assert.NotEmpty(result.Value.ConfigDirectories);
        Assert.NotEmpty(result.Value.DataDirectories);
    }

    private static XdgEnvironment Environment(
        string? home = "/home/user",
        string? config = null,
        string? state = null,
        string? data = null,
        string? cache = null,
        string? runtime = null,
        string? configDirectories = null,
        string? dataDirectories = null)
    {
        return new XdgEnvironment(
            home,
            config,
            state,
            data,
            cache,
            runtime,
            configDirectories,
            dataDirectories);
    }
}
