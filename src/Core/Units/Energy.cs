// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Globalization;
using Core.Results;

namespace Core.Units;

/// <summary>
/// An exact quantity of energy stored as a signed number of microjoules.
/// </summary>
public readonly struct Energy : IEquatable<Energy>, IComparable<Energy>
{
    private const long MicrojoulesPerMillijoule = 1_000L;
    private const long MicrojoulesPerJoule = 1_000_000L;
    private const long MicrojoulesPerKilojoule = 1_000_000_000L;
    private const long MicrojoulesPerMegajoule = 1_000_000_000_000L;
    private const long MicrojoulesPerGigajoule = 1_000_000_000_000_000L;
    private const long MicrojoulesPerTerajoule = 1_000_000_000_000_000_000L;

    private static readonly ulong[] HumanMultipliers =
    [
        1UL,
        (ulong)MicrojoulesPerMillijoule,
        (ulong)MicrojoulesPerJoule,
        (ulong)MicrojoulesPerKilojoule,
        (ulong)MicrojoulesPerMegajoule,
        (ulong)MicrojoulesPerGigajoule,
        (ulong)MicrojoulesPerTerajoule,
    ];

    private static readonly string[] HumanSymbols = ["uJ", "mJ", "J", "kJ", "MJ", "GJ", "TJ"];

    private readonly long _microjoules;

    /// <summary>
    /// Initializes a new instance of the <see cref="Energy"/> struct from an exact number of microjoules.
    /// </summary>
    /// <param name="microjoules">The signed number of microjoules.</param>
    public Energy(long microjoules)
    {
        _microjoules = microjoules;
    }

    /// <summary>
    /// Gets the exact number of microjoules.
    /// </summary>
    public long Microjoules => _microjoules;

    /// <summary>Creates an energy from an exact number of microjoules.</summary>
    /// <param name="microjoules">The number of microjoules.</param>
    /// <returns>The energy.</returns>
    public static Energy FromMicrojoules(long microjoules) => new(microjoules);

    /// <summary>Creates an energy from a number of millijoules.</summary>
    /// <param name="millijoules">The number of millijoules.</param>
    /// <returns>The energy.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Energy FromMillijoules(long millijoules) => new(checked(millijoules * MicrojoulesPerMillijoule));

    /// <summary>Creates an energy from a number of joules.</summary>
    /// <param name="joules">The number of joules.</param>
    /// <returns>The energy.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Energy FromJoules(long joules) => new(checked(joules * MicrojoulesPerJoule));

    /// <summary>Creates an energy from a number of kilojoules.</summary>
    /// <param name="kilojoules">The number of kilojoules.</param>
    /// <returns>The energy.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Energy FromKilojoules(long kilojoules) => new(checked(kilojoules * MicrojoulesPerKilojoule));

    /// <summary>Creates an energy from a number of megajoules.</summary>
    /// <param name="megajoules">The number of megajoules.</param>
    /// <returns>The energy.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Energy FromMegajoules(long megajoules) => new(checked(megajoules * MicrojoulesPerMegajoule));

    /// <summary>Creates an energy from a number of gigajoules.</summary>
    /// <param name="gigajoules">The number of gigajoules.</param>
    /// <returns>The energy.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Energy FromGigajoules(long gigajoules) => new(checked(gigajoules * MicrojoulesPerGigajoule));

    /// <summary>Creates an energy from a number of terajoules.</summary>
    /// <param name="terajoules">The number of terajoules.</param>
    /// <returns>The energy.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Energy FromTerajoules(long terajoules) => new(checked(terajoules * MicrojoulesPerTerajoule));

    /// <summary>
    /// Parses an invariant energy such as <c>1500</c>, <c>-1500 uJ</c>, <c>1.5 mJ</c>, <c>2 J</c>
    /// or <c>3 kJ</c>.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="energy">The parsed energy when parsing succeeds.</param>
    /// <returns><see langword="true"/> when parsing succeeds; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// A bare number and the <c>uJ</c>, <c>mJ</c>, <c>J</c>, <c>kJ</c>, <c>MJ</c>, <c>GJ</c> and
    /// <c>TJ</c> suffixes are accepted. A value must resolve to a whole number of microjoules.
    /// </remarks>
    public static bool TryParse(ReadOnlySpan<char> text, out Energy energy)
    {
        energy = default;
        if (!QuantityParser.TrySplit(text, out var number, out var suffix)
            || !TryGetMultiplier(suffix, out var multiplier)
            || !QuantityParser.TryScale(number, multiplier, out var microjoules))
        {
            return false;
        }

        energy = new Energy(microjoules);
        return true;
    }

    /// <summary>
    /// Parses an invariant energy.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The parsed energy, or a <see cref="CoreError"/> when parsing fails.</returns>
    public static Result<Energy> Parse(ReadOnlySpan<char> text)
    {
        return TryParse(text, out var energy)
            ? Result<Energy>.Success(energy)
            : Result<Energy>.Failure(new CoreError("units.invalid", $"Invalid energy: '{text.ToString()}'."));
    }

    /// <summary>
    /// Returns the exact energy in the invariant round-trippable form <c>"{microjoules} uJ"</c>.
    /// </summary>
    /// <returns>The exact energy.</returns>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{_microjoules} uJ");
    }

    /// <summary>
    /// Returns a compact representation such as <c>1.5 J</c> or <c>-12 mJ</c>.
    /// </summary>
    /// <returns>The human-readable energy.</returns>
    public string ToHumanString()
    {
        return QuantityFormatter.ToHumanString(_microjoules, HumanMultipliers, HumanSymbols);
    }

    /// <summary>
    /// Determines whether this instance equals another instance.
    /// </summary>
    /// <param name="other">The other instance.</param>
    /// <returns><see langword="true"/> when both hold the same number of microjoules; otherwise <see langword="false"/>.</returns>
    public bool Equals(Energy other)
    {
        return _microjoules == other._microjoules;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Energy other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return _microjoules.GetHashCode();
    }

    /// <summary>
    /// Compares this instance with another instance.
    /// </summary>
    /// <param name="other">The other instance.</param>
    /// <returns>The comparison result.</returns>
    public int CompareTo(Energy other)
    {
        return _microjoules.CompareTo(other._microjoules);
    }

    /// <summary>Adds two energies.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Energy operator +(Energy left, Energy right) => new(checked(left._microjoules + right._microjoules));

    /// <summary>Subtracts two energies.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Energy operator -(Energy left, Energy right) => new(checked(left._microjoules - right._microjoules));

    /// <summary>Negates an energy.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The negated energy.</returns>
    /// <exception cref="OverflowException">The result does not fit in a signed 64-bit integer.</exception>
    public static Energy operator -(Energy value) => new(checked(-value._microjoules));

    /// <summary>Compares two energies for equality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Energy left, Energy right) => left.Equals(right);

    /// <summary>Compares two energies for inequality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Energy left, Energy right) => !left.Equals(right);

    /// <summary>Determines whether the left energy is smaller than the right energy.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when left is smaller; otherwise <see langword="false"/>.</returns>
    public static bool operator <(Energy left, Energy right) => left._microjoules < right._microjoules;

    /// <summary>Determines whether the left energy is greater than the right energy.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when left is greater; otherwise <see langword="false"/>.</returns>
    public static bool operator >(Energy left, Energy right) => left._microjoules > right._microjoules;

    /// <summary>Determines whether the left energy is smaller than or equal to the right energy.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when left is smaller or equal; otherwise <see langword="false"/>.</returns>
    public static bool operator <=(Energy left, Energy right) => left._microjoules <= right._microjoules;

    /// <summary>Determines whether the left energy is greater than or equal to the right energy.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when left is greater or equal; otherwise <see langword="false"/>.</returns>
    public static bool operator >=(Energy left, Energy right) => left._microjoules >= right._microjoules;

    private static bool TryGetMultiplier(ReadOnlySpan<char> suffix, out long multiplier)
    {
        multiplier = 1;
        if (suffix.IsEmpty
            || suffix.SequenceEqual("uJ")
            || suffix.SequenceEqual("µJ")
            || suffix.SequenceEqual("μJ"))
        {
            return true;
        }

        if (suffix.SequenceEqual("mJ"))
        {
            multiplier = MicrojoulesPerMillijoule;
            return true;
        }

        if (suffix.SequenceEqual("J"))
        {
            multiplier = MicrojoulesPerJoule;
            return true;
        }

        if (suffix.SequenceEqual("kJ"))
        {
            multiplier = MicrojoulesPerKilojoule;
            return true;
        }

        if (suffix.SequenceEqual("MJ"))
        {
            multiplier = MicrojoulesPerMegajoule;
            return true;
        }

        if (suffix.SequenceEqual("GJ"))
        {
            multiplier = MicrojoulesPerGigajoule;
            return true;
        }

        if (suffix.SequenceEqual("TJ"))
        {
            multiplier = MicrojoulesPerTerajoule;
            return true;
        }

        return false;
    }
}
