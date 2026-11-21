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

    [Fact]
    public void Scrub_redacts_a_home_path_embedded_in_a_message()
    {
        // A stack trace carries the path after " in ", which ForMessage leaves alone.
        var text = "   at App.Main() in /home/user/src/Program.cs:line 42";

        Assert.Equal(
            "   at App.Main() in ~/src/Program.cs:line 42",
            PathRedaction.Scrub(text, "/home/user"));
        Assert.Equal("~/a then ~/b", PathRedaction.Scrub("/home/user/a then /home/user/b", "/home/user"));
    }

    [Fact]
    public void Scrub_redacts_before_punctuation_quotes_and_an_escaped_newline()
    {
        // A log line escapes control characters first, so a real newline arrives as
        // the two characters \n; a quote or a comma is just as much a boundary.
        Assert.Equal("'~'", PathRedaction.Scrub("'/home/user'", "/home/user"));
        Assert.Equal("~/a.txt.", PathRedaction.Scrub("/home/user/a.txt.", "/home/user"));
        Assert.Equal("Path=~, missing", PathRedaction.Scrub("Path=/home/user, missing", "/home/user"));
        Assert.Equal("~\\n/file.txt", PathRedaction.Scrub("/home/user\\n/file.txt", "/home/user"));
    }

    [Fact]
    public void Scrub_leaves_a_sibling_account_and_unrelated_text_alone()
    {
        Assert.Equal("/home/user2/src/file.cs", PathRedaction.Scrub("/home/user2/src/file.cs", "/home/user"));
        Assert.Equal("nothing here", PathRedaction.Scrub("nothing here", "/home/user"));
        Assert.Equal("/home/user/a", PathRedaction.Scrub("/home/user/a", string.Empty));
        Assert.Equal(string.Empty, PathRedaction.Scrub(null, "/home/user"));
        Assert.Throws<ArgumentNullException>(() => PathRedaction.Scrub("text", null!));
    }
}
