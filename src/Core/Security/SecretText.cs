// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Security;

/// <summary>
/// A text value that must not be displayed or logged: <see cref="ToString"/> and
/// string interpolation are redacted, and <see cref="Reveal"/> is the single exit.
/// </summary>
/// <remarks>
/// A .NET <see cref="string"/> cannot be reliably zeroized (the runtime may copy
/// it), so this type documents that limit instead of pretending to erase it: keep
/// the raw value only as long as the operation needs it and never log it.
/// Equality is fixed-time for equal-length values, so a comparison does not leak
/// where the first differing character is.
/// </remarks>
public sealed class SecretText : IEquatable<SecretText>
{
    private readonly string _value;

    private SecretText(string value) => _value = value;

    /// <summary>Gets the empty secret.</summary>
    public static SecretText Empty { get; } = new(string.Empty);

    /// <summary>Wraps a value; a null value is the empty secret.</summary>
    /// <param name="value">The secret value, or <see langword="null"/>.</param>
    /// <returns>The wrapper.</returns>
    public static SecretText From(string? value) => value is null ? Empty : new SecretText(value);

    /// <summary>Gets a value indicating whether the secret is empty.</summary>
    public bool IsEmpty => _value.Length == 0;

    /// <summary>
    /// Returns the raw secret. This is the only way to read it; a caller must not
    /// write it to a log, a notification or a view.
    /// </summary>
    /// <returns>The raw value.</returns>
    public string Reveal() => _value;

    /// <inheritdoc />
    public bool Equals(SecretText? other) =>
        other is not null && FixedTimeEquals(_value, other._value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SecretText);

    /// <inheritdoc />
    /// <remarks>The hash is derived from the raw value; it is not a confidentiality boundary.</remarks>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value);

    /// <summary>Always redacted: a secret never appears in a log line or a view.</summary>
    /// <returns><c>[redacted]</c>, or <c>(none)</c> when empty.</returns>
    public override string ToString() => IsEmpty ? "(none)" : "[redacted]";

    private static bool FixedTimeEquals(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        var difference = 0;

        for (var index = 0; index < left.Length; index++)
        {
            difference |= left[index] ^ right[index];
        }

        return difference == 0;
    }
}
