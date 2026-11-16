// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.IO.Pipeline;

/// <summary>
/// Configures a <see cref="BlockPipeline"/> run.
/// </summary>
public readonly struct BlockPipelineOptions
{
    /// <summary>
    /// The largest number of worker threads a pipeline may use.
    /// </summary>
    public const int MaximumThreads = 1024;

    /// <summary>
    /// Initializes the options with a 32-item first block, a 512-item maximum block
    /// and 16 in-flight blocks per thread.
    /// </summary>
    public BlockPipelineOptions()
    {
    }

    /// <summary>
    /// Gets the number of items in the first block. The block size doubles every
    /// <c>threads</c> produced blocks until it reaches <see cref="MaximumBlockSize"/>.
    /// </summary>
    public int InitialBlockSize { get; init; } = 32;

    /// <summary>
    /// Gets the largest number of items a block may hold.
    /// </summary>
    public int MaximumBlockSize { get; init; } = 512;

    /// <summary>
    /// Gets the number of produced-but-unemitted blocks allowed per worker thread.
    /// The total in-flight bound is <c>threads * InFlightBlocksPerThread</c>.
    /// </summary>
    public int InFlightBlocksPerThread { get; init; } = 16;

    /// <summary>
    /// Gets the default options: block sizes from 32 to 512 and 16 in-flight blocks per thread.
    /// </summary>
    public static BlockPipelineOptions Default { get; } = new();
}
