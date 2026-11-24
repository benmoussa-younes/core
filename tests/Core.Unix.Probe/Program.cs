using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Core.Platform.Linux;

namespace Core.Unix.Probe;

internal static partial class Program
{
    private static int Main(string[] args)
    {
        if (args is ["--self-test"])
        {
            return SelfTest.Run();
        }

        if (args is ["--fd0"])
        {
            return DescriptorZero() ? 0 : 1;
        }

        if (args is ["--spawn-child", var factsPath, var readText])
        {
            var reads = readText
                .Split(',')
                .Select(text => int.Parse(text, CultureInfo.InvariantCulture))
                .ToArray();
            return SpawnChild.Run(factsPath, reads);
        }

        if (args is ["--sleep-grandchild", var pidPath])
        {
            var startInfo = new ProcessStartInfo("/bin/sleep") { UseShellExecute = false };
            startInfo.ArgumentList.Add("30");
            var grandchild = Process.Start(startInfo)!;
            File.WriteAllText(pidPath, grandchild.Id.ToString(CultureInfo.InvariantCulture));
            Thread.Sleep(30_000);
            return 0;
        }

        if (args is ["--exit", var codeText])
        {
            Environment.Exit(int.Parse(codeText, CultureInfo.InvariantCulture));
            return 0;
        }

        if (args is ["--sleep"])
        {
            Thread.Sleep(30_000);
            return 0;
        }

        Console.Error.WriteLine("usage: Core.Unix.Probe --self-test|--fd0|--spawn-child|--sleep-grandchild|--exit|--sleep");
        return 2;
    }

    internal static bool DescriptorZero()
    {
        // WHY: the first UnixPipe.Create loads the runtime; warm it before close(0) so no lazy load takes descriptor 0.
        var warmup = UnixPipe.Create();

        if (!warmup.IsSuccess)
        {
            return false;
        }

        warmup.Value.Dispose();

        if (Close(0) != 0)
        {
            return false;
        }

        var created = UnixPipe.Create();

        if (!created.IsSuccess)
        {
            return false;
        }

        using var pipe = created.Value;

        if (pipe.ReadDescriptor != 0)
        {
            return false;
        }

        pipe.CloseRead();
        return Fcntl(0, 1, 0) == -1;
    }

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    internal static partial int Close(int descriptor);

    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    internal static partial int Fcntl(int descriptor, int command, int argument);
}
