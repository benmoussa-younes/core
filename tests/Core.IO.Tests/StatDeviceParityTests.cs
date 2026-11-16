// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform.Linux;

namespace Core.IO.Tests;

public sealed class StatDeviceParityTests
{
    [Fact]
    public void The_device_is_identical_on_the_statx_and_fstatat_paths()
    {
        // /tmp is a major-0 device where the two encodings coincide; use the test
        // output directory on the real device so the packing is exercised.
        var path = AppContext.BaseDirectory;

        Assert.True(LinuxFileSystem.TryStat(path, out var statx));
        Assert.True(LinuxFileSystem.TryStatWithFallback(path, out var fallback));

        Assert.Equal(statx.Inode, fallback.Inode);
        Assert.Equal(statx.Device, fallback.Device);
    }
}
