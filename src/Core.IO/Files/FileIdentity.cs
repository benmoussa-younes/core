// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.IO.Files;

/// <summary>
/// The identity of a file-system object: the device it lives on and its inode.
/// It is enough to detect that a path was swapped (a rename or a symbolic link)
/// between the walk that discovered it and the read that consumes it.
/// </summary>
/// <param name="Device">The device the object lives on.</param>
/// <param name="Inode">The object's inode.</param>
public readonly record struct FileIdentity(ulong Device, ulong Inode)
{
    /// <summary>Gets an unknown identity; a consumer skips identity verification for it.</summary>
    public static FileIdentity Unknown => default;

    /// <summary>Gets a value indicating whether the identity is known (a non-zero inode).</summary>
    public bool IsKnown => Inode != 0;
}
