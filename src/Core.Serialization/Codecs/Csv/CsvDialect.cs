// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Serialization.Codecs.Csv;

/// <summary>
/// The CSV dialect: the field and record separators and the header and
/// byte-order-mark conventions, so a file reopens exactly as it was written.
/// </summary>
/// <remarks>
/// A consumer that needs <c>;</c>, a tab or <c>|</c> separators, CRLF endings or a
/// byte-order mark supplies a dialect instead of post-processing the bytes. The
/// delimiter and the quote must be single-byte (ASCII) characters; a
/// <see cref="Quote"/> of <c>'\0'</c> disables quoting.
/// </remarks>
public sealed class CsvDialect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CsvDialect"/> class.
    /// </summary>
    /// <param name="delimiter">The single-byte field delimiter; defaults to a comma.</param>
    /// <param name="quote">The single-byte quote character, or <c>'\0'</c> to disable quoting.</param>
    /// <param name="hasHeader">Whether the first record is the header.</param>
    /// <param name="lineEnding">The record separator written between records (<c>"\n"</c>, <c>"\r\n"</c> or <c>"\r"</c>).</param>
    /// <param name="writeBom">Whether a UTF-8 byte-order mark is written first.</param>
    /// <exception cref="ArgumentException">
    /// The delimiter or quote is not a usable single byte, they are equal, or the
    /// line ending is not a supported separator.
    /// </exception>
    public CsvDialect(
        char delimiter = ',',
        char quote = '"',
        bool hasHeader = true,
        string lineEnding = "\n",
        bool writeBom = false)
    {
        if (delimiter is '\0' or '\r' or '\n' || delimiter > 0x7F)
        {
            throw new ArgumentException("The delimiter must be a single-byte character other than a newline.", nameof(delimiter));
        }

        if (quote > 0x7F || quote is '\r' or '\n')
        {
            throw new ArgumentException("The quote must be a single-byte character other than a newline, or '\\0'.", nameof(quote));
        }

        if (quote != '\0' && quote == delimiter)
        {
            throw new ArgumentException("The quote and the delimiter must differ.", nameof(quote));
        }

        if (lineEnding is not ("\n" or "\r\n" or "\r"))
        {
            throw new ArgumentException("The line ending must be '\\n', '\\r\\n' or '\\r'.", nameof(lineEnding));
        }

        Delimiter = delimiter;
        Quote = quote;
        HasHeader = hasHeader;
        LineEnding = lineEnding;
        WriteBom = writeBom;
    }

    /// <summary>Gets the default dialect: a comma, a double quote, a header, LF and no BOM.</summary>
    public static CsvDialect Default { get; } = new();

    /// <summary>Gets the single-byte field delimiter.</summary>
    public char Delimiter { get; }

    /// <summary>Gets the single-byte quote character, or <c>'\0'</c> when quoting is disabled.</summary>
    public char Quote { get; }

    /// <summary>Gets a value indicating whether the first record is the header.</summary>
    public bool HasHeader { get; }

    /// <summary>Gets the record separator written between records.</summary>
    public string LineEnding { get; }

    /// <summary>Gets a value indicating whether a UTF-8 byte-order mark is written first.</summary>
    public bool WriteBom { get; }

    /// <summary>Gets a value indicating whether quoting is enabled.</summary>
    internal bool HasQuote => Quote != '\0';
}
