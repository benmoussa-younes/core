// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform.Linux;

namespace Core.Platform.Linux.Tests;

[Collection("process-limits")]
public sealed class UnixProcessControlTests
{
    [Fact]
    public void DumpableFlagRoundTripsAndRestores()
    {
        Assert.True(UnixProcessControl.TryGetDumpable(out var original, out var error));
        Assert.Equal(LinuxErrno.None, error);

        try
        {
            Assert.True(UnixProcessControl.TrySetDumpable(!original, out error));
            Assert.Equal(LinuxErrno.None, error);
            Assert.True(UnixProcessControl.TryGetDumpable(out var changed, out _));
            Assert.Equal(!original, changed);
        }
        finally
        {
            Assert.True(UnixProcessControl.TrySetDumpable(original, out _));
            Assert.True(UnixProcessControl.TryGetDumpable(out var restored, out _));
            Assert.Equal(original, restored);
        }
    }

    [Fact]
    public void NoNewPrivilegesFlagIsReadable()
    {
        Assert.True(UnixProcessControl.TryGetNoNewPrivileges(out _, out var error));
        Assert.Equal(LinuxErrno.None, error);
    }

    [Fact]
    public void ThreadNameRoundTripsAndRestores()
    {
        Assert.True(UnixProcessControl.TryGetName(out var original, out _));

        try
        {
            Assert.True(UnixProcessControl.TrySetName("core-test", out var error));
            Assert.Equal(LinuxErrno.None, error);
            Assert.True(UnixProcessControl.TryGetName(out var name, out _));
            Assert.Equal("core-test", name);
        }
        finally
        {
            Assert.True(UnixProcessControl.TrySetName(original, out _));
        }
    }

    [Fact]
    public void ThreadNameLongerThanFifteenBytesIsRejected()
    {
        Assert.Throws<ArgumentException>(() => UnixProcessControl.TrySetName("0123456789abcdef", out _));
    }
}
