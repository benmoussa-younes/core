// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.IO.Walking;
namespace Core.IO.Tests;

public sealed class WalkSkipReasonNamesTests
{
    [Theory]
    [InlineData(WalkSkipReason.SymbolicLink, "symlink")]
    [InlineData(WalkSkipReason.Cycle, "cycle")]
    [InlineData(WalkSkipReason.NonUtf8, "non-utf8")]
    [InlineData(WalkSkipReason.Unreadable, "unreadable")]
    [InlineData(WalkSkipReason.PseudoFilesystem, "pseudo_filesystem")]
    public void MapsEveryReasonToItsStableName(WalkSkipReason reason, string expected)
    {
        Assert.Equal(expected, WalkSkipReasonNames.ToName(reason));
    }

    [Fact]
    public void RejectsAnUndefinedReason()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WalkSkipReasonNames.ToName((WalkSkipReason)99));
    }
}
