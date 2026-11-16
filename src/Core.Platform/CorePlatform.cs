// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform;

/// <summary>
/// The explicit platform composition a product hands to the Core host.
/// </summary>
/// <remarks>
/// There is no container and no reflection: a product creates the platform once
/// (for example <c>LinuxPlatform.Create()</c>) and passes it to the host. As the
/// ports grow, the properties are added here, so every consumer sees one
/// composition object instead of a service locator.
/// </remarks>
public sealed class CorePlatform
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CorePlatform"/> class.
    /// </summary>
    /// <param name="paths">The path resolver.</param>
    /// <param name="hostServices">
    /// The platform-owned host services, or <see langword="null"/> for a consumer
    /// that does not create a UI host.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="paths"/> is <see langword="null"/>.</exception>
    public CorePlatform(IPathResolver paths, ICoreHostServices? hostServices = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        Paths = paths;
        HostServices = hostServices;
    }

    /// <summary>Gets the path resolver.</summary>
    public IPathResolver Paths { get; }

    /// <summary>Gets the host services, or <see langword="null"/> when none were supplied.</summary>
    public ICoreHostServices? HostServices { get; }
}
