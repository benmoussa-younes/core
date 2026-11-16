// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.InteropServices;
using Core.Platform.Linux;

namespace Core.IO.Tests;

/// <summary>
/// The process umask is global state, so the tests that set it run in a
/// collection xUnit never runs in parallel with another collection.
/// </summary>
[CollectionDefinition("process-umask", DisableParallelization = true)]
public sealed class ProcessUmaskCollection
{
}

[Collection("process-umask")]
public sealed class AtomicFileWriterUmaskTests
{
    private const UnixFileMode AllPermissions =
        UnixFileMode.UserRead | UnixFileMode.UserWrite |
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite;

    // 0077: the group and other bits are removed from a newly created file.
    private const uint Umask0077 = 0b000_111_111;

    [Fact]
    public void ANewTargetRespectsTheProcessUmask()
    {
        using var temp = new TempDirectory();
        var target = temp.FullPath("artifact.ndjson");

        var previous = SetProcessUmask(Umask0077);

        try
        {
            using (var writer = new AtomicFileWriter(target, AllPermissions))
            {
                writer.Stream.Write("x"u8);
                Assert.True(writer.Commit().IsSuccess);
            }
        }
        finally
        {
            _ = SetProcessUmask(previous);
        }

        // 0666 & ~0077 = 0600: the constructor's mode under the process umask,
        // never the permissive mode itself.
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(target));
    }

    [DllImport("libc", EntryPoint = "umask", SetLastError = false)]
    private static extern uint SetProcessUmask(uint mask);
}
