// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Concurrency.Tests;

/// <summary>
/// The collection the few timing-sensitive tests share so they run one at a time.
/// </summary>
/// <remarks>
/// xUnit runs test classes in parallel by default; a measurement of the library's
/// own cost taken under that contention measures the contention, not the library.
/// <see cref="CollectionDefinitionAttribute.DisableParallelization"/> serializes
/// every class in this collection. The contention/buffer tests keep their exact
/// numbers in the spike and the AOT probe; here they are coarse regression guards.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingSensitiveCollection
{
    /// <summary>The collection name used by <see cref="CollectionAttribute"/>.</summary>
    public const string Name = "TimingSensitive";
}
