// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Microsoft.Win32.SafeHandles;

namespace Core.Platform.Linux;

/// <summary>
/// Owns a raw Linux file descriptor and closes it with <c>close(2)</c> when it is
/// released, including from a finalizer.
/// </summary>
/// <remarks>
/// Wrapping the descriptor in a <see cref="SafeHandleMinusOneIsInvalid"/> makes
/// the lifetime exception-safe and finalizable without a hand-written finalizer
/// in the public type. Descriptor 0 is a valid Linux descriptor, so only -1 is
/// invalid; the conventional zero-or-minus-one rule would leak a pipe end that
/// landed on descriptor 0.
/// </remarks>
internal sealed class LinuxFileDescriptor : SafeHandleMinusOneIsInvalid
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxFileDescriptor"/> class.
    /// </summary>
    /// <param name="descriptor">The owned descriptor.</param>
    internal LinuxFileDescriptor(int descriptor)
        : base(ownsHandle: true)
    {
        SetHandle(descriptor);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        return Interop.Close((int)handle) == 0;
    }
}
