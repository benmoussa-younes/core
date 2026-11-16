// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Paths;

/// <summary>
/// The base directories an application resolves its files below.
/// </summary>
/// <remarks>
/// The set is platform-neutral: a Linux resolver fills it from the XDG base
/// directories, a Windows resolver from the known folders, and a test from an
/// explicit fixture. The directories are absolute and already resolved; the
/// application namespacing (vendor and application) belongs to
/// <c>ApplicationPaths</c>.
/// </remarks>
public sealed class PathSet
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PathSet"/> class.
    /// </summary>
    /// <param name="configHome">The user configuration base directory.</param>
    /// <param name="stateHome">The user state base directory.</param>
    /// <param name="dataHome">The user data base directory.</param>
    /// <param name="cacheHome">The user cache base directory.</param>
    /// <param name="runtimeDirectory">The per-session runtime directory, or <see langword="null"/> when none is available.</param>
    /// <param name="configDirectories">The preference-ordered configuration search directories.</param>
    /// <param name="dataDirectories">The preference-ordered data search directories.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public PathSet(
        string configHome,
        string stateHome,
        string dataHome,
        string cacheHome,
        string? runtimeDirectory,
        IReadOnlyList<string> configDirectories,
        IReadOnlyList<string> dataDirectories)
    {
        ArgumentNullException.ThrowIfNull(configHome);
        ArgumentNullException.ThrowIfNull(stateHome);
        ArgumentNullException.ThrowIfNull(dataHome);
        ArgumentNullException.ThrowIfNull(cacheHome);
        ArgumentNullException.ThrowIfNull(configDirectories);
        ArgumentNullException.ThrowIfNull(dataDirectories);

        ConfigHome = configHome;
        StateHome = stateHome;
        DataHome = dataHome;
        CacheHome = cacheHome;
        RuntimeDirectory = runtimeDirectory;
        ConfigDirectories = configDirectories;
        DataDirectories = dataDirectories;
    }

    /// <summary>Gets the user configuration base directory.</summary>
    public string ConfigHome { get; }

    /// <summary>Gets the user state base directory.</summary>
    public string StateHome { get; }

    /// <summary>Gets the user data base directory.</summary>
    public string DataHome { get; }

    /// <summary>Gets the user cache base directory.</summary>
    public string CacheHome { get; }

    /// <summary>Gets the per-session runtime directory, or <see langword="null"/> when none is available.</summary>
    public string? RuntimeDirectory { get; }

    /// <summary>Gets the preference-ordered configuration search directories.</summary>
    public IReadOnlyList<string> ConfigDirectories { get; }

    /// <summary>Gets the preference-ordered data search directories.</summary>
    public IReadOnlyList<string> DataDirectories { get; }
}
