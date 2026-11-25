// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.Versioning;

namespace Core.Platform.Linux;

/// <summary>
/// Applies a process mask and restores the previous one on dispose.
/// </summary>
[SupportedOSPlatform("linux")]
public readonly struct UnixMaskScope : IDisposable
{
    private readonly UnixFileMode _previous;
    private readonly bool _entered;

    private UnixMaskScope(UnixFileMode previous)
    {
        _previous = previous;
        _entered = true;
    }

    /// <summary>Gets the mask in force before the scope was entered.</summary>
    public UnixFileMode Previous => _previous;

    /// <summary>Applies a process mask and remembers the previous one.</summary>
    /// <param name="mask">The bits to remove from every created file.</param>
    /// <returns>The scope that restores the previous mask on dispose.</returns>
    public static UnixMaskScope Enter(UnixFileMode mask)
    {
        return new UnixMaskScope(UnixProcessMask.Replace(mask));
    }

    /// <summary>Restores the mask in force before the scope was entered.</summary>
    /// <remarks>A default-initialized scope does nothing, so it can never clear the mask.</remarks>
    public void Dispose()
    {
        if (!_entered)
        {
            return;
        }

        _ = UnixProcessMask.Replace(_previous);
    }
}
