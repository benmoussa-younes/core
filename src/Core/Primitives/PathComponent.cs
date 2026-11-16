// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;
using Core.Results;

namespace Core.Primitives;

/// <summary>
/// Validates one path component — a file or directory name — so combining it with
/// a directory can never escape that directory.
/// </summary>
/// <remarks>
/// A component is non-blank, is not <c>.</c> or <c>..</c>, contains no directory
/// separator, no control character and no leading or trailing whitespace, and is
/// at most <see cref="MaxBytes"/> UTF-8 bytes (Linux <c>NAME_MAX</c>). The rule is
/// shared by <c>ApplicationPaths</c>, the serialization store and the
/// single-instance socket path, so they cannot drift apart.
/// </remarks>
public static class PathComponent
{
    /// <summary>The maximum length of one component in UTF-8 bytes (Linux <c>NAME_MAX</c>).</summary>
    public const int MaxBytes = 255;

    /// <summary>
    /// Validates a path component.
    /// </summary>
    /// <param name="value">The component to validate.</param>
    /// <returns>The component, or a failure with code <c>paths.invalid_component</c>.</returns>
    public static Result<string> Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value is "." or ".." ||
            value.Contains('/') ||
            value.Contains('\\') ||
            value != value.Trim() ||
            HasControlCharacter(value) ||
            Encoding.UTF8.GetByteCount(value) > MaxBytes)
        {
            return Result<string>.Failure(new CoreError(
                CoreErrorCodes.PathsInvalidComponent,
                $"'{Display(value)}' is not a valid path component."));
        }

        return Result<string>.Success(value);
    }

    /// <summary>
    /// Determines whether a value is a valid path component.
    /// </summary>
    /// <param name="value">The component to test.</param>
    /// <returns><see langword="true"/> when <see cref="Validate"/> would succeed.</returns>
    public static bool IsValid(string? value) => Validate(value).IsSuccess;

    private static bool HasControlCharacter(string value)
    {
        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }

    // A failure message may be logged: never echo a control character or a huge
    // value verbatim into it.
    private static string Display(string? value)
    {
        if (value is null)
        {
            return "null";
        }

        var builder = new StringBuilder(Math.Min(value.Length, 64));

        foreach (var character in value)
        {
            if (builder.Length == 64)
            {
                builder.Append('…');
                break;
            }

            builder.Append(char.IsControl(character) ? '?' : character);
        }

        return builder.ToString();
    }
}
