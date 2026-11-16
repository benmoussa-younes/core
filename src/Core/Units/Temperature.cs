// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Globalization;
using Core.Results;

namespace Core.Units;

/// <summary>
/// An exact temperature stored as a signed number of millidegrees Celsius.
/// </summary>
public readonly struct Temperature : IEquatable<Temperature>, IComparable<Temperature>
{
    private const long MillidegreesPerDegree = 1_000L;
    private const long KelvinOffsetInMillidegrees = 273_150L;

    private static readonly ulong[] HumanMultipliers = [1UL, (ulong)MillidegreesPerDegree];

    private static readonly string[] HumanSymbols = ["mC", "C"];

    private readonly long _millidegreesCelsius;

    /// <summary>
    /// Initializes a new instance of the <see cref="Temperature"/> struct from an exact number of
    /// millidegrees Celsius.
    /// </summary>
    /// <param name="millidegreesCelsius">The signed number of millidegrees Celsius.</param>
    public Temperature(long millidegreesCelsius)
    {
        _millidegreesCelsius = millidegreesCelsius;
    }

    /// <summary>
    /// Gets the exact number of millidegrees Celsius.
    /// </summary>
    public long MillidegreesCelsius => _millidegreesCelsius;

    /// <summary>Creates a temperature from an exact number of millidegrees Celsius.</summary>
    /// <param name="millidegreesCelsius">The number of millidegrees Celsius.</param>
    /// <returns>The temperature.</returns>
    public static Temperature FromMillidegreesCelsius(long millidegreesCelsius) => new(millidegreesCelsius);

    /// <summary>Creates a temperature from a number of degrees Celsius.</summary>
    /// <param name="degreesCelsius">The number of degrees Celsius.</param>
    /// <returns>The temperature.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Temperature FromDegreesCelsius(long degreesCelsius) => new(checked(degreesCelsius * MillidegreesPerDegree));

    /// <summary>Creates a temperature from a number of kelvin.</summary>
    /// <param name="kelvin">The number of kelvin.</param>
    /// <returns>The temperature.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Temperature FromKelvin(long kelvin) => new(checked(kelvin * MillidegreesPerDegree - KelvinOffsetInMillidegrees));

    /// <summary>
    /// Parses an invariant temperature such as <c>1500</c>, <c>-1500 mC</c>, <c>21.5 C</c> or
    /// <c>300 K</c>.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="temperature">The parsed temperature when parsing succeeds.</param>
    /// <returns><see langword="true"/> when parsing succeeds; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// A bare number and the <c>mC</c>, <c>C</c> and <c>K</c> suffixes are accepted; <c>K</c> is
    /// converted through the <c>273.15</c> offset. A value must resolve to a whole number of
    /// millidegrees Celsius.
    /// </remarks>
    public static bool TryParse(ReadOnlySpan<char> text, out Temperature temperature)
    {
        temperature = default;
        if (!QuantityParser.TrySplit(text, out var number, out var suffix)
            || !TryGetUnit(suffix, out var multiplier, out var offset)
            || !QuantityParser.TryScale(number, multiplier, out var millidegrees))
        {
            return false;
        }

        if (offset != 0)
        {
            if (millidegrees < long.MinValue - offset)
            {
                return false;
            }

            millidegrees += offset;
        }

        temperature = new Temperature(millidegrees);
        return true;
    }

    /// <summary>
    /// Parses an invariant temperature.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The parsed temperature, or a <see cref="CoreError"/> when parsing fails.</returns>
    public static Result<Temperature> Parse(ReadOnlySpan<char> text)
    {
        return TryParse(text, out var temperature)
            ? Result<Temperature>.Success(temperature)
            : Result<Temperature>.Failure(new CoreError("units.invalid", $"Invalid temperature: '{text.ToString()}'."));
    }

    /// <summary>
    /// Returns the exact temperature in the invariant round-trippable form <c>"{millidegrees} mC"</c>.
    /// </summary>
    /// <returns>The exact temperature.</returns>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{_millidegreesCelsius} mC");
    }

    /// <summary>
    /// Returns a compact representation such as <c>21.5 C</c> or <c>-12 mC</c>.
    /// </summary>
    /// <returns>The human-readable temperature.</returns>
    public string ToHumanString()
    {
        return QuantityFormatter.ToHumanString(_millidegreesCelsius, HumanMultipliers, HumanSymbols);
    }

    /// <summary>
    /// Determines whether this instance equals another instance.
    /// </summary>
    /// <param name="other">The other instance.</param>
    /// <returns><see langword="true"/> when both hold the same number of millidegrees; otherwise <see langword="false"/>.</returns>
    public bool Equals(Temperature other)
    {
        return _millidegreesCelsius == other._millidegreesCelsius;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Temperature other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return _millidegreesCelsius.GetHashCode();
    }

    /// <summary>
    /// Compares this instance with another instance.
    /// </summary>
    /// <param name="other">The other instance.</param>
    /// <returns>The comparison result.</returns>
    public int CompareTo(Temperature other)
    {
        return _millidegreesCelsius.CompareTo(other._millidegreesCelsius);
    }

    /// <summary>Adds the stored values of two temperatures.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Temperature operator +(Temperature left, Temperature right) => new(checked(left._millidegreesCelsius + right._millidegreesCelsius));

    /// <summary>Subtracts the stored values of two temperatures.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Temperature operator -(Temperature left, Temperature right) => new(checked(left._millidegreesCelsius - right._millidegreesCelsius));

    /// <summary>Negates a temperature.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The negated temperature.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Temperature operator -(Temperature value) => new(checked(-value._millidegreesCelsius));

    /// <summary>Compares two temperatures for equality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Temperature left, Temperature right) => left.Equals(right);

    /// <summary>Compares two temperatures for inequality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Temperature left, Temperature right) => !left.Equals(right);

    /// <summary>Determines whether the left temperature is smaller than the right temperature.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when left is smaller; otherwise <see langword="false"/>.</returns>
    public static bool operator <(Temperature left, Temperature right) => left._millidegreesCelsius < right._millidegreesCelsius;

    /// <summary>Determines whether the left temperature is greater than the right temperature.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when left is greater; otherwise <see langword="false"/>.</returns>
    public static bool operator >(Temperature left, Temperature right) => left._millidegreesCelsius > right._millidegreesCelsius;

    /// <summary>Determines whether the left temperature is smaller than or equal to the right temperature.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when left is smaller or equal; otherwise <see langword="false"/>.</returns>
    public static bool operator <=(Temperature left, Temperature right) => left._millidegreesCelsius <= right._millidegreesCelsius;

    /// <summary>Determines whether the left temperature is greater than or equal to the right temperature.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when left is greater or equal; otherwise <see langword="false"/>.</returns>
    public static bool operator >=(Temperature left, Temperature right) => left._millidegreesCelsius >= right._millidegreesCelsius;

    private static bool TryGetUnit(ReadOnlySpan<char> suffix, out long multiplier, out long offset)
    {
        multiplier = 1;
        offset = 0;
        if (suffix.IsEmpty || suffix.SequenceEqual("mC") || suffix.SequenceEqual("m°C"))
        {
            return true;
        }

        if (suffix.SequenceEqual("C") || suffix.SequenceEqual("°C"))
        {
            multiplier = MillidegreesPerDegree;
            return true;
        }

        if (suffix.SequenceEqual("K"))
        {
            multiplier = MillidegreesPerDegree;
            offset = -KelvinOffsetInMillidegrees;
            return true;
        }

        return false;
    }
}
