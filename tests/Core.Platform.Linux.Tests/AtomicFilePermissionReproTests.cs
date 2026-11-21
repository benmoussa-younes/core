// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform;

namespace Core.Platform.Linux.Tests;

// Reproduction cases for the owner-only permission findings. Each one states the
// guarantee the documentation makes; every case fails on the current tree.
public sealed class AtomicFilePermissionReproTests
{
    private const UnixFileMode OwnerOnly =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode WorldReadable =
        UnixFileMode.UserRead | UnixFileMode.UserWrite |
        UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const UnixFileMode GroupReadableDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute;

    [Fact]
    public void AnOwnerOnlyCommitOverAnExistingWorldReadableFileLeavesItOwnerOnly()
    {
        // JsonSettingsStore.Save documents a file created owner-only (0600) before
        // the rename. AtomicFileOptions.OwnerOnly is an explicit request, so it must
        // win over the "preserve the destination mode" rule when the destination is
        // an existing file that is world-readable.
        using var temp = new TempDirectory();
        var target = temp.WriteFile("settings.json", "{}");
        File.SetUnixFileMode(target, WorldReadable);

        var created = new LinuxAtomicFileWriterFactory()
            .Create(target, new AtomicFileOptions { OwnerOnly = true });

        Assert.True(created.IsSuccess);
        using (var writer = created.Value)
        {
            writer.Stream.Write("{\"theme\":\"Dark\"}"u8);
            Assert.True(writer.Commit().IsSuccess);
        }

        Assert.Equal(OwnerOnly, File.GetUnixFileMode(target));
    }

    [Fact]
    public void EnsureDirectoryKeepsAPreExistingGroupReadableDirectory()
    {
        // LinuxCoreHostServices.PrepareStateDirectory preserves a pre-existing 0750
        // state directory and only refuses a group- or world-writable one.
        // EnsureDirectory(ownerOnly: true) must not narrow a directory it did not
        // create: the store does not own the directory policy.
        using var temp = new TempDirectory();
        var state = temp.CreateDirectory("state");
        File.SetUnixFileMode(state, GroupReadableDirectory);

        new LinuxAtomicFileWriterFactory().EnsureDirectory(state, ownerOnly: true);

        Assert.Equal(GroupReadableDirectory, File.GetUnixFileMode(state));
    }

    [Fact]
    public void EnsureDirectoryDoesNotTightenADirectoryOwnedByAnotherUser()
    {
        // The chmod runs unconditionally on a pre-existing directory, so a caller
        // that names a directory it does not own gets EPERM even when create-if-
        // absent would have succeeded. /proc/sys is root-owned on every Linux host.
        if (Environment.IsPrivilegedProcess)
        {
            Assert.Skip("The case needs an unprivileged process.");
        }

        new LinuxAtomicFileWriterFactory().EnsureDirectory("/proc/sys", ownerOnly: true);
    }

    [Fact]
    public void TryStatRefusesAPathWithAnEmbeddedNul()
    {
        // The string overload encodes UTF-8 without rejecting an interior NUL, and
        // the kernel stops at the first NUL. The path below therefore stats the
        // real file and silently ignores the smuggled suffix.
        using var temp = new TempDirectory();
        var real = temp.WriteFile("real.txt", "x");

        Assert.False(LinuxFileSystem.TryStat(string.Concat(real, "\0", "/../../etc/shadow"), out _));
    }
}
