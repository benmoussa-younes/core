using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Core.Unix.Probe;

internal static partial class SpawnChild
{
    internal static int Run(string factsPath, int[] readDescriptors)
    {
        var lines = new List<string>
        {
            $"CWD={Directory.GetCurrentDirectory()}",
            $"ENV={ReadEnvironment()}",
            $"SIGTERM_BLOCKED={ReadSigTermBlocked()}",
        };

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

    private static unsafe int ReadSigTermBlocked()
    {
        var current = stackalloc byte[128];

        if (SigProcMask(0, null, current) != 0)
        {
            return -1;
        }

        return SigISMmber(current, 15) == 1 ? 1 : 0;
    }

    [LibraryImport("libc", EntryPoint = "sigprocmask", SetLastError = true)]
    private static unsafe partial int SigProcMask(int how, byte* set, byte* oldSet);

    [LibraryImport("libc", EntryPoint = "sigismember", SetLastError = true)]
    private static unsafe partial int SigISMmber(byte* set, int signal);
}
