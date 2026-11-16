// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.IO.Walking;
using Core.Primitives;
namespace Core.IO.Tests;

public sealed class WalkOptionsTests
{
    [Fact]
    public void ExposesItsConfiguration()
    {
        var includes = new[] { PathPattern.Create("*.txt").Value };
        var excludes = new[] { PathPattern.Create("*.tmp").Value };

        var options = new WalkOptions(includes, excludes, 3, followSymbolicLinks: true);

        Assert.Same(includes, options.Includes);
        Assert.Same(excludes, options.Excludes);
        Assert.Equal(3, options.MaximumDepth);
        Assert.True(options.FollowSymbolicLinks);
        Assert.False(options.IncludePseudoFilesystems);
    }

    [Fact]
    public void IncludesPseudoFilesystemsWhenRequested()
    {
        var options = new WalkOptions([], [], 0, followSymbolicLinks: false, includePseudoFilesystems: true);

        Assert.True(options.IncludePseudoFilesystems);
    }

    [Fact]
    public void RejectsNullPatternLists()
    {
        Assert.Throws<ArgumentNullException>(() => new WalkOptions(null!, [], 0, false));
        Assert.Throws<ArgumentNullException>(() => new WalkOptions([], null!, 0, false));
    }

    [Fact]
    public void RejectsANegativeDepth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WalkOptions([], [], -1, false));
    }
}
