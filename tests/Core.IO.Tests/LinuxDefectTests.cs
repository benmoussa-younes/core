// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform.Linux;
using Core.IO.Walking;

namespace Core.IO.Tests;

public sealed class LinuxDefectTests
{
    [Fact]
    public void The_entry_name_buffer_is_bounded()
    {
        var buffer = new byte[16];

        Assert.Equal(4, DirectoryWalker.EncodeName("abc", buffer));
        Assert.Equal(0, buffer[3]);

        Assert.Throws<InvalidOperationException>(() => DirectoryWalker.EncodeName(new string('x', 20), buffer));
    }

    [Fact]
    public void A_default_walk_policy_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new DirectoryWalker(default!, new LinuxFileSystemAdapter()));
    }

    [Fact]
    public void Syncing_a_null_directory_is_rejected_clearly()
    {
        Assert.Throws<ArgumentNullException>(() => LinuxFileSystem.TrySyncDirectory(null!));
    }

    [Fact]
    public void An_out_of_range_timestamp_fails_loudly()
    {
        Assert.Equal(1_500_000_000L, LinuxStatLayout.ToUnixNanoseconds(1, 500_000_000));
        Assert.Throws<OverflowException>(() => LinuxStatLayout.ToUnixNanoseconds(long.MaxValue, 0));
    }

    [Fact]
    public void Advising_a_bad_descriptor_fails()
    {
        // A closed descriptor is EBADF; the error path must return false, not throw.
        Assert.False(LinuxFileSystem.TryAdvise(descriptor: -1, 0, 0, LinuxFileAdvice.Normal));
    }
}
