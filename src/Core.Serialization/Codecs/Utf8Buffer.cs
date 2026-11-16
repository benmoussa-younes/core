// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Buffers;
using System.Text;
using Core.Results;

namespace Core.Serialization.Codecs;

/// <summary>
/// The shared UTF-8 emission the hand-rolled text codecs use: a string, a single
/// byte, and the <c>Result&lt;byte[]&gt;</c> wrapper that runs a writer into a
/// fresh buffer.
/// </summary>
internal static class Utf8Buffer
{
    /// <summary>Runs a writer into a new UTF-8 byte array.</summary>
    /// <typeparam name="TState">The writer's input.</typeparam>
    /// <param name="state">The value passed to the writer.</param>
    /// <param name="write">The writer, a static method so no closure allocates.</param>
    /// <returns>The bytes, or the writer's failure.</returns>
    internal static Result<byte[]> ToUtf8Bytes<TState>(
        TState state,
        Func<TState, IBufferWriter<byte>, Result<bool>> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var result = write(state, buffer);

        return result.IsSuccess
            ? Result<byte[]>.Success(buffer.WrittenSpan.ToArray())
            : Result<byte[]>.Failure(result.Error);
    }

    /// <summary>Writes a string as UTF-8.</summary>
    /// <param name="writer">The destination buffer.</param>
    /// <param name="text">The text; empty writes nothing.</param>
    internal static void Write(IBufferWriter<byte> writer, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        var length = Encoding.UTF8.GetByteCount(text);
        var span = writer.GetSpan(length);
        Encoding.UTF8.GetBytes(text, span);
        writer.Advance(length);
    }

    /// <summary>Writes a single byte.</summary>
    /// <param name="writer">The destination buffer.</param>
    /// <param name="value">The byte.</param>
    internal static void WriteByte(IBufferWriter<byte> writer, byte value)
    {
        var span = writer.GetSpan(1);
        span[0] = value;
        writer.Advance(1);
    }
}
