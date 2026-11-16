// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.Versioning;

namespace Core.Platform.Linux;

/// <summary>
/// The Linux composition a product hands to the Core host.
/// </summary>
[SupportedOSPlatform("linux")]
public static class LinuxPlatform
{
    /// <summary>Creates the Linux platform composition.</summary>
    /// <param name="paths">The path resolver.</param>
    /// <returns>The composition, including the Linux host services.</returns>
    public static CorePlatform Create(IPathResolver paths) =>
        new(paths, new LinuxCoreHostServices());
}
