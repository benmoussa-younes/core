// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Globalization;
using Core.Platform.Linux;

namespace Core.Platform.Linux.Tests;

[CollectionDefinition("process-limits", DisableParallelization = true)]
public sealed class ProcessLimitsCollection
{
}

[Collection("process-limits")]
public sealed class UnixLockedMemoryTests
{
    private const int MemLockResource = 8;

    [Fact]
    public void LockedMemoryIsWritableAndZeroedOnDispose()
    {
        var buffer = new byte[64];
        var locked = UnixLockedMemory.Lock(buffer);
        Assert.True(locked.IsSuccess);

        using (var memory = locked.Value)
        {
            memory.Span[0] = 42;
            memory.Span[^1] = 7;
            Assert.Equal(42, buffer[0]);
        }

        Assert.All(buffer, value => Assert.Equal(0, value));
    }

    [Fact]
    public void LockRaisesAndRestoresTheLockedMemoryCounter()
    {
        var before = ReadLockedKilobytes();

        var result = UnixLockedMemory.Lock(new byte[4096]);
        Assert.True(result.IsSuccess);

        using (result.Value)
        {
            Assert.True(ReadLockedKilobytes() > before);
        }

        Assert.Equal(before, ReadLockedKilobytes());
    }

    [Fact]
    public void LockFailsWhenTheLockedMemoryLimitIsZero()
    {
        var original = ReadLimit(MemLockResource);

        try
        {
            WriteLimit(MemLockResource, 0, original.Maximum);

            var locked = UnixLockedMemory.Lock(new byte[4096]);

            Assert.True(locked.TryGetError(out var error));
            Assert.Equal("unix.mlock", error!.Code);
            Assert.Contains("mlock failed:", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            WriteLimit(MemLockResource, original.Current, original.Maximum);
        }
    }

    [Fact]
    public void LockRejectsAnEmptyBuffer()
    {
        Assert.Throws<ArgumentException>(() => UnixLockedMemory.Lock([]));
    }

    private static (ulong Current, ulong Maximum) ReadLimit(int resource)
    {
        var limit = new byte[16];

        unsafe
        {
            fixed (byte* pointer = limit)
            {
                Assert.Equal(0, Interop.GetRLimit(resource, pointer));
            }
        }

        return (BitConverter.ToUInt64(limit, 0), BitConverter.ToUInt64(limit, 8));
    }

    private static void WriteLimit(int resource, ulong current, ulong maximum)
    {
        var limit = new byte[16];
        BitConverter.TryWriteBytes(limit.AsSpan(0, 8), current);
        BitConverter.TryWriteBytes(limit.AsSpan(8, 8), maximum);

        unsafe
        {
            fixed (byte* pointer = limit)
            {
                Assert.Equal(0, Interop.SetRLimit(resource, pointer));
            }
        }
    }

    private static long ReadLockedKilobytes()
    {
        foreach (var line in File.ReadLines("/proc/self/status"))
        {
            if (!line.StartsWith("VmLck:", StringComparison.Ordinal))
            {
                continue;
            }

            var value = line["VmLck:".Length..]
                .Replace("kB", string.Empty, StringComparison.Ordinal)
                .Trim();

            return long.Parse(value, CultureInfo.InvariantCulture);
        }

        throw new InvalidOperationException("VmLck is missing from /proc/self/status.");
    }
}
