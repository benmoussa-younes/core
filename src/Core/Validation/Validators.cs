// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;

namespace Core.Validation;

/// <summary>
/// The small validators every form needs: a non-blank value, a bounded length and
/// a TCP/UDP port. A product-specific rule (a tunnel name, a network prefix) stays
/// in the product; these are the shapes it composes with.
/// </summary>
public static class Validators
{
    /// <summary>Requires a non-blank value.</summary>
    /// <param name="value">The value.</param>
    /// <param name="field">The field name used in the failure message.</param>
    /// <returns>The value, or a failure with code <c>validation.required</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is <see langword="null"/>.</exception>
    public static Result<string> NotBlank(string? value, string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return string.IsNullOrWhiteSpace(value)
            ? Result<string>.Failure(new CoreError(
                CoreErrorCodes.ValidationRequired,
                $"'{field}' is required."))
            : Result<string>.Success(value);
    }

    /// <summary>Requires a non-blank value no longer than a maximum.</summary>
    /// <param name="value">The value.</param>
    /// <param name="maximumLength">The maximum length; must be positive.</param>
    /// <param name="field">The field name used in the failure message.</param>
    /// <returns>The value, or a failure with code <c>validation.required</c> or <c>validation.too_long</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumLength"/> is not positive.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is <see langword="null"/>.</exception>
    public static Result<string> MaxLength(string? value, int maximumLength, string field)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 1);
        ArgumentNullException.ThrowIfNull(field);

        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<string>.Failure(new CoreError(
                CoreErrorCodes.ValidationRequired,
                $"'{field}' is required."));
        }

        return value.Length > maximumLength
            ? Result<string>.Failure(new CoreError(
                CoreErrorCodes.ValidationTooLong,
                $"'{field}' is longer than {maximumLength} characters."))
            : Result<string>.Success(value);
    }

    /// <summary>Requires a valid TCP or UDP port.</summary>
    /// <param name="value">The port number.</param>
    /// <returns>The port, or a failure with code <c>validation.port</c>.</returns>
    public static Result<int> Port(int value)
    {
        return value is >= 1 and <= 65_535
            ? Result<int>.Success(value)
            : Result<int>.Failure(new CoreError(
                CoreErrorCodes.ValidationPort,
                $"'{value}' is not a port between 1 and 65535."));
    }
}
