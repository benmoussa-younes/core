// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Primitives;

namespace Core.Paths;

/// <summary>
/// The file name a single-instance socket uses.
/// </summary>
/// <remarks>
/// The name carries the vendor and the application, like the state directory
/// does, so two products of different vendors that share an application name
/// never share one lock. Each part is a validated path component: the name is a
/// file name, never a path.
/// </remarks>
public static class SingleInstanceSocketName
{
    /// <summary>
    /// Builds the socket file name for a product whose runtime directory is
    /// already per-user, without the redundant user part.
    /// </summary>
    /// <param name="vendor">The vendor namespace, for example <c>core</c>.</param>
    /// <param name="application">The application name.</param>
    /// <returns>The file name, ending in <c>.socket</c>, without a directory.</returns>
    /// <remarks>
    /// This name assumes <c>XDG_RUNTIME_DIR</c> is per-user and owner-only, as the
    /// XDG base directory specification requires. A runtime directory shared by two
    /// users would let their instances collide, so the three-argument overload that
    /// carries the user stays the safe default when the directory is not known to be
    /// per-user.
    /// </remarks>
    /// <exception cref="ArgumentException">A part is not a valid path component.</exception>
    public static string For(string vendor, string application)
    {
        return string.Concat(
            Component(vendor, nameof(vendor)),
            "-",
            Component(application, nameof(application)),
            ".socket");
    }

    /// <summary>Builds the socket file name for a product and a user.</summary>
    /// <param name="vendor">The vendor namespace, for example <c>core</c>.</param>
    /// <param name="application">The application name.</param>
    /// <param name="user">The user name.</param>
    /// <returns>The file name, ending in <c>.sock</c>, without a directory.</returns>
    /// <exception cref="ArgumentException">A part is not a valid path component.</exception>
    public static string For(string vendor, string application, string user)
    {
        return string.Concat(
            Component(vendor, nameof(vendor)),
            "-",
            Component(application, nameof(application)),
            "-",
            Component(user, nameof(user)),
            ".sock");
    }

    private static string Component(string value, string parameterName)
    {
        var validated = PathComponent.Validate(value);

        if (validated.TryGetError(out var error))
        {
            throw new ArgumentException(error.Message, parameterName);
        }

        return validated.Value;
    }
}
