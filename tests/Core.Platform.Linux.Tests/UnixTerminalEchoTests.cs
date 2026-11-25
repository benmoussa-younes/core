// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;
using Core.Platform.Linux;

namespace Core.Platform.Linux.Tests;

[Collection("process-limits")]
public sealed class UnixTerminalEchoTests
{
    [Fact]
    public void EchoIsDisabledAndRestored()
    {
        var (master, slave) = OpenPseudoTerminal();

        try
        {
            Assert.True(ReadRawEcho(slave));

            var disabled = UnixTerminalEcho.Disable(slave);
            Assert.True(disabled.IsSuccess);

            using (disabled.Value)
            {
                Assert.False(ReadRawEcho(slave));
            }

            Assert.True(ReadRawEcho(slave));
        }
        finally
        {
            _ = Interop.Close(slave);
            _ = Interop.Close(master);
        }
    }

    [Fact]
    public void AClosedDescriptorIsRefused()
    {
        var (master, slave) = OpenPseudoTerminal();
        _ = Interop.Close(slave);

        try
        {
            var disabled = UnixTerminalEcho.Disable(slave);

            Assert.True(disabled.TryGetError(out var error));
            Assert.Equal("unix.termios", error!.Code);
            Assert.Contains("BadFileDescriptor", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            _ = Interop.Close(master);
        }
    }

    [Fact]
    public void ANonTerminalDescriptorIsRefused()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("plain.txt", "x");
        var descriptor = Open(path);

        try
        {
            var disabled = UnixTerminalEcho.Disable(descriptor);

            Assert.True(disabled.TryGetError(out var error));
            Assert.Equal("unix.termios", error!.Code);
            Assert.Contains("NotATty", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            _ = Interop.Close(descriptor);
        }
    }

    private static bool ReadRawEcho(int descriptor)
    {
        var attributes = new byte[60];

        unsafe
        {
            fixed (byte* pointer = attributes)
            {
                Assert.Equal(0, Interop.TcGetAttr(descriptor, pointer));
            }
        }

        return (BitConverter.ToUInt32(attributes, 12) & 0x8) != 0;
    }

    private static (int Master, int Slave) OpenPseudoTerminal()
    {
        var master = Interop.PosixOpenPt(Interop.OpenReadWrite | Interop.OpenNoControlTerminal);
        Assert.True(master >= 0);

        try
        {
            Assert.Equal(0, Interop.GrantPt(master));
            Assert.Equal(0, Interop.UnlockPt(master));

            var name = new byte[256];

            unsafe
            {
                fixed (byte* pointer = name)
                {
                    Assert.Equal(0, Interop.PtsNameR(master, pointer, (nuint)name.Length));
                }
            }

            var path = Encoding.UTF8.GetString(name, 0, Array.IndexOf(name, (byte)0));
            var slave = Open(path);
            Assert.True(slave >= 0);
            return (master, slave);
        }
        catch
        {
            _ = Interop.Close(master);
            throw;
        }
    }

    private static int Open(string path)
    {
        var encoded = Encoding.UTF8.GetBytes(path + "\0");

        unsafe
        {
            fixed (byte* pointer = encoded)
            {
                return Interop.OpenAt(
                    Interop.AtCurrentWorkingDirectory,
                    pointer,
                    Interop.OpenReadWrite | Interop.OpenNoControlTerminal,
                    0);
            }
        }
    }
}
