// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Serialization.Codecs.Json;

/// <summary>
/// How a <see cref="Values.DataKind.Number"/> cell (a <see cref="double"/>) is
/// rendered into JSON. The default (<see cref="Default"/>) keeps the historical
/// BCL behaviour — <c>Utf8JsonWriter.WriteNumberValue(double)</c> — so existing
/// consumers are unaffected; a caller that needs a canonical text (shortest
/// round-trip digits, a trailing <c>.0</c> for integral values, an explicit
/// exponent form) supplies its own renderer, which must return a valid JSON
/// number for any finite input.
/// </summary>
/// <remarks>
/// The renderer is only consulted for <see cref="Values.DataKind.Number"/> cells
/// (and non-finite values are still refused by the writer). Integers, decimals,
/// dates, times and timestamps are untouched. The <see cref="double"/> overload
/// of the delegate keeps the hot path allocation-free when the renderer is a
/// static method.
/// </remarks>
public readonly struct JsonNumberFormat
{
    private readonly Func<double, string>? _renderer;

    private JsonNumberFormat(Func<double, string>? renderer) => _renderer = renderer;

    /// <summary>The BCL rendering (unchanged legacy behaviour).</summary>
    public static JsonNumberFormat Default => default;

    /// <summary>Whether a custom renderer is installed.</summary>
    public bool HasCustomRenderer => _renderer is not null;

    /// <summary>Creates a format that renders every number cell with <paramref name="renderer"/>.</summary>
    /// <param name="renderer">The renderer; must not be <see langword="null"/> and must
    /// return a valid JSON number for any finite input.</param>
    /// <returns>The format.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
    public static JsonNumberFormat From(Func<double, string> renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        return new JsonNumberFormat(renderer);
    }

    /// <summary>Renders a number cell, or <see langword="null"/> when the caller must fall back to the BCL.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The JSON text, or <see langword="null"/> for the default format.</returns>
    internal string? Render(double value) => _renderer?.Invoke(value);
}
