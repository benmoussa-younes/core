// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers.Binary;
using System.Runtime.InteropServices;


namespace Core.Platform.Linux;

/// <summary>
/// The fixed byte offsets of the two kernel status layouts.
/// </summary>
/// <remarks>
/// <c>statx</c> has a kernel-defined, architecture-independent layout; only its
/// <c>stx_uid</c>, <c>stx_gid</c>, <c>stx_mode</c>, <c>stx_ino</c>,
/// <c>stx_size</c>, <c>stx_mtime</c> and <c>stx_dev_*</c> fields are read. The
/// older <c>struct stat</c> differs per architecture: x86-64 places
/// <c>st_uid</c>/<c>st_gid</c>/<c>st_mode</c> at bytes 28/32/24 and the
/// asm-generic AArch64 layout at bytes 24/28/16, while device, inode, size and
/// <c>st_mtim</c> share the same offsets. A layout the table does not know is
/// refused rather than read at the wrong offset.
/// </remarks>
internal static class LinuxStatLayout
{
    /// <summary>The permission and set-id bits of <c>st_mode</c> (<c>0o7777</c>).</summary>
    private const ushort PermissionMask = 0x0FFF;

    /// <summary>The fixed size of a <c>struct statx</c>.</summary>
    internal const int StatxSize = 256;

    /// <summary>The size of the <c>struct stat</c> scratch buffer; larger than any supported layout.</summary>
    internal const int StatBufferSize = 256;

    internal const int StatxUserIdOffset = 20;
    internal const int StatxGroupIdOffset = 24;
    internal const int StatxModeOffset = 28;
    internal const int StatxInodeOffset = 32;
    internal const int StatxSizeOffset = 40;
    internal const int StatxMtimeSecondsOffset = 112;
    internal const int StatxMtimeNanosecondsOffset = 120;
    internal const int StatxDeviceMajorOffset = 136;
    internal const int StatxDeviceMinorOffset = 140;

    internal const int StatDeviceOffset = 0;
    internal const int StatInodeOffset = 8;
    internal const int StatSizeOffset = 48;
    internal const int StatMtimeSecondsOffset = 88;
    internal const int StatMtimeNanosecondsOffset = 96;

    private const ushort ModeTypeMask = 0xF000;
    private const ushort ModeFifo = 0x1000;
    private const ushort ModeCharacterDevice = 0x2000;
    private const ushort ModeDirectory = 0x4000;
    private const ushort ModeBlockDevice = 0x6000;
    private const ushort ModeRegularFile = 0x8000;
    private const ushort ModeSymbolicLink = 0xA000;
    private const ushort ModeSocket = 0xC000;

    /// <summary>
    /// The byte offsets of the <c>st_mode</c>, <c>st_uid</c> and <c>st_gid</c>
    /// fields of one <c>struct stat</c> layout.
    /// </summary>
    /// <param name="Mode">The offset of <c>st_mode</c>.</param>
    /// <param name="UserId">The offset of <c>st_uid</c>.</param>
    /// <param name="GroupId">The offset of <c>st_gid</c>.</param>
    internal readonly record struct StatFieldOffsets(int Mode, int UserId, int GroupId);

    /// <summary>
    /// Looks up the field offsets of <c>struct stat</c> for an architecture.
    /// </summary>
    /// <param name="architecture">The architecture to look up.</param>
    /// <param name="offsets">The offsets when the layout is known.</param>
    /// <returns><see langword="true"/> when the layout is known.</returns>
    internal static bool TryGetStatFieldOffsets(Architecture architecture, out StatFieldOffsets offsets)
    {
        switch (architecture)
        {
            case Architecture.X64:
                offsets = new StatFieldOffsets(Mode: 24, UserId: 28, GroupId: 32);
                return true;

            case Architecture.Arm64:
                offsets = new StatFieldOffsets(Mode: 16, UserId: 24, GroupId: 28);
                return true;

            default:
                offsets = default;
                return false;
        }
    }

    /// <summary>
    /// Parses a raw <c>struct statx</c> buffer. The caller passes a buffer of at
    /// least <see cref="StatxSize"/> bytes.
    /// </summary>
    /// <param name="buffer">The raw status buffer.</param>
    /// <returns>The parsed status.</returns>
    internal static LinuxFileStatus ParseStatx(ReadOnlySpan<byte> buffer)
    {
        var userId = BinaryPrimitives.ReadUInt32LittleEndian(buffer[StatxUserIdOffset..]);
        var groupId = BinaryPrimitives.ReadUInt32LittleEndian(buffer[StatxGroupIdOffset..]);
        var mode = BinaryPrimitives.ReadUInt16LittleEndian(buffer[StatxModeOffset..]);
        var inode = BinaryPrimitives.ReadUInt64LittleEndian(buffer[StatxInodeOffset..]);
        var size = BinaryPrimitives.ReadInt64LittleEndian(buffer[StatxSizeOffset..]);
        var seconds = BinaryPrimitives.ReadInt64LittleEndian(buffer[StatxMtimeSecondsOffset..]);
        var nanoseconds = BinaryPrimitives.ReadUInt32LittleEndian(buffer[StatxMtimeNanosecondsOffset..]);
        var deviceMajor = BinaryPrimitives.ReadUInt32LittleEndian(buffer[StatxDeviceMajorOffset..]);
        var deviceMinor = BinaryPrimitives.ReadUInt32LittleEndian(buffer[StatxDeviceMinorOffset..]);

        return new LinuxFileStatus(
            FileTypeFromMode(mode),
            size,
            ToUnixNanoseconds(seconds, nanoseconds),
            MakeDevice(deviceMajor, deviceMinor),
            inode,
            userId,
            groupId,
            (uint)(mode & PermissionMask));
    }

    /// <summary>
    /// Parses a raw <c>struct statx</c> buffer without throwing on an
    /// out-of-range timestamp; the caller's probe contract is a boolean.
    /// </summary>
    /// <param name="buffer">The raw status buffer.</param>
    /// <param name="status">The parsed status when the timestamp fits.</param>
    /// <returns><see langword="true"/> when the buffer was parsed.</returns>
    internal static bool TryParseStatx(ReadOnlySpan<byte> buffer, out LinuxFileStatus status)
    {
        try
        {
            status = ParseStatx(buffer);
            return true;
        }
        catch (OverflowException)
        {
            status = default;
            return false;
        }
    }

    /// <summary>
    /// Parses a raw <c>struct stat</c> buffer for an explicit architecture.
    /// </summary>
    /// <param name="buffer">The raw status buffer.</param>
    /// <param name="architecture">The architecture whose layout applies.</param>
    /// <param name="status">The parsed status when the layout is known.</param>
    /// <returns><see langword="true"/> when the layout is known and parsed.</returns>
    internal static bool TryParseStat(ReadOnlySpan<byte> buffer, Architecture architecture, out LinuxFileStatus status)
    {
        if (!TryGetStatFieldOffsets(architecture, out var offsets))
        {
            status = default;
            return false;
        }

        var mode = (ushort)BinaryPrimitives.ReadUInt32LittleEndian(buffer[offsets.Mode..]);
        var userId = BinaryPrimitives.ReadUInt32LittleEndian(buffer[offsets.UserId..]);
        var groupId = BinaryPrimitives.ReadUInt32LittleEndian(buffer[offsets.GroupId..]);
        var inode = BinaryPrimitives.ReadUInt64LittleEndian(buffer[StatInodeOffset..]);
        var size = BinaryPrimitives.ReadInt64LittleEndian(buffer[StatSizeOffset..]);
        var seconds = BinaryPrimitives.ReadInt64LittleEndian(buffer[StatMtimeSecondsOffset..]);
        var nanoseconds = BinaryPrimitives.ReadUInt32LittleEndian(buffer[StatMtimeNanosecondsOffset..]);
        var rawDevice = BinaryPrimitives.ReadUInt64LittleEndian(buffer[StatDeviceOffset..]);

        if (!TryToUnixNanoseconds(seconds, nanoseconds, out var mtime))
        {
            status = default;
            return false;
        }

        status = new LinuxFileStatus(
            FileTypeFromMode(mode),
            size,
            mtime,
            MakeDevice(DeviceMajor(rawDevice), DeviceMinor(rawDevice)),
            inode,
            userId,
            groupId,
            (uint)(mode & PermissionMask));

        return true;
    }

    /// <summary>
    /// Maps an <c>st_mode</c> value to a file type.
    /// </summary>
    /// <param name="mode">The raw mode.</param>
    /// <returns>The file type, or <see cref="LinuxFileType.Unknown"/>.</returns>
    internal static LinuxFileType FileTypeFromMode(ushort mode)
    {
        return (ushort)(mode & ModeTypeMask) switch
        {
            ModeFifo => LinuxFileType.Fifo,
            ModeCharacterDevice => LinuxFileType.CharacterDevice,
            ModeDirectory => LinuxFileType.Directory,
            ModeBlockDevice => LinuxFileType.BlockDevice,
            ModeRegularFile => LinuxFileType.RegularFile,
            ModeSymbolicLink => LinuxFileType.SymbolicLink,
            ModeSocket => LinuxFileType.Socket,
            _ => LinuxFileType.Unknown,
        };
    }

    /// <summary>
    /// Encodes a device major and minor number into a single identity value.
    /// </summary>
    /// <param name="major">The major number.</param>
    /// <param name="minor">The minor number.</param>
    /// <returns>The packed identity.</returns>
    internal static ulong MakeDevice(uint major, uint minor)
    {
        return ((ulong)major << 32) | minor;
    }

    /// <summary>
    /// Decodes the major number from a raw Linux <c>dev_t</c> (<c>st_dev</c>), so
    /// the <c>fstatat</c> fallback packs it like <c>statx</c> does.
    /// </summary>
    /// <param name="device">The raw <c>st_dev</c> value.</param>
    /// <returns>The major number.</returns>
    internal static uint DeviceMajor(ulong device)
    {
        return (uint)(((device >> 8) & 0xFFFUL) | ((device >> 32) & ~0xFFFUL));
    }

    /// <summary>
    /// Decodes the minor number from a raw Linux <c>dev_t</c> (<c>st_dev</c>).
    /// </summary>
    /// <param name="device">The raw <c>st_dev</c> value.</param>
    /// <returns>The minor number.</returns>
    internal static uint DeviceMinor(ulong device)
    {
        return (uint)((device & 0xFFUL) | ((device >> 12) & ~0xFFUL));
    }

    /// <summary>
    /// Combines split seconds and nanoseconds into a Unix nanosecond value.
    /// </summary>
    /// <param name="seconds">The seconds component.</param>
    /// <param name="nanoseconds">The nanoseconds component.</param>
    /// <returns>The combined value.</returns>
    /// <exception cref="OverflowException">The timestamp does not fit a 64-bit nanosecond value.</exception>
    internal static long ToUnixNanoseconds(long seconds, uint nanoseconds)
    {
        // Checked: a corrupt or out-of-range timestamp must fail loudly, not wrap.
        return checked((seconds * 1_000_000_000L) + nanoseconds);
    }

    /// <summary>
    /// Combines split seconds and nanoseconds, returning <see langword="false"/>
    /// instead of throwing when the timestamp does not fit.
    /// </summary>
    internal static bool TryToUnixNanoseconds(long seconds, uint nanoseconds, out long value)
    {
        try
        {
            value = ToUnixNanoseconds(seconds, nanoseconds);
            return true;
        }
        catch (OverflowException)
        {
            value = 0;
            return false;
        }
    }
}
