using Core.Platform.Linux;

namespace Core.Platform.Linux.Tests;

[Collection("process-limits")]
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
    public void AChangedSoftLimitIsAppliedAndRestored()
    {
        Assert.True(UnixProcessLimits.TryGet(UnixResource.NoFile, out var original, out _));
        Assert.True(original.Soft > 0);
        var changed = original with { Soft = original.Soft - 1 };

        try
        {
            Assert.True(UnixProcessLimits.TrySet(UnixResource.NoFile, changed, out var error));
            Assert.Equal(LinuxErrno.None, error);
            Assert.True(UnixProcessLimits.TryGet(UnixResource.NoFile, out var applied, out _));
            Assert.Equal(changed, applied);
        }
        finally
        {
            Assert.True(UnixProcessLimits.TrySet(UnixResource.NoFile, original, out _));
        }
    }

    [Fact]
    public void AnUnknownResourceIsRefused()
    {
        Assert.False(UnixProcessLimits.TrySet((UnixResource)9999, UnixResourceLimit.Unlimited, out var error));
        Assert.Equal(LinuxErrno.InvalidArgument, error);
    }

    [Fact]
    public void UnlimitedIsTheKernelInfinity()
    {
        Assert.Equal(new UnixResourceLimit(ulong.MaxValue, ulong.MaxValue), UnixResourceLimit.Unlimited);
    }
}
