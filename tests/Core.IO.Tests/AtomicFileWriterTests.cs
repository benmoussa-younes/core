// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text.RegularExpressions;
using Core.Platform.Linux;

namespace Core.IO.Tests;

public sealed class AtomicFileWriterTests
{
    private const UnixFileMode Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    // The writer requests a permissive mode, as the tools do; the tests below
    // assert on the target's actual mode, so a reset to `mode & ~umask` is
    // visible as a difference from the mode the target already had.
    private const UnixFileMode PermissiveMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite |
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite;

    [Fact]
    public void CommitIsObservableByReReadingTheTarget()
    {
        using var temp = new TempDirectory();
        var target = temp.FullPath("artifact.ndjson");

        using (var writer = new AtomicFileWriter(target, Mode))
        {
            writer.Stream.Write("hello"u8);
            Assert.True(writer.Commit().IsSuccess);
        }

        Assert.Equal("hello", File.ReadAllText(target));
        Assert.Empty(TemporaryFiles(temp));
    }

    [Fact]
    public void TheStreamIsInvalidAfterCommitAndAbort()
    {
        using var temp = new TempDirectory();

        using (var committed = new AtomicFileWriter(temp.FullPath("committed"), Mode))
        {
            committed.Stream.Write("x"u8);
            Assert.True(committed.Commit().IsSuccess);
            Assert.Throws<InvalidOperationException>(() => committed.Stream);
        }

        using var aborted = new AtomicFileWriter(temp.FullPath("aborted"), Mode);
        aborted.Abort();
        Assert.Throws<InvalidOperationException>(() => aborted.Stream);
    }

    [Fact]
    public void CommitCreatesTheTargetWithTheRequestedMode()
    {
        using var temp = new TempDirectory();
        var target = temp.FullPath("artifact.ndjson");

        using (var writer = new AtomicFileWriter(target, Mode))
        {
            writer.Stream.Write("x"u8);
            Assert.True(writer.Commit().IsSuccess);
        }

        Assert.Equal(Mode, File.GetUnixFileMode(target));
    }

    [Fact]
    public void CommitReplacesThePreviousContent()
    {
        using var temp = new TempDirectory();
        var target = temp.WriteFile("artifact.ndjson", "previous");

        using (var writer = new AtomicFileWriter(target, Mode))
        {
            writer.Stream.Write("new"u8);
            Assert.True(writer.Commit().IsSuccess);
        }

        Assert.Equal("new", File.ReadAllText(target));
    }

    [Fact]
    public void CommitPreservesTheExistingTargetMode()
    {
        using var temp = new TempDirectory();
        var target = temp.WriteFile("artifact.ndjson", "previous");
        File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        using (var writer = new AtomicFileWriter(target, PermissiveMode))
        {
            writer.Stream.Write("new"u8);
            Assert.True(writer.Commit().IsSuccess);
        }

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(target));
        Assert.Equal("new", File.ReadAllText(target));
    }

    [Fact]
    public void CommitPreservesARestrictiveExistingTargetMode()
    {
        using var temp = new TempDirectory();
        var target = temp.WriteFile("artifact.ndjson", "previous");
        File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.GroupRead);

        using (var writer = new AtomicFileWriter(target, PermissiveMode))
        {
            writer.Stream.Write("new"u8);
            Assert.True(writer.Commit().IsSuccess);
        }

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.GroupRead, File.GetUnixFileMode(target));
        Assert.Equal("new", File.ReadAllText(target));
    }

    [Fact]
    public void CommitDoesNotResetTheExistingTargetModeToTheRequestedMode()
    {
        // The pre-fix writer created the temporary file with the requested mode
        // and renamed it, so an existing 0600 target came back as
        // `0666 & ~umask` (0644 under umask 022). The mode must survive.
        using var temp = new TempDirectory();
        var target = temp.WriteFile("artifact.ndjson", "previous");
        File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        using (var writer = new AtomicFileWriter(target, PermissiveMode))
        {
            writer.Stream.Write("new"u8);
            Assert.True(writer.Commit().IsSuccess);
        }

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(target));
    }

    [Fact]
    public void CommitDoesNotCopyANonRegularDestinationMode()
    {
        using var temp = new TempDirectory();
        var target = temp.FullPath("artifact.ndjson");
        RawFileSystem.MakeFifo(target, 0x1A4);

        using (var writer = new AtomicFileWriter(target, Mode))
        {
            writer.Stream.Write("new"u8);
            Assert.True(writer.Commit().IsSuccess);
        }

        // The destination was a FIFO, not a regular file: the committed regular
        // file gets the writer's mode, not the FIFO's 0644.
        Assert.Equal(Mode, File.GetUnixFileMode(target));
        Assert.Equal("new", File.ReadAllText(target));
    }

    [Fact]
    public void CommitRefusesASymbolicLinkDestinationAndLeavesBothUntouched()
    {
        using var temp = new TempDirectory();
        var referent = temp.FullPath("referent.ndjson");
        File.WriteAllText(referent, "referent-content");
        File.SetUnixFileMode(referent, UnixFileMode.UserRead | UnixFileMode.GroupRead);
        var target = temp.CreateFileSymlink("artifact.ndjson", "referent.ndjson");

        using var writer = new AtomicFileWriter(target, Mode);
        writer.Stream.Write("new"u8);

        var result = writer.Commit();

        Assert.True(result.IsFailure);
        Assert.Equal("io.symbolic_link_target", result.Error.Code);
        Assert.True(IsSymbolicLink(target));
        Assert.Equal("referent-content", File.ReadAllText(referent));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.GroupRead, File.GetUnixFileMode(referent));
        Assert.Empty(TemporaryFiles(temp));
    }

    [Fact]
    public void CommitRefusesADanglingSymbolicLinkDestination()
    {
        using var temp = new TempDirectory();
        var target = temp.CreateFileSymlink("artifact.ndjson", "missing-referent.ndjson");

        using var writer = new AtomicFileWriter(target, Mode);
        writer.Stream.Write("new"u8);

        var result = writer.Commit();

        Assert.True(result.IsFailure);
        Assert.Equal("io.symbolic_link_target", result.Error.Code);
        Assert.True(IsSymbolicLink(target));
        Assert.False(File.Exists(temp.FullPath("missing-referent.ndjson")));
        Assert.Empty(TemporaryFiles(temp));
    }

    [Fact]
    public void AbortLeavesTheTargetByteIdentical()
    {
        using var temp = new TempDirectory();
        var target = temp.WriteFile("artifact.ndjson", "previous");

        using var writer = new AtomicFileWriter(target, Mode);
        writer.Stream.Write("new"u8);
        writer.Abort();

        Assert.Equal("previous", File.ReadAllText(target));
        Assert.Empty(TemporaryFiles(temp));
    }

    [Fact]
    public void AbortWithoutAPreviousTargetLeavesNoTarget()
    {
        using var temp = new TempDirectory();
        var target = temp.FullPath("artifact.ndjson");

        using var writer = new AtomicFileWriter(target, Mode);
        writer.Stream.Write("new"u8);
        writer.Abort();

        Assert.False(File.Exists(target));
        Assert.Empty(TemporaryFiles(temp));
    }

    [Fact]
    public void TheTargetAppearsOnlyOnCommit()
    {
        using var temp = new TempDirectory();
        var target = temp.FullPath("artifact.ndjson");

        using (var writer = new AtomicFileWriter(target, Mode))
        {
            writer.Stream.Write("half"u8);
            writer.Stream.Flush();

            Assert.False(File.Exists(target));
            Assert.Single(TemporaryFiles(temp));

            Assert.True(writer.Commit().IsSuccess);
        }

        Assert.Equal("half", File.ReadAllText(target));
    }

    [Fact]
    public void TheTemporaryFileIsNamedRecognisably()
    {
        using var temp = new TempDirectory();
        var target = temp.FullPath("manifest.ndjson");

        using var writer = new AtomicFileWriter(target, Mode);
        writer.Stream.Write("x"u8);

        var temporary = Assert.Single(TemporaryFiles(temp));

        Assert.Matches(new Regex("^\\.manifest\\.ndjson-[0-9a-f]{32}\\.tmp$"), Path.GetFileName(temporary));
        Assert.Equal(temp.Path, Path.GetDirectoryName(temporary));
    }

    [Fact]
    public void DisposeAbortsAnUncommittedWriter()
    {
        using var temp = new TempDirectory();
        var target = temp.WriteFile("artifact.ndjson", "previous");

        using (var writer = new AtomicFileWriter(target, Mode))
        {
            writer.Stream.Write("new"u8);
        }

        Assert.Equal("previous", File.ReadAllText(target));
        Assert.Empty(TemporaryFiles(temp));
    }

    [Fact]
    public void DisposeIsIdempotentAndKeepsACommittedTarget()
    {
        using var temp = new TempDirectory();
        var target = temp.FullPath("artifact.ndjson");

        var writer = new AtomicFileWriter(target, Mode);
        writer.Stream.Write("committed"u8);
        Assert.True(writer.Commit().IsSuccess);
        writer.Dispose();
        writer.Dispose();

        Assert.Equal("committed", File.ReadAllText(target));
        Assert.Empty(TemporaryFiles(temp));
    }

    [Fact]
    public void AbortIsIdempotent()
    {
        using var temp = new TempDirectory();
        var target = temp.FullPath("artifact.ndjson");

        var writer = new AtomicFileWriter(target, Mode);
        writer.Stream.Write("x"u8);
        writer.Abort();
        writer.Abort();

        Assert.False(File.Exists(target));
    }

    [Fact]
    public void AbortAfterCommitIsANoOp()
    {
        using var temp = new TempDirectory();
        var target = temp.FullPath("artifact.ndjson");

        var writer = new AtomicFileWriter(target, Mode);
        writer.Stream.Write("committed"u8);
        Assert.True(writer.Commit().IsSuccess);
        writer.Abort();

        Assert.Equal("committed", File.ReadAllText(target));
    }

    [Fact]
    public void CommitTwiceIsAProgrammerError()
    {
        using var temp = new TempDirectory();
        var target = temp.FullPath("artifact.ndjson");

        using var writer = new AtomicFileWriter(target, Mode);
        writer.Stream.Write("x"u8);
        Assert.True(writer.Commit().IsSuccess);

        Assert.Throws<InvalidOperationException>(() => writer.Commit());
    }

    [Fact]
    public void CommitAfterAbortIsAProgrammerError()
    {
        using var temp = new TempDirectory();
        var target = temp.FullPath("artifact.ndjson");

        using var writer = new AtomicFileWriter(target, Mode);
        writer.Abort();

        Assert.Throws<InvalidOperationException>(() => writer.Commit());
    }

    [Fact]
    public void UseAfterDisposeIsAProgrammerError()
    {
        using var temp = new TempDirectory();
        var target = temp.FullPath("artifact.ndjson");

        var writer = new AtomicFileWriter(target, Mode);
        writer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => writer.Stream);
        Assert.Throws<ObjectDisposedException>(() => writer.Commit());
    }

    [Fact]
    public void AbortSwallowsAnUndeletableTemporaryFile()
    {
        using var temp = new TempDirectory();
        var target = temp.WriteFile("artifact.ndjson", "previous");

        var writer = new AtomicFileWriter(target, Mode);
        writer.Stream.Write("new"u8);

        var directoryMode = File.GetUnixFileMode(temp.Path);

        try
        {
            // A directory without the write bit refuses the unlink; the abort
            // must still be safe, because the target is the guarantee.
            File.SetUnixFileMode(temp.Path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            writer.Abort();
        }
        finally
        {
            File.SetUnixFileMode(temp.Path, directoryMode);
        }

        Assert.Equal("previous", File.ReadAllText(target));
        writer.Dispose();
    }

    [Fact]
    public void AnEmptyTargetPathIsRejected()
    {
        using var temp = new TempDirectory();

        Assert.Throws<ArgumentException>(() => new AtomicFileWriter(string.Empty, Mode));
        Assert.True(temp.Path.Length > 0);
    }

    [Fact]
    public void AMissingTargetDirectoryIsReported()
    {
        using var temp = new TempDirectory();

        Assert.Throws<DirectoryNotFoundException>(
            () => new AtomicFileWriter(temp.FullPath("missing/artifact.ndjson"), Mode));
    }

    [Fact]
    public void ADirectoryDestinationIsAFailureAndLeavesNoTemporaryFile()
    {
        using var temp = new TempDirectory();
        var target = temp.CreateDirectory("artifact.ndjson");

        var writer = new AtomicFileWriter(target, Mode);
        writer.Stream.Write("new"u8);

        var result = writer.Commit();

        Assert.True(result.IsFailure);
        Assert.Equal("io.commit_failed", result.Error.Code);
        Assert.Empty(TemporaryFiles(temp));
        Assert.True(Directory.Exists(target));
        Assert.Throws<InvalidOperationException>(() => writer.Stream.Write("x"u8));
        Assert.Throws<InvalidOperationException>(() => writer.Commit());
    }

    internal static bool IsSymbolicLink(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static List<string> TemporaryFiles(TempDirectory temp)
    {
        return [.. Directory.GetFiles(temp.Path, ".*.tmp")];
    }
}
