// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform.Linux;

/// <summary>
/// The raw environment values the XDG base directory resolution reads, taken as
/// an explicit snapshot so resolution is testable without touching the process
/// environment.
/// </summary>
public readonly struct XdgEnvironment
{
    /// <summary>
    /// Initializes a snapshot from the environment values.
    /// </summary>
    /// <param name="homeDirectory">The user's home directory (<c>$HOME</c>), or <see langword="null"/> when unset.</param>
    /// <param name="configHome">The value of <c>$XDG_CONFIG_HOME</c>, or <see langword="null"/> when unset.</param>
    /// <param name="stateHome">The value of <c>$XDG_STATE_HOME</c>, or <see langword="null"/> when unset.</param>
    /// <param name="dataHome">The value of <c>$XDG_DATA_HOME</c>, or <see langword="null"/> when unset.</param>
    /// <param name="cacheHome">The value of <c>$XDG_CACHE_HOME</c>, or <see langword="null"/> when unset.</param>
    /// <param name="runtimeDirectory">The value of <c>$XDG_RUNTIME_DIR</c>, or <see langword="null"/> when unset.</param>
    /// <param name="configDirectories">The value of <c>$XDG_CONFIG_DIRS</c>, or <see langword="null"/> when unset.</param>
    /// <param name="dataDirectories">The value of <c>$XDG_DATA_DIRS</c>, or <see langword="null"/> when unset.</param>
    public XdgEnvironment(
        string? homeDirectory,
        string? configHome,
        string? stateHome,
        string? dataHome,
        string? cacheHome,
        string? runtimeDirectory,
        string? configDirectories,
        string? dataDirectories)
    {
        HomeDirectory = homeDirectory;
        ConfigHome = configHome;
        StateHome = stateHome;
        DataHome = dataHome;
        CacheHome = cacheHome;
        RuntimeDirectory = runtimeDirectory;
        ConfigDirectories = configDirectories;
        DataDirectories = dataDirectories;
    }

    /// <summary>Gets the user's home directory (<c>$HOME</c>).</summary>
    public string? HomeDirectory { get; }

    /// <summary>Gets the <c>$XDG_CONFIG_HOME</c> value.</summary>
    public string? ConfigHome { get; }

    /// <summary>Gets the <c>$XDG_STATE_HOME</c> value.</summary>
    public string? StateHome { get; }

    /// <summary>Gets the <c>$XDG_DATA_HOME</c> value.</summary>
    public string? DataHome { get; }

    /// <summary>Gets the <c>$XDG_CACHE_HOME</c> value.</summary>
    public string? CacheHome { get; }

    /// <summary>Gets the <c>$XDG_RUNTIME_DIR</c> value.</summary>
    public string? RuntimeDirectory { get; }

    /// <summary>Gets the <c>$XDG_CONFIG_DIRS</c> value.</summary>
    public string? ConfigDirectories { get; }

    /// <summary>Gets the <c>$XDG_DATA_DIRS</c> value.</summary>
    public string? DataDirectories { get; }
}
