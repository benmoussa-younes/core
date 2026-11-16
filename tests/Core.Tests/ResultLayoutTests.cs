// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.CompilerServices;
using Core.Results;

namespace Core.Tests;

public sealed class ResultLayoutTests
{
    [Fact]
    public void The_result_struct_is_two_references_wide()
    {
        // The failure flag is derived from the error reference, so a value and a
        // reference result are both two machine words. Guarded so a re-added bool
        // cannot silently grow every Result<T>.
        Assert.Equal(16, Unsafe.SizeOf<Result<long>>());
        Assert.Equal(16, Unsafe.SizeOf<Result<string>>());
    }

    [Fact]
    public void The_default_result_is_a_success()
    {
        Result<int> result = default;

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(0, result.Value);
    }
}
