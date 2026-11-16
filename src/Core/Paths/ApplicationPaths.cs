// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Primitives;
using Core.Results;
namespace Core.Paths;

/// <summary>
/// The per-application directories and files derived from a <see cref="PathSet"/>
/// </summary>
/// <remarks>
/// Every directory is namespaced by vendor and application, so an application
/// reads and writes only below <c>&lt;base&gt;/&lt;vendor&gt;/&lt;application&gt;</c>.
/// </remarks>
public sealed class ApplicationPaths
{
    private ApplicationPaths(
        string configDirectory,
        string stateDirectory,
        string dataDirectory,
        string cacheDirectory)
    {
        ConfigDirectory = configDirectory;
        StateDirectory = stateDirectory;
        DataDirectory = dataDirectory;
        CacheDirectory = cacheDirectory;
    }

    /// <summary>Gets the directory an application stores its configuration in.</summary>
    public string ConfigDirectory { get; }

    /// <summary>Gets the directory an application stores its persisted state in.</summary>
    public string StateDirectory { get; }

    /// <summary>Gets the directory an application stores its data in.</summary>
    public string DataDirectory { get; }

    /// <summary>Gets the directory an application stores its cache in.</summary>
    public string CacheDirectory { get; }

    /// <summary>
    /// Creates the application paths for a vendor and application name.
    /// </summary>
    /// <param name="directories">The resolved base directories.</param>
    /// <param name="vendor">The vendor namespace, for example <c>core</c>.</param>
    /// <param name="application">The application name.</param>
    /// <returns>The application paths, or a <see cref="CoreError"/> when a name is not a valid path component.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="directories"/> is <see langword="null"/>.</exception>
    public static Result<ApplicationPaths> Create(
        PathSet directories,
        string vendor,
        string application)
    {
        ArgumentNullException.ThrowIfNull(directories);

        if (ValidateComponent(vendor) is { } vendorError)
        {
            return Result<ApplicationPaths>.Failure(vendorError);
        }

        if (ValidateComponent(application) is { } applicationError)
        {
            return Result<ApplicationPaths>.Failure(applicationError);
        }

        return Result<ApplicationPaths>.Success(new ApplicationPaths(
            Path.Combine(directories.ConfigHome, vendor, application),
            Path.Combine(directories.StateHome, vendor, application),
            Path.Combine(directories.DataHome, vendor, application),
            Path.Combine(directories.CacheHome, vendor, application)));
    }

    /// <summary>
    /// Gets the path of a configuration file below <see cref="ConfigDirectory"/>.
    /// </summary>
    /// <param name="name">The file name, relative to the configuration directory.</param>
    /// <returns>The combined path.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    public string ConfigFile(string name) => Combine(ConfigDirectory, name);

    /// <summary>
    /// Gets the path of a state file below <see cref="StateDirectory"/>.
    /// </summary>
    /// <param name="name">The file name, relative to the state directory.</param>
    /// <returns>The combined path.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    public string StateFile(string name) => Combine(StateDirectory, name);

    /// <summary>
    /// Gets the path of a data file below <see cref="DataDirectory"/>.
    /// </summary>
    /// <param name="name">The file name, relative to the data directory.</param>
    /// <returns>The combined path.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    public string DataFile(string name) => Combine(DataDirectory, name);

    /// <summary>
    /// Gets the path of a cache file below <see cref="CacheDirectory"/>.
    /// </summary>
    /// <param name="name">The file name, relative to the cache directory.</param>
    /// <returns>The combined path.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    public string CacheFile(string name) => Combine(CacheDirectory, name);

    private static string Combine(string directory, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var validated = PathComponent.Validate(name);
        if (validated.TryGetError(out var error))
        {
            throw new ArgumentException(error.Message, nameof(name));
        }

        return Path.Combine(directory, validated.Value);
    }

    private static CoreError? ValidateComponent(string value)
    {
        var validated = PathComponent.Validate(value);

        return validated.TryGetError(out var error) ? error : null;
    }
}
