// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;

namespace Core.Platform.Linux.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "core-platform-" + Guid.NewGuid().ToString("N"));

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
