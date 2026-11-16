// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.IO.Files;

namespace Core.Platform.Linux;

/// <summary>
/// Receives one regular file of a <see cref="RawFileWalker"/> walk.
/// </summary>
/// <param name="relativePath">The root-relative path as UTF-8, without a leading slash; valid only during the call.</param>
/// <param name="directoryDescriptor">The open parent directory, or <c>-1</c> for a root file.</param>
/// <param name="name">The entry name as UTF-8; valid only during the call.</param>
/// <param name="identity">The device and inode the directory stream reported, or <see cref="FileIdentity.Unknown"/>.</param>
public delegate void RawFileHandler(
    ReadOnlySpan<byte> relativePath,
    int directoryDescriptor,
    ReadOnlySpan<byte> name,
    FileIdentity identity);

