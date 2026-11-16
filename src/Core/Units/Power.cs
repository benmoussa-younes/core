// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Globalization;
using Core.Results;

namespace Core.Units;

/// <summary>
/// An exact quantity of power stored as a signed number of microwatts.
/// </summary>
public readonly struct Power : IEquatable<Power>, IComparable<Power>
{
    private const long MicrowattsPerMilliwatt = 1_000L;
    private const long MicrowattsPerWatt = 1_000_000L;
    private const long MicrowattsPerKilowatt = 1_000_000_000L;
    private const long MicrowattsPerMegawatt = 1_000_000_000_000L;
    private const long MicrowattsPerGigawatt = 1_000_000_000_000_000L;
    private const long MicrowattsPerTerawatt = 1_000_000_000_000_000_000L;

    private static readonly ulong[] HumanMultipliers =
    [
        1UL,
        (ulong)MicrowattsPerMilliwatt,
        (ulong)MicrowattsPerWatt,
        (ulong)MicrowattsPerKilowatt,
        (ulong)MicrowattsPerMegawatt,
        (ulong)MicrowattsPerGigawatt,
        (ulong)MicrowattsPerTerawatt,
    ];

    private static readonly string[] HumanSymbols = ["uW", "mW", "W", "kW", "MW", "GW", "TW"];

    private readonly long _microwatts;

    /// <summary>
    /// Initializes a new instance of the <see cref="Power"/> struct from an exact number of microwatts.
    /// </summary>
    /// <param name="microwatts">The signed number of microwatts.</param>
    public Power(long microwatts)
    {
        _microwatts = microwatts;
    }

    /// <summary>
    /// Gets the exact number of microwatts.
    /// </summary>
    public long Microwatts => _microwatts;

    /// <summary>Creates a power from an exact number of microwatts.</summary>
    /// <param name="microwatts">The number of microwatts.</param>
    /// <returns>The power.</returns>
    public static Power FromMicrowatts(long microwatts) => new(microwatts);

    /// <summary>Creates a power from a number of milliwatts.</summary>
    /// <param name="milliwatts">The number of milliwatts.</param>
    /// <returns>The power.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Power FromMilliwatts(long milliwatts) => new(checked(milliwatts * MicrowattsPerMilliwatt));

    /// <summary>Creates a power from a number of watts.</summary>
    /// <param name="watts">The number of watts.</param>
    /// <returns>The power.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Power FromWatts(long watts) => new(checked(watts * MicrowattsPerWatt));

    /// <summary>Creates a power from a number of kilowatts.</summary>
    /// <param name="kilowatts">The number of kilowatts.</param>
    /// <returns>The power.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Power FromKilowatts(long kilowatts) => new(checked(kilowatts * MicrowattsPerKilowatt));

    /// <summary>Creates a power from a number of megawatts.</summary>
    /// <param name="megawatts">The number of megawatts.</param>
    /// <returns>The power.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Power FromMegawatts(long megawatts) => new(checked(megawatts * MicrowattsPerMegawatt));

    /// <summary>Creates a power from a number of gigawatts.</summary>
    /// <param name="gigawatts">The number of gigawatts.</param>
    /// <returns>The power.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Power FromGigawatts(long gigawatts) => new(checked(gigawatts * MicrowattsPerGigawatt));

    /// <summary>Creates a power from a number of terawatts.</summary>
    /// <param name="terawatts">The number of terawatts.</param>
    /// <returns>The power.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Power FromTerawatts(long terawatts) => new(checked(terawatts * MicrowattsPerTerawatt));

    /// <summary>
    /// Parses an invariant power such as <c>1500</c>, <c>-1500 uW</c>, <c>1.5 mW</c>, <c>2 W</c>
    /// or <c>3 kW</c>.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="power">The parsed power when parsing succeeds.</param>
    /// <returns><see langword="true"/> when parsing succeeds; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// A bare number and the <c>uW</c>, <c>mW</c>, <c>W</c>, <c>kW</c>, <c>MW</c>, <c>GW</c> and
    /// <c>TW</c> suffixes are accepted. A value must resolve to a whole number of microwatts.
    /// </remarks>
    public static bool TryParse(ReadOnlySpan<char> text, out Power power)
    {
        power = default;
        if (!QuantityParser.TrySplit(text, out var number, out var suffix)
            || !TryGetMultiplier(suffix, out var multiplier)
            || !QuantityParser.TryScale(number, multiplier, out var microwatts))
        {
            return false;
        }

        power = new Power(microwatts);
        return true;
    }

    /// <summary>
    /// Parses an invariant power.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The parsed power, or a <see cref="CoreError"/> when parsing fails.</returns>
    public static Result<Power> Parse(ReadOnlySpan<char> text)
    {
        return TryParse(text, out var power)
            ? Result<Power>.Success(power)
            : Result<Power>.Failure(new CoreError("units.invalid", $"Invalid power: '{text.ToString()}'."));
    }

    /// <summary>
    /// Returns the exact power in the invariant round-trippable form <c>"{microwatts} uW"</c>.
    /// </summary>
    /// <returns>The exact power.</returns>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{_microwatts} uW");
    }

    /// <summary>
    /// Returns a compact representation such as <c>1.5 W</c> or <c>-12 mW</c>.
    /// </summary>
    /// <returns>The human-readable power.</returns>
    public string ToHumanString()
    {
        return QuantityFormatter.ToHumanString(_microwatts, HumanMultipliers, HumanSymbols);
    }

    /// <summary>
    /// Determines whether this instance equals another instance.
    /// </summary>
    /// <param name="other">The other instance.</param>
    /// <returns><see langword="true"/> when both hold the same number of microwatts; otherwise <see langword="false"/>.</returns>
    public bool Equals(Power other)
    {
        return _microwatts == other._microwatts;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Power other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return _microwatts.GetHashCode();
    }

    /// <summary>
    /// Compares this instance with another instance.
    /// </summary>
    /// <param name="other">The other instance.</param>
    /// <returns>The comparison result.</returns>
    public int CompareTo(Power other)
    {
        return _microwatts.CompareTo(other._microwatts);
    }

    /// <summary>Adds two powers.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Power operator +(Power left, Power right) => new(checked(left._microwatts + right._microwatts));

    /// <summary>Subtracts two powers.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Power operator -(Power left, Power right) => new(checked(left._microwatts - right._microwatts));

    /// <summary>Negates a power.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The negated power.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Power operator -(Power value) => new(checked(-value._microwatts));

    /// <summary>Compares two powers for equality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Power left, Power right) => left.Equals(right);

    /// <summary>Compares two powers for inequality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Power left, Power right) => !left.Equals(right);

    /// <summary>Determines whether the left power is smaller than the right power.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when left is smaller; otherwise <see langword="false"/>.</returns>
    public static bool operator <(Power left, Power right) => left._microwatts < right._microwatts;

    /// <summary>Determines whether the left power is greater than the right power.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when left is greater; otherwise <see langword="false"/>.</returns>
    public static bool operator >(Power left, Power right) => left._microwatts > right._microwatts;

    /// <summary>Determines whether the left power is smaller than or equal to the right power.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when left is smaller or equal; otherwise <see langword="false"/>.</returns>
    public static bool operator <=(Power left, Power right) => left._microwatts <= right._microwatts;

    /// <summary>Determines whether the left power is greater than or equal to the right power.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when left is greater or equal; otherwise <see langword="false"/>.</returns>
    public static bool operator >=(Power left, Power right) => left._microwatts >= right._microwatts;

    private static bool TryGetMultiplier(ReadOnlySpan<char> suffix, out long multiplier)
    {
        multiplier = 1;
        if (suffix.IsEmpty
            || suffix.SequenceEqual("uW")
            || suffix.SequenceEqual("µW")
            || suffix.SequenceEqual("μW"))
        {
            return true;
        }

        if (suffix.SequenceEqual("mW"))
        {
            multiplier = MicrowattsPerMilliwatt;
            return true;
        }

        if (suffix.SequenceEqual("W"))
        {
            multiplier = MicrowattsPerWatt;
            return true;
        }

        if (suffix.SequenceEqual("kW"))
        {
            multiplier = MicrowattsPerKilowatt;
            return true;
        }

        if (suffix.SequenceEqual("MW"))
        {
            multiplier = MicrowattsPerMegawatt;
            return true;
        }

        if (suffix.SequenceEqual("GW"))
        {
            multiplier = MicrowattsPerGigawatt;
            return true;
        }

        if (suffix.SequenceEqual("TW"))
        {
            multiplier = MicrowattsPerTerawatt;
            return true;
        }

        return false;
    }
}
