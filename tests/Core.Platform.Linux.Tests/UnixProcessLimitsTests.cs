using Core.Platform.Linux;

namespace Core.Platform.Linux.Tests;

public sealed class UnixProcessLimitsTests
{
    [Fact]
    public void AResourceLimitRoundTrips()
    {
        Assert.True(UnixProcessLimits.TryGet(UnixResource.NoFile, out var original, out var error));
        Assert.Equal(LinuxErrno.None, error);
        Assert.True(original.Hard >= original.Soft);

        Assert.True(UnixProcessLimits.TrySet(UnixResource.NoFile, original, out error));
        Assert.Equal(LinuxErrno.None, error);
        Assert.True(UnixProcessLimits.TryGet(UnixResource.NoFile, out var again, out _));
        Assert.Equal(original, again);
    }

    [Fact]
    public void UnlimitedIsTheKernelInfinity()
    {
        Assert.Equal(new UnixResourceLimit(ulong.MaxValue, ulong.MaxValue), UnixResourceLimit.Unlimited);
    }
}
