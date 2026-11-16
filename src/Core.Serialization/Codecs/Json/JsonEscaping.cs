// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text.Encodings.Web;

namespace Core.Serialization.Codecs.Json;

/// <summary>
/// The string-escaping modes <see cref="JsonDataWriter"/> can emit.
/// <see cref="Default"/> keeps the historical BCL behaviour — the writer's built-in
/// <see cref="JavaScriptEncoder"/> — so existing consumers are byte-for-byte
/// unaffected. <see cref="Utf8Raw"/> is the compact dialect a caller may need when
/// its canonical JSON keeps non-ASCII text as raw UTF-8 and spells every escape out
/// in lowercase hexadecimal.
/// </summary>
/// <remarks>
/// <para>
/// The two modes differ in exactly three ways, all applied to the text of a
/// <see cref="Values.DataKind.Text"/> scalar and to an object/map property name:
/// </para>
/// <list type="bullet">
/// <item><description>a character above U+007F is written as raw UTF-8 instead of <c>\uXXXX</c>;</description></item>
/// <item><description>U+0000–U+001F, U+007F and U+2028/U+2029 are always written as <c>\u00xx</c>/<c>\u202x</c>
/// with lowercase hex, never as the short forms <c>\b</c>, <c>\f</c>, <c>\n</c>, <c>\r</c>, <c>\t</c>;</description></item>
/// <item><description>only <c>"</c> and <c>\</c> keep a two-character escape.</description></item>
/// </list>
/// <para>
/// A lone surrogate is not valid UTF-16 text and <see cref="System.Text.Json.Utf8JsonWriter"/>
/// refuses it in either mode; <see cref="JsonDataWriter"/> turns that into a typed
/// failure rather than letting it escape.
/// </para>
/// </remarks>
public static class JsonEscaping
{
    /// <summary>The BCL default escaping (unchanged legacy behaviour).</summary>
    public static JavaScriptEncoder Default => JavaScriptEncoder.Default;

    /// <summary>Raw UTF-8 for non-ASCII, lowercase <c>\uXXXX</c> for every other escape.</summary>
    public static JavaScriptEncoder Utf8Raw { get; } = new Utf8RawEncoder();

    /// <summary>
    /// Escapes JSON text as raw UTF-8 with explicit lowercase hexadecimal escapes:
    /// no short forms and no non-ASCII escaping. It never encodes a scalar that the
    /// rules leave literal, so the writer transcodes it to UTF-8 verbatim.
    /// </summary>
    private sealed class Utf8RawEncoder : JavaScriptEncoder
    {
        private const string HexDigits = "0123456789abcdef";

        /// <summary>The widest output is <c>\uXXXX</c>.</summary>
        public override int MaxOutputCharactersPerInputCharacter => 6;

        /// <summary>True for the characters this mode escapes: <c>"</c>, <c>\</c>, U+0000–U+001F, U+007F, U+2028/U+2029 and any surrogate.</summary>
        /// <param name="unicodeScalar">The scalar.</param>
        /// <returns>Whether it is escaped.</returns>
        public override bool WillEncode(int unicodeScalar) =>
            unicodeScalar is '"' or '\\'
            || unicodeScalar < 0x20
            || unicodeScalar == 0x7F
            || unicodeScalar == 0x2028
            || unicodeScalar == 0x2029
            || unicodeScalar is >= 0xD800 and <= 0xDFFF;

        /// <summary>Finds the first escaped character so the writer can copy the untouched prefix verbatim.</summary>
        /// <param name="text">The UTF-16 text.</param>
        /// <param name="textLength">Its length.</param>
        /// <returns>The index, or <c>-1</c> when nothing needs escaping.</returns>
        public override unsafe int FindFirstCharacterToEncode(char* text, int textLength)
        {
            for (var index = 0; index < textLength; index++)
            {
                var current = text[index];

                if (current is '"' or '\\'
                    || current < 0x20
                    || current == 0x7F
                    || current == '\u2028'
                    || current == '\u2029'
                    || char.IsSurrogate(current))
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>Writes the escaped form of one scalar.</summary>
        /// <param name="unicodeScalar">The scalar.</param>
        /// <param name="buffer">The destination.</param>
        /// <param name="bufferLength">Its capacity.</param>
        /// <param name="written">The number of chars written.</param>
        /// <returns>Whether the buffer was large enough.</returns>
        public override unsafe bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int written)
        {
            written = 0;

            if (unicodeScalar is '"' or '\\')
            {
                if (bufferLength < 2)
                {
                    return false;
                }

                buffer[0] = '\\';
                buffer[1] = (char)unicodeScalar;
                written = 2;
                return true;
            }

            if (unicodeScalar is < 0x20 or 0x7F or 0x2028 or 0x2029)
            {
                if (bufferLength < 6)
                {
                    return false;
                }

                buffer[0] = '\\';
                buffer[1] = 'u';
                buffer[2] = HexDigits[(unicodeScalar >> 12) & 0xF];
                buffer[3] = HexDigits[(unicodeScalar >> 8) & 0xF];
                buffer[4] = HexDigits[(unicodeScalar >> 4) & 0xF];
                buffer[5] = HexDigits[unicodeScalar & 0xF];
                written = 6;
                return true;
            }

            // A lone surrogate has no JSON form: report it so the writer refuses it
            // (Utf8JsonWriter throws ArgumentException, which JsonDataWriter maps to
            // a typed failure). A valid scalar should never reach here because
            // WillEncode is false for it.
            return false;
        }
    }
}
