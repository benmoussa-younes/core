// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;

namespace Core.Platform;

/// <summary>
/// Opens a file as an <see cref="IMemoryMapping"/>.
/// </summary>
/// <remarks>
/// A product supplies the platform factory (<c>LinuxMemoryMappingFactory</c> on
/// Linux) to <c>MappedFile.Open</c>; the default remains the portable BCL
/// mapping. A failure is a typed <see cref="CoreError"/>, never an exception.
/// </remarks>
public interface IMemoryMappingFactory
{
    /// <summary>
    /// Opens a file as a read-only mapping.
    /// </summary>
    /// <param name="path">The file to map.</param>
    /// <returns>The mapping, or a failure with code <c>io.unreadable</c>.</returns>
    Result<IMemoryMapping> Open(string path);
}
