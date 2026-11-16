// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Paths;
using Core.Results;

namespace Core.Platform.Linux;

/// <summary>
/// The Linux path resolver: it reads the XDG base directories, or an explicit
/// <see cref="XdgEnvironment"/> snapshot for a deterministic test.
/// </summary>
/// <remarks>
/// A product composes it once into a <see cref="Core.Platform.CorePlatform"/> and
/// hands that platform to the host. There is no global state and no process
/// environment read when an explicit snapshot is supplied.
/// </remarks>
public sealed class XdgPathResolver : Core.Platform.IPathResolver
{
    private readonly XdgEnvironment? _environment;

    /// <summary>Initializes a resolver that reads the current process environment.</summary>
    public XdgPathResolver()
    {
    }

    /// <summary>Initializes a resolver over an explicit environment snapshot.</summary>
    /// <param name="environment">The environment snapshot.</param>
    public XdgPathResolver(XdgEnvironment environment)
    {
        _environment = environment;
    }

    /// <inheritdoc />
    public Result<PathSet> Resolve()
    {
        return _environment is { } environment
            ? XdgDirectories.Create(environment)
            : XdgDirectories.FromEnvironment();
    }
}
