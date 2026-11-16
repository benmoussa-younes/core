// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Primitives;

namespace Core.Serialization.Storage;

/// <summary>
/// Validates a store file name as a single path component, so a store can never
/// read or write outside its directory.
/// </summary>
internal static class DataFilePath
{
    internal static string Combine(string directory, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var validated = PathComponent.Validate(name);
        if (validated.TryGetError(out var error))
        {
            throw new ArgumentException(error.Message, nameof(name));
        }

        return Path.Combine(directory, validated.Value);
    }
}
