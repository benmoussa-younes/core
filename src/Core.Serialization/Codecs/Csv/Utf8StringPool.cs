// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;

namespace Core.Serialization.Codecs.Csv;

/// <summary>
/// A bounded UTF-8 string intern pool for CSV text: a repeated value is decoded
/// once and returned again, so a large table with a low-cardinality text column
/// does not hold a string per row. Once the pool is full, later values are
/// decoded without being kept. A hash collision is detected by comparing the
/// original bytes, so a wrong string is never returned.
/// </summary>
internal sealed class Utf8StringPool
{
    private readonly int _capacity;
    private readonly Dictionary<ulong, (string Text, byte[] Bytes)> _entries = [];

    internal Utf8StringPool(int capacity)
    {
        _capacity = capacity;
    }

    internal string Get(ReadOnlySpan<byte> utf8)
    {
        if (_entries.Count < _capacity)
        {
            var hash = Hash(utf8);

            if (_entries.TryGetValue(hash, out var entry) && entry.Bytes.AsSpan().SequenceEqual(utf8))
            {
                return entry.Text;
            }

            var text = Encoding.UTF8.GetString(utf8);
            _entries[hash] = (text, utf8.ToArray());

            return text;
        }

        return Encoding.UTF8.GetString(utf8);
    }

    private static ulong Hash(ReadOnlySpan<byte> utf8)
    {
        // FNV-1a 64: a cheap, well-distributed hash for short strings.
        var hash = 14695981039346656037UL;

        foreach (var value in utf8)
        {
            hash = (hash ^ value) * 1099511628211UL;
        }

        return hash;
    }
}
