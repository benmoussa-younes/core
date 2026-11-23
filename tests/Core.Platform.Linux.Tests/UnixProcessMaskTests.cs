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

        using (var scope = UnixMaskScope.Enter(GroupAndOther))
        {
            Assert.Equal(original, scope.Previous);
            Assert.True(UnixProcessMask.TryRead(out var current));
            Assert.Equal(GroupAndOther, current);
        }

        Assert.True(UnixProcessMask.TryRead(out var restored));
        Assert.Equal(original, restored);
    }

    [Fact]
    public void ANewFileFollowsTheScopedMask()
    {
        using var temp = new TempDirectory();
        var path = temp.FullPath("masked.txt");

        using (UnixMaskScope.Enter(GroupAndOther))
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

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }
}
