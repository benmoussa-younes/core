using Core.Platform.Linux;

namespace Core.Platform.Linux.Tests;

[Collection("process-limits")]
public sealed class UnixProcessMaskTests
{
    private const UnixFileMode GroupAndOther =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private const UnixFileMode GroupAndOtherWritable =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite;

    [Fact]
    public void MaskScopeAppliesAndRestoresTheProcessMask()
    {
        Assert.True(UnixProcessMask.TryRead(out var original));
        var target = original == GroupAndOther ? UnixFileMode.GroupWrite : GroupAndOther;

        using (var scope = UnixMaskScope.Enter(target))
        {
            Assert.Equal(original, scope.Previous);
            Assert.True(UnixProcessMask.TryRead(out var current));
            Assert.Equal(target, current);
        }

        Assert.True(UnixProcessMask.TryRead(out var restored));
        Assert.Equal(original, restored);

        default(UnixMaskScope).Dispose();
        Assert.True(UnixProcessMask.TryRead(out var untouched));
        Assert.Equal(original, untouched);
    }

    [Fact]
    public void ANewFileFollowsTheScopedMask()
    {
        using var temp = new TempDirectory();
        var path = temp.FullPath("masked.txt");
        Assert.True(UnixProcessMask.TryRead(out var original));
        var mask = original == GroupAndOther ? UnixFileMode.GroupWrite | UnixFileMode.OtherWrite : GroupAndOther;
        var expected = mask == GroupAndOther
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite
            : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

        using (UnixMaskScope.Enter(mask))
        {
            using var stream = new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | GroupAndOtherWritable,
                });
        }

        Assert.Equal(expected, File.GetUnixFileMode(path));
    }
}
