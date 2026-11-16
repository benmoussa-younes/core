// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;

namespace Core.Platform.Linux;

/// <summary>
/// The Linux <see cref="IMemoryMappingFactory"/>: raw <c>mmap</c>, measured at
/// 13,3 µs and 40 B per open versus 23,1 µs and 680 B for the BCL mapping.
/// </summary>
public sealed class LinuxMemoryMappingFactory : IMemoryMappingFactory
{
    /// <inheritdoc />
    public Result<IMemoryMapping> Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var opened = LinuxMemoryMapping.Open(path);

        if (opened.TryGetError(out var error))
        {
            return Result<IMemoryMapping>.Failure(error);
        }

        return Result<IMemoryMapping>.Success(opened.Value);
    }
}
