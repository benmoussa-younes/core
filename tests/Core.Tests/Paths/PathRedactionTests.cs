// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Primitives;

namespace Core.Tests;

public sealed class PathRedactionTests
{
    [Fact]
    public void It_shortens_the_home_prefix_and_leaves_other_paths()
    {
        Assert.Equal("~/documents/a.md", PathRedaction.ForMessage("/home/user/documents/a.md", "/home/user"));
        Assert.Equal("~", PathRedaction.ForMessage("/home/user", "/home/user"));
        Assert.Equal("/home/user2/a.md", PathRedaction.ForMessage("/home/user2/a.md", "/home/user"));
        Assert.Equal("/etc/hosts", PathRedaction.ForMessage("/etc/hosts", "/home/user"));
        Assert.Equal("relative/a.md", PathRedaction.ForMessage("relative/a.md", "/home/user"));
    }

    [Fact]
    public void It_handles_a_trailing_separator_empty_home_and_null_path()
    {
        Assert.Equal("~/a.md", PathRedaction.ForMessage("/home/user/a.md", "/home/user/"));
        Assert.Equal("/home/user/a.md", PathRedaction.ForMessage("/home/user/a.md", string.Empty));
        Assert.Equal(string.Empty, PathRedaction.ForMessage(null, "/home/user"));
        Assert.Equal("/a", PathRedaction.ForMessage("/a", "/"));
        Assert.Throws<ArgumentNullException>(() => PathRedaction.ForMessage("/a", null!));
    }
}
