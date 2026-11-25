// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Core.Unix.Probe;

internal static partial class SpawnChild
{
    internal static int Run(string factsPath, int[] readDescriptors)
    {
        var terminal = IsATty(0) != 0;
        var lines = new List<string>
        {
            $"CWD={Directory.GetCurrentDirectory()}",
            $"ENV={ReadEnvironment()}",
            $"SIGTERM_BLOCKED={ReadSigTermBlocked()}",
            $"ISATTY0={(terminal ? 1 : 0)}",
            $"FOREGROUND={TcGetPgrp(0)}",
            $"WINSIZE={ReadWindowSize(0)}",
        };

        if (terminal)
        {
            lines.Add($"STDIN={ReadTerminalLine(0, ReadTimeoutMilliseconds)}");
            WriteTerminal(1, "pty-child\n");
        }

        lines.AddRange(ReadDescriptorTargets());

        foreach (var descriptor in readDescriptors)
        {
            var buffer = new byte[256];
            var count = ReadDescriptor(descriptor, buffer);
            lines.Add($"FD{descriptor}={Encoding.UTF8.GetString(buffer, 0, count)}");
        }

        File.WriteAllLines(factsPath, lines);
        return 0;
    }

    private static string ReadEnvironment()
    {
        return Encoding.UTF8
            .GetString(File.ReadAllBytes("/proc/self/environ"))
            .Replace('\0', ' ')
            .TrimEnd();
    }

    private static IEnumerable<string> ReadDescriptorTargets()
    {
        return Directory.GetFiles("/proc/self/fd")
            .Select(path => Path.GetFileName(path))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => $"TARGET_{name}={new FileInfo($"/proc/self/fd/{name}").LinkTarget ?? "closed"}");
    }

    private static int ReadDescriptor(int descriptor, byte[] buffer)
    {
        using var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: false);
        using var stream = new FileStream(handle, FileAccess.Read, bufferSize: 1);
        var count = 0;

        while (count < buffer.Length)
        {
            var read = stream.Read(buffer, count, buffer.Length - count);

            if (read == 0)
            {
                break;
            }

            count += read;
        }

        return count;
    }

    private static unsafe string ReadTerminalLine(int descriptor, int timeoutMilliseconds)
    {
        var text = new StringBuilder();
        var buffer = stackalloc byte[1];
        var poll = new PollDescriptor { Descriptor = descriptor, Events = PollReadable };

        if (Poll(&poll, 1, timeoutMilliseconds) <= 0)
        {
            return "none";
        }

        while (text.Length < 64)
        {
            var read = Read(descriptor, buffer, 1);

            if (read <= 0 || buffer[0] == (byte)'\n')
            {
                break;
            }

            _ = text.Append((char)buffer[0]);
        }

        return text.ToString();
    }

    private static unsafe void WriteTerminal(int descriptor, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        fixed (byte* pointer = bytes)
        {
            _ = Write(descriptor, pointer, bytes.Length);
        }
    }

    private static unsafe string ReadWindowSize(int descriptor)
    {
        Span<ushort> values = stackalloc ushort[4];

        fixed (ushort* pointer = values)
        {
            if (Ioctl(descriptor, WindowSizeRequest, pointer) != 0)
            {
                return "none";
            }
        }

        return $"{values[0]},{values[1]}";
    }

    private static unsafe int ReadSigTermBlocked()
    {
        var current = stackalloc byte[128];

        if (SigProcMask(0, null, current) != 0)
        {
            return -1;
        }

        return SigISMmber(current, 15) == 1 ? 1 : 0;
    }

    private const int ReadTimeoutMilliseconds = 2000;
    private const short PollReadable = 1;
    private const nuint WindowSizeRequest = 0x5413;

    [LibraryImport("libc", EntryPoint = "sigprocmask", SetLastError = true)]
    private static unsafe partial int SigProcMask(int how, byte* set, byte* oldSet);

    [LibraryImport("libc", EntryPoint = "sigismember", SetLastError = true)]
    private static unsafe partial int SigISMmber(byte* set, int signal);

    [LibraryImport("libc", EntryPoint = "isatty", SetLastError = true)]
    private static partial int IsATty(int descriptor);

    [LibraryImport("libc", EntryPoint = "tcgetpgrp", SetLastError = true)]
    private static partial int TcGetPgrp(int descriptor);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static unsafe partial int Ioctl(int descriptor, nuint request, void* argument);

    private struct PollDescriptor
    {
        internal int Descriptor;
        internal short Events;
        internal short Ready;
    }

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static unsafe partial int Poll(void* descriptors, nuint count, int timeoutMilliseconds);

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    private static unsafe partial int Read(int descriptor, byte* buffer, nint count);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    private static unsafe partial int Write(int descriptor, byte* buffer, nint count);
}
