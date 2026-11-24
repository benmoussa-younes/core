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

        Console.Error.WriteLine("usage: Core.Unix.Probe --self-test|--fd0");
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
    private static partial int Close(int descriptor);

    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int Fcntl(int descriptor, int command, int argument);
}
