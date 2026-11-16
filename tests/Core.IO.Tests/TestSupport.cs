// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Text;

namespace Core.IO.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "core-io-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string WriteFile(string relativePath, string content)
    {
        return WriteBytes(relativePath, Encoding.UTF8.GetBytes(content));
    }

    public string WriteBytes(string relativePath, byte[] content)
    {
        var fullPath = FullPath(relativePath);
        var directory = System.IO.Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(fullPath, content);
        return fullPath;
    }

    public string CreateDirectory(string relativePath)
    {
        var fullPath = FullPath(relativePath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public string FullPath(string relativePath)
    {
        return System.IO.Path.Combine(Path, relativePath);
    }

    public string CreateFileSymlink(string relativePath, string target)
    {
        var fullPath = FullPath(relativePath);
        File.CreateSymbolicLink(fullPath, target);
        return fullPath;
    }

    public string CreateDirectorySymlink(string relativePath, string target)
    {
        var fullPath = FullPath(relativePath);
        Directory.CreateSymbolicLink(fullPath, target);
        return fullPath;
    }

    public void Dispose()
    {
        RestorePermissions(Path);

        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void RestorePermissions(string path)
    {
        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                RestorePermissions(entry);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// Creates names and descriptors that cannot be expressed through the managed
/// file-system API, so the native byte path can be exercised.
/// </summary>
internal static unsafe class RawFileSystem
{
    private const int OpenCreateWrite = 0x41;
    private const int Mode644 = 0x1A4;

    public static void CreateFileWithRawName(string directory, byte[] name)
    {
        var directoryBytes = Encoding.UTF8.GetBytes(directory);
        var path = new byte[directoryBytes.Length + 1 + name.Length + 1];
        directoryBytes.CopyTo(path, 0);
        path[directoryBytes.Length] = (byte)'/';
        name.CopyTo(path, directoryBytes.Length + 1);

        var fileDescriptor = OpenRaw(path, OpenCreateWrite, Mode644);

        if (fileDescriptor < 0)
        {
            throw new IOException($"open failed with errno {Marshal.GetLastPInvokeError()}.");
        }

        _ = Close(fileDescriptor);
    }

    public static int OpenReadOnly(string path)
    {
        var bytes = Encoding.UTF8.GetBytes(path + "\0");

        fixed (byte* pointer = bytes)
        {
            return OpenAt(-100, pointer, 0, 0);
        }
    }

    public static void MakeFifo(string path, uint mode)
    {
        if (MakeFifoNative(path, mode) != 0)
        {
            throw new IOException($"mkfifo failed with errno {Marshal.GetLastPInvokeError()}.");
        }
    }

    private static int OpenRaw(byte[] path, int flags, int mode)
    {
        fixed (byte* pointer = path)
        {
            return OpenAt(-100, pointer, flags, (uint)mode);
        }
    }

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAt(int directoryDescriptor, byte* path, int flags, uint mode);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fileDescriptor);

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifoNative([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
}
