// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Globalization;
using Core.Results;

namespace Core.Units;

/// <summary>
/// An exact frequency stored as a signed number of hertz.
/// </summary>
public readonly struct Frequency : IEquatable<Frequency>, IComparable<Frequency>
{
    private const long HertzPerKilohertz = 1_000L;
    private const long HertzPerMegahertz = 1_000_000L;
    private const long HertzPerGigahertz = 1_000_000_000L;
    private const long HertzPerTerahertz = 1_000_000_000_000L;
    private const long HertzPerPetahertz = 1_000_000_000_000_000L;
    private const long HertzPerExahertz = 1_000_000_000_000_000_000L;

    private static readonly ulong[] HumanMultipliers =
    [
        1UL,
        (ulong)HertzPerKilohertz,
        (ulong)HertzPerMegahertz,
        (ulong)HertzPerGigahertz,
        (ulong)HertzPerTerahertz,
        (ulong)HertzPerPetahertz,
        (ulong)HertzPerExahertz,
    ];

    private static readonly string[] HumanSymbols = ["Hz", "kHz", "MHz", "GHz", "THz", "PHz", "EHz"];

    private readonly long _hertz;

    /// <summary>
    /// Initializes a new instance of the <see cref="Frequency"/> struct from an exact number of hertz.
    /// </summary>
    /// <param name="hertz">The signed number of hertz.</param>
    public Frequency(long hertz)
    {
        _hertz = hertz;
    }

    /// <summary>
    /// Gets the exact number of hertz.
    /// </summary>
    public long Hertz => _hertz;

    /// <summary>Creates a frequency from an exact number of hertz.</summary>
    /// <param name="hertz">The number of hertz.</param>
    /// <returns>The frequency.</returns>
    public static Frequency FromHertz(long hertz) => new(hertz);

    /// <summary>Creates a frequency from a number of kilohertz.</summary>
    /// <param name="kilohertz">The number of kilohertz.</param>
    /// <returns>The frequency.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Frequency FromKilohertz(long kilohertz) => new(checked(kilohertz * HertzPerKilohertz));

    /// <summary>Creates a frequency from a number of megahertz.</summary>
    /// <param name="megahertz">The number of megahertz.</param>
    /// <returns>The frequency.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Frequency FromMegahertz(long megahertz) => new(checked(megahertz * HertzPerMegahertz));

    /// <summary>Creates a frequency from a number of gigahertz.</summary>
    /// <param name="gigahertz">The number of gigahertz.</param>
    /// <returns>The frequency.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Frequency FromGigahertz(long gigahertz) => new(checked(gigahertz * HertzPerGigahertz));

    /// <summary>Creates a frequency from a number of terahertz.</summary>
    /// <param name="terahertz">The number of terahertz.</param>
    /// <returns>The frequency.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Frequency FromTerahertz(long terahertz) => new(checked(terahertz * HertzPerTerahertz));

    /// <summary>Creates a frequency from a number of petahertz.</summary>
    /// <param name="petahertz">The number of petahertz.</param>
    /// <returns>The frequency.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Frequency FromPetahertz(long petahertz) => new(checked(petahertz * HertzPerPetahertz));

    /// <summary>Creates a frequency from a number of exahertz.</summary>
    /// <param name="exahertz">The number of exahertz.</param>
    /// <returns>The frequency.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Frequency FromExahertz(long exahertz) => new(checked(exahertz * HertzPerExahertz));

    /// <summary>
    /// Parses an invariant frequency such as <c>1500</c>, <c>-1500 Hz</c>, <c>1.5 kHz</c>,
    /// <c>2 MHz</c> or <c>3 GHz</c>.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="frequency">The parsed frequency when parsing succeeds.</param>
    /// <returns><see langword="true"/> when parsing succeeds; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// A bare number and the <c>Hz</c>, <c>kHz</c>, <c>MHz</c>, <c>GHz</c>, <c>THz</c>, <c>PHz</c>
    /// and <c>EHz</c> suffixes are accepted. A value must resolve to a whole number of hertz.
    /// </remarks>
    public static bool TryParse(ReadOnlySpan<char> text, out Frequency frequency)
    {
        frequency = default;
        if (!QuantityParser.TrySplit(text, out var number, out var suffix)
            || !TryGetMultiplier(suffix, out var multiplier)
            || !QuantityParser.TryScale(number, multiplier, out var hertz))
        {
            return false;
        }

        frequency = new Frequency(hertz);
        return true;
    }

    /// <summary>
    /// Parses an invariant frequency.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The parsed frequency, or a <see cref="CoreError"/> when parsing fails.</returns>
    public static Result<Frequency> Parse(ReadOnlySpan<char> text)
    {
        return TryParse(text, out var frequency)
            ? Result<Frequency>.Success(frequency)
            : Result<Frequency>.Failure(new CoreError("units.invalid", $"Invalid frequency: '{text.ToString()}'."));
    }

    /// <summary>
    /// Returns the exact frequency in the invariant round-trippable form <c>"{hertz} Hz"</c>.
    /// </summary>
    /// <returns>The exact frequency.</returns>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{_hertz} Hz");
    }

    /// <summary>
    /// Returns a compact representation such as <c>1.5 GHz</c> or <c>-12 kHz</c>.
    /// </summary>
    /// <returns>The human-readable frequency.</returns>
    public string ToHumanString()
    {
        return QuantityFormatter.ToHumanString(_hertz, HumanMultipliers, HumanSymbols);
    }

    /// <summary>
    /// Determines whether this instance equals another instance.
    /// </summary>
    /// <param name="other">The other instance.</param>
    /// <returns><see langword="true"/> when both hold the same number of hertz; otherwise <see langword="false"/>.</returns>
    public bool Equals(Frequency other)
    {
        return _hertz == other._hertz;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Frequency other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return _hertz.GetHashCode();
    }

    /// <summary>
    /// Compares this instance with another instance.
    /// </summary>
    /// <param name="other">The other instance.</param>
    /// <returns>The comparison result.</returns>
    public int CompareTo(Frequency other)
    {
        return _hertz.CompareTo(other._hertz);
    }

    /// <summary>Adds two frequencies.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Frequency operator +(Frequency left, Frequency right) => new(checked(left._hertz + right._hertz));

    /// <summary>Subtracts two frequencies.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Frequency operator -(Frequency left, Frequency right) => new(checked(left._hertz - right._hertz));

    /// <summary>Negates a frequency.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The negated frequency.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Frequency operator -(Frequency value) => new(checked(-value._hertz));

    /// <summary>Compares two frequencies for equality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Frequency left, Frequency right) => left.Equals(right);

    /// <summary>Compares two frequencies for inequality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Frequency left, Frequency right) => !left.Equals(right);

    /// <summary>Determines whether the left frequency is smaller than the right frequency.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when left is smaller; otherwise <see langword="false"/>.</returns>
    public static bool operator <(Frequency left, Frequency right) => left._hertz < right._hertz;

    /// <summary>Determines whether the left frequency is greater than the right frequency.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when left is greater; otherwise <see langword="false"/>.</returns>
    public static bool operator >(Frequency left, Frequency right) => left._hertz > right._hertz;

    /// <summary>Determines whether the left frequency is smaller than or equal to the right frequency.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when left is smaller or equal; otherwise <see langword="false"/>.</returns>
    public static bool operator <=(Frequency left, Frequency right) => left._hertz <= right._hertz;

    /// <summary>Determines whether the left frequency is greater than or equal to the right frequency.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when left is greater or equal; otherwise <see langword="false"/>.</returns>
    public static bool operator >=(Frequency left, Frequency right) => left._hertz >= right._hertz;

    private static bool TryGetMultiplier(ReadOnlySpan<char> suffix, out long multiplier)
    {
        multiplier = 1;
        if (suffix.IsEmpty || suffix.SequenceEqual("Hz"))
        {
            return true;
        }

        if (suffix.SequenceEqual("kHz"))
        {
            multiplier = HertzPerKilohertz;
            return true;
        }

        if (suffix.SequenceEqual("MHz"))
        {
            multiplier = HertzPerMegahertz;
            return true;
        }

        if (suffix.SequenceEqual("GHz"))
        {
            multiplier = HertzPerGigahertz;
            return true;
        }

        if (suffix.SequenceEqual("THz"))
        {
            multiplier = HertzPerTerahertz;
            return true;
        }

        if (suffix.SequenceEqual("PHz"))
        {
            multiplier = HertzPerPetahertz;
            return true;
        }

        if (suffix.SequenceEqual("EHz"))
        {
            multiplier = HertzPerExahertz;
            return true;
        }

        return false;
    }
}
