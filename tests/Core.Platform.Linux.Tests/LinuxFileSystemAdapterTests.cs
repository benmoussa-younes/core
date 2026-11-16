// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform.Linux;
using Core.Platform;

namespace Core.Platform.Linux.Tests;

public sealed class LinuxFileSystemAdapterTests
{
    [Fact]
    public void It_maps_the_linux_surface_to_the_portable_file_system()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("data.bin", "data");
        IFileSystem fileSystem = new LinuxFileSystemAdapter();

        Assert.True(fileSystem.TryStat(path, out var status));
        Assert.Equal(FileType.RegularFile, status.Type);
        Assert.Equal(4, status.Size);
        Assert.True(fileSystem.TryAccess(path, FileAccessMode.Read));

        Assert.True(fileSystem.TryOpenRead(path, noFollow: false, out var handle));
        Assert.NotNull(handle);

        using (handle)
        {
            Assert.True(fileSystem.TryStatDescriptor(handle!, out var descriptor));
            Assert.Equal(FileType.RegularFile, descriptor.Type);
            Assert.Equal(status.Device, descriptor.Device);
            Assert.Equal(status.Inode, descriptor.Inode);
        }

        Assert.True(fileSystem.TrySyncDirectory(temp.Path));
    }
}
