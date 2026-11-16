// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Core.Platform.Linux;


namespace Core.Platform.Linux.Tests;

public sealed class LinuxStatLayoutTests
{
    // The kernel ABI offsets are written as literals here on purpose: a test
    // that placed the synthetic field through the production constant would
    // move with the constant and prove nothing.
    private const int StatxUserId = 20;
    private const int StatxGroupId = 24;
    private const int StatxMode = 28;

    [Fact]
    public void ParseStatxReadsEveryFieldAtItsFixedOffset()
    {
        var buffer = Statx(
            userId: 1001,
            groupId: 1002,
            mode: 0x81A4,
            inode: 42,
            size: 1234,
            seconds: 1_700_000_000,
            nanoseconds: 123_456_789,
            major: 8,
            minor: 1);

        var status = LinuxStatLayout.ParseStatx(buffer);

        Assert.Equal(LinuxFileType.RegularFile, status.Type);
        Assert.Equal(42ul, status.Inode);
        Assert.Equal(1234, status.Size);
        Assert.Equal(1_700_000_000_123_456_789L, status.MtimeNanoseconds);
        Assert.Equal(((ulong)8 << 32) | 1, status.Device);
        Assert.Equal(1001u, status.OwnerUserId);
        Assert.Equal(1002u, status.OwnerGroupId);
        Assert.Equal(0x1A4u, status.Mode);
    }

    [Fact]
    public void ParseStatxKeepsOnlyThePermissionBitsInMode()
    {
        // 0x8FE4 is a regular file whose permission word carries set-user-ID
        // plus 0o744; the type bits must not leak into Mode.
        var buffer = Statx(0, 0, 0x8FE4, 0, 0, 0, 0, 0, 0);

        var status = LinuxStatLayout.ParseStatx(buffer);

        Assert.Equal(LinuxFileType.RegularFile, status.Type);
        Assert.Equal(0xFE4u, status.Mode);
    }

    [Theory]
    [InlineData(0x1000, LinuxFileType.Fifo)]
    [InlineData(0x2000, LinuxFileType.CharacterDevice)]
    [InlineData(0x4000, LinuxFileType.Directory)]
    [InlineData(0x6000, LinuxFileType.BlockDevice)]
    [InlineData(0x8000, LinuxFileType.RegularFile)]
    [InlineData(0xA000, LinuxFileType.SymbolicLink)]
    [InlineData(0xC000, LinuxFileType.Socket)]
    [InlineData(0x0000, LinuxFileType.Unknown)]
    [InlineData(0x81A4, LinuxFileType.RegularFile)]
    [InlineData(0xE000, LinuxFileType.Unknown)]
    public void FileTypeFromModeMapsEveryType(int mode, LinuxFileType expected)
    {
        Assert.Equal(expected, LinuxStatLayout.FileTypeFromMode((ushort)mode));
    }

    [Fact]
    public void StatxFieldOffsetsMatchTheKernelAbi()
    {
        Assert.Equal(StatxUserId, LinuxStatLayout.StatxUserIdOffset);
        Assert.Equal(StatxGroupId, LinuxStatLayout.StatxGroupIdOffset);
        Assert.Equal(StatxMode, LinuxStatLayout.StatxModeOffset);
    }

    [Fact]
    public void TryParseStatReadsTheX64Layout()
    {
        var buffer = Stat(
            modeOffset: 24,
            userIdOffset: 28,
            groupIdOffset: 32,
            userId: 1001,
            groupId: 1002,
            mode: 0x41ED,
            device: 7,
            inode: 9,
            size: 55,
            seconds: 10,
            nanoseconds: 5);

        Assert.True(LinuxStatLayout.TryParseStat(buffer, Architecture.X64, out var status));
        Assert.Equal(LinuxFileType.Directory, status.Type);
        Assert.Equal(7ul, status.Device);
        Assert.Equal(9ul, status.Inode);
        Assert.Equal(55, status.Size);
        Assert.Equal(10_000_000_005L, status.MtimeNanoseconds);
        Assert.Equal(1001u, status.OwnerUserId);
        Assert.Equal(1002u, status.OwnerGroupId);
        Assert.Equal(0x1EDu, status.Mode);
    }

    [Fact]
    public void TryParseStatReadsTheArm64Layout()
    {
        // The asm-generic layout places the ownership and mode words 8 bytes
        // before the x86-64 ones; reading the x86-64 offsets here would return
        // the wrong values instead of failing.
        var buffer = Stat(
            modeOffset: 16,
            userIdOffset: 24,
            groupIdOffset: 28,
            userId: 1001,
            groupId: 1002,
            mode: 0xA1FF,
            device: 1,
            inode: 2,
            size: 3,
            seconds: 4,
            nanoseconds: 6);

        Assert.True(LinuxStatLayout.TryParseStat(buffer, Architecture.Arm64, out var status));
        Assert.Equal(LinuxFileType.SymbolicLink, status.Type);
        Assert.Equal(1ul, status.Device);
        Assert.Equal(2ul, status.Inode);
        Assert.Equal(3, status.Size);
        Assert.Equal(1001u, status.OwnerUserId);
        Assert.Equal(1002u, status.OwnerGroupId);
        Assert.Equal(0x1FFu, status.Mode);
    }

    [Theory]
    [InlineData(Architecture.X64, 24, 28, 32)]
    [InlineData(Architecture.Arm64, 16, 24, 28)]
    public void StatFieldOffsetsMatchTheArchitectureTable(Architecture architecture, int mode, int userId, int groupId)
    {
        Assert.True(LinuxStatLayout.TryGetStatFieldOffsets(architecture, out var offsets));
        Assert.Equal(mode, offsets.Mode);
        Assert.Equal(userId, offsets.UserId);
        Assert.Equal(groupId, offsets.GroupId);
    }

    [Theory]
    [InlineData(Architecture.S390x)]
    [InlineData(Architecture.Arm)]
    public void TryGetStatFieldOffsetsRefusesAnUnknownArchitecture(Architecture architecture)
    {
        Assert.False(LinuxStatLayout.TryGetStatFieldOffsets(architecture, out var offsets));
        Assert.Equal(default, offsets);
    }

    [Fact]
    public void TryParseStatRefusesAnUnknownArchitecture()
    {
        var buffer = Stat(24, 28, 32, 0, 0, 0x8000, 0, 0, 0, 0, 0);

        Assert.False(LinuxStatLayout.TryParseStat(buffer, Architecture.S390x, out var status));
        Assert.Equal(default, status);
    }

    [Fact]
    public void FileStatusLayoutIsPinned()
    {
        // The status is returned by value from every stat; a field added without
        // a look at the by-value cost would slip past the byte-offset tests.
        // Eight words: four 64-bit fields, three 32-bit fields and the file type,
        // plus the trailing 32-bit padding of the automatic layout.
        Assert.Equal(56, Unsafe.SizeOf<LinuxFileStatus>());
    }

    [Fact]
    public void MakeDevicePacksMajorAndMinor()
    {
        Assert.Equal(((ulong)0x1234 << 32) | 0x5678, LinuxStatLayout.MakeDevice(0x1234, 0x5678));
    }

    [Fact]
    public void TryParseStatxRefusesAnOutOfRangeTimestamp()
    {
        var buffer = Statx(0, 0, 0x81A4, 0, 0, long.MaxValue, 0, 0, 0);

        Assert.False(LinuxStatLayout.TryParseStatx(buffer, out var status));
        Assert.Equal(default, status);
    }

    [Fact]
    public void TryParseStatRefusesAnOutOfRangeTimestamp()
    {
        var buffer = Stat(24, 28, 32, 0, 0, 0x81A4, 0, 0, 0, long.MaxValue, 0);

        Assert.False(LinuxStatLayout.TryParseStat(buffer, Architecture.X64, out var status));
        Assert.Equal(default, status);
    }

    [Fact]
    public void ToUnixNanosecondsCombinesTheParts()
    {
        Assert.Equal(1_000_000_001L, LinuxStatLayout.ToUnixNanoseconds(1, 1));
    }

    private static byte[] Statx(
        uint userId,
        uint groupId,
        ushort mode,
        ulong inode,
        long size,
        long seconds,
        uint nanoseconds,
        uint major,
        uint minor)
    {
        var buffer = new byte[LinuxStatLayout.StatxSize];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(StatxUserId), userId);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(StatxGroupId), groupId);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(StatxMode), mode);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(LinuxStatLayout.StatxInodeOffset), inode);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(LinuxStatLayout.StatxSizeOffset), size);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(LinuxStatLayout.StatxMtimeSecondsOffset), seconds);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(LinuxStatLayout.StatxMtimeNanosecondsOffset), nanoseconds);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(LinuxStatLayout.StatxDeviceMajorOffset), major);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(LinuxStatLayout.StatxDeviceMinorOffset), minor);
        return buffer;
    }

    private static byte[] Stat(
        int modeOffset,
        int userIdOffset,
        int groupIdOffset,
        uint userId,
        uint groupId,
        ushort mode,
        ulong device,
        ulong inode,
        long size,
        long seconds,
        uint nanoseconds)
    {
        var buffer = new byte[LinuxStatLayout.StatBufferSize];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(modeOffset), mode);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(userIdOffset), userId);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(groupIdOffset), groupId);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(LinuxStatLayout.StatDeviceOffset), device);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(LinuxStatLayout.StatInodeOffset), inode);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(LinuxStatLayout.StatSizeOffset), size);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(LinuxStatLayout.StatMtimeSecondsOffset), seconds);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(LinuxStatLayout.StatMtimeNanosecondsOffset), nanoseconds);
        return buffer;
    }
}
