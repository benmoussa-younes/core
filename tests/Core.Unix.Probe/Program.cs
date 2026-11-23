namespace Core.Unix.Probe;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["--self-test"])
        {
            return SelfTest.Run();
        }

        Console.Error.WriteLine("usage: Core.Unix.Probe --self-test");
        return 2;
    }
}
