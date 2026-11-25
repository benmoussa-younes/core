// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Globalization;
using System.Runtime.InteropServices;
using Core.Platform.Linux;
using Core.Results;

namespace Core.Platform.Linux.Tests;

[Collection("process-limits")]
public sealed partial class UnixTerminalRawModeTests
{
    private const int TermiosSize = 60;
    private const int InputFlagsOffset = 0;
    private const int OutputFlagsOffset = 4;
    private const int ControlFlagsOffset = 8;
    private const int LocalFlagsOffset = 12;
    private const int ControlCharactersOffset = 17;
    private const int MinimumIndex = 6;
    private const int TimeoutIndex = 5;
    private const uint InputMask = 0x5EB;
    private const uint PostProcessOutput = 0x1;
    private const uint LocalMask = 0x804B;
    private const uint CharacterSizeMask = 0x30;
    private const uint ParityEnable = 0x100;

    [Fact]
    public void TheRawAttributesAreAppliedAndRestored()
    {
        var (master, slave) = OpenPseudoTerminal();

        try
        {
            var original = ReadAttributes(slave);
            var enabled = UnixTerminalRawMode.Enable(slave);

            Assert.True(enabled.IsSuccess);

            using (enabled.Value)
            {
                var current = ReadAttributes(slave);
                var input = BitConverter.ToUInt32(current, InputFlagsOffset);
                var output = BitConverter.ToUInt32(current, OutputFlagsOffset);
                var control = BitConverter.ToUInt32(current, ControlFlagsOffset);
                var local = BitConverter.ToUInt32(current, LocalFlagsOffset);

                Assert.Equal(0u, input & InputMask);
                Assert.Equal(0u, output & PostProcessOutput);
                Assert.Equal(CharacterSizeMask, control & CharacterSizeMask);
                Assert.Equal(0u, control & ParityEnable);
                Assert.Equal(0u, local & LocalMask);
                Assert.Equal(1, current[ControlCharactersOffset + MinimumIndex]);
                Assert.Equal(0, current[ControlCharactersOffset + TimeoutIndex]);
            }

            Assert.Equal(original, ReadAttributes(slave));
        }
        finally
        {
            _ = Interop.Close(slave);
            _ = Interop.Close(master);
        }
    }

    [Fact]
    public async Task ARawDescriptorReturnsBytesWithoutANewline()
    {
        var (master, slave) = OpenPseudoTerminal();
        var bytes = "ping"u8.ToArray();

        try
        {
            var enabled = UnixTerminalRawMode.Enable(slave);

            Assert.True(enabled.IsSuccess);

            using (enabled.Value)
            {
                Assert.Equal(bytes.Length, Write(master, bytes));

                var buffer = new byte[bytes.Length];
                var read = await Task.Run(() => Read(slave, buffer))
                    .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

                Assert.Equal(bytes.Length, read);
                Assert.Equal("ping", System.Text.Encoding.UTF8.GetString(buffer));
            }
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
            var enabled = UnixTerminalRawMode.Enable(slave);

            Assert.True(enabled.TryGetError(out var error));
            Assert.Equal(CoreErrorCodes.UnixTerminalFailed, error!.Code);
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
            var enabled = UnixTerminalRawMode.Enable(descriptor);

            Assert.True(enabled.TryGetError(out var error));
            Assert.Equal(CoreErrorCodes.UnixTerminalFailed, error!.Code);
            Assert.Contains("NotATty", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            _ = Interop.Close(descriptor);
        }
    }

    private static byte[] ReadAttributes(int descriptor)
    {
        var attributes = new byte[TermiosSize];

        unsafe
        {
            fixed (byte* pointer = attributes)
            {
                Assert.Equal(0, Interop.TcGetAttr(descriptor, pointer));
            }
        }

        return attributes;
    }

    private static unsafe int Write(int descriptor, byte[] bytes)
    {
        fixed (byte* pointer = bytes)
        {
            return WriteCore(descriptor, pointer, bytes.Length);
        }
    }

    private static unsafe int Read(int descriptor, byte[] bytes)
    {
        fixed (byte* pointer = bytes)
        {
            return ReadCore(descriptor, pointer, bytes.Length);
        }
    }

    private static (int Master, int Slave) OpenPseudoTerminal()
    {
        var master = Interop.PosixOpenPt(Interop.OpenReadWrite | Interop.OpenNoControlTerminal);
        Assert.True(master >= 0, $"posix_openpt failed: {master}.");

        try
        {
            Assert.Equal(0, Interop.GrantPt(master));
            Assert.Equal(0, Interop.UnlockPt(master));

            var name = new byte[128];

            unsafe
            {
                fixed (byte* pointer = name)
                {
                    Assert.Equal(0, Interop.PtsNameR(master, pointer, (nuint)name.Length));
                }
            }

            var path = System.Text.Encoding.UTF8.GetString(name, 0, Array.IndexOf(name, (byte)0));
            var slave = Open(path);

            Assert.True(slave >= 0, $"the slave could not be opened: {slave.ToString(CultureInfo.InvariantCulture)}.");
            return (master, slave);
        }
        catch
        {
            _ = Interop.Close(master);
            throw;
        }
    }

    private static unsafe int Open(string path)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(path + "\0");

        fixed (byte* pointer = bytes)
        {
            return Interop.OpenAt(
                Interop.AtCurrentWorkingDirectory,
                pointer,
                Interop.OpenReadWrite | Interop.OpenNoControlTerminal,
                0);
        }
    }

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    private static unsafe partial int WriteCore(int descriptor, byte* buffer, nint count);

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    private static unsafe partial int ReadCore(int descriptor, byte* buffer, nint count);
}
