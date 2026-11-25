// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform.Linux;

/// <summary>
/// Maps one descriptor of the parent process to a descriptor number of the child.
/// </summary>
/// <param name="SourceDescriptor">The open parent descriptor the child receives a copy of.</param>
/// <param name="TargetDescriptor">The descriptor number in the child.</param>
public readonly record struct UnixDescriptorMapping(int SourceDescriptor, int TargetDescriptor);
