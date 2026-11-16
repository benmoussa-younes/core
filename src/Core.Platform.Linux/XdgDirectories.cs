// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Paths;
using Core.Results;

namespace Core.Platform.Linux;

/// <summary>
/// Resolves the XDG base directories into a <see cref="PathSet"/>, following the
/// XDG Base Directory Specification 0.8.
/// </summary>
/// <remarks>
/// An environment value is honoured only when it is an absolute path; an empty
/// or relative value is ignored and the specification default is used. Only
/// <see cref="PathSet.RuntimeDirectory"/> has no default, because the
/// specification leaves it to the session manager.
/// </remarks>
public static class XdgDirectories
{
    private const string ConfigHomeVariable = "XDG_CONFIG_HOME";
    private const string StateHomeVariable = "XDG_STATE_HOME";
    private const string DataHomeVariable = "XDG_DATA_HOME";
    private const string CacheHomeVariable = "XDG_CACHE_HOME";
    private const string RuntimeDirectoryVariable = "XDG_RUNTIME_DIR";
    private const string ConfigDirectoriesVariable = "XDG_CONFIG_DIRS";
    private const string DataDirectoriesVariable = "XDG_DATA_DIRS";

    /// <summary>
    /// Resolves the base directories from the current process environment.
    /// </summary>
    /// <returns>The resolved directories, or a <see cref="CoreError"/> when the home directory is unavailable.</returns>
    public static Result<PathSet> FromEnvironment()
    {
        return Create(new XdgEnvironment(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable(ConfigHomeVariable),
            Environment.GetEnvironmentVariable(StateHomeVariable),
            Environment.GetEnvironmentVariable(DataHomeVariable),
            Environment.GetEnvironmentVariable(CacheHomeVariable),
            Environment.GetEnvironmentVariable(RuntimeDirectoryVariable),
            Environment.GetEnvironmentVariable(ConfigDirectoriesVariable),
            Environment.GetEnvironmentVariable(DataDirectoriesVariable)));
    }

    /// <summary>
    /// Resolves the base directories from an explicit environment snapshot.
    /// </summary>
    /// <param name="environment">The environment snapshot to resolve.</param>
    /// <returns>The resolved directories, or a <see cref="CoreError"/> when the home directory is unavailable.</returns>
    public static Result<PathSet> Create(XdgEnvironment environment)
    {
        var home = AbsoluteOrNull(environment.HomeDirectory);

        if (home is null)
        {
            return Result<PathSet>.Failure(new CoreError(
                "xdg.home_unset",
                "The home directory is not set, so the XDG base directories cannot be resolved."));
        }

        return Result<PathSet>.Success(new PathSet(
            AbsoluteOrNull(environment.ConfigHome) ?? Path.Combine(home, ".config"),
            AbsoluteOrNull(environment.StateHome) ?? Path.Combine(home, ".local", "state"),
            AbsoluteOrNull(environment.DataHome) ?? Path.Combine(home, ".local", "share"),
            AbsoluteOrNull(environment.CacheHome) ?? Path.Combine(home, ".cache"),
            AbsoluteOrNull(environment.RuntimeDirectory),
            ParseDirectories(environment.ConfigDirectories, "/etc/xdg"),
            ParseDirectories(environment.DataDirectories, "/usr/local/share", "/usr/share")));
    }

    private static string? AbsoluteOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value) ? null : value;
    }

    private static string[] ParseDirectories(string? value, params string[] defaults)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaults;
        }

        var entries = value.Split(':');
        var directories = new List<string>(entries.Length);

        foreach (var entry in entries)
        {
            if (AbsoluteOrNull(entry) is { } directory)
            {
                directories.Add(directory);
            }
        }

        return directories.Count > 0 ? [.. directories] : defaults;
    }
}
