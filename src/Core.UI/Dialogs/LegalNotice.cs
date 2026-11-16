// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.UI.Dialogs;

/// <summary>
/// The standard LGPL-3.0-or-later legal notice an interactive product must show, so
/// the wording lives in one place instead of being retyped per About dialog and
/// per version banner.
/// </summary>
public static class LegalNotice
{
    /// <summary>The licence label shown on the About dialog's link.</summary>
    public const string LicenseName = "GNU LGPL v3.0 or later";

    /// <summary>The licence URL the About dialog links to.</summary>
    public static Uri LicenseUrl { get; } = new("https://www.gnu.org/licenses/lgpl-3.0.html");

    /// <summary>Builds the copyright line.</summary>
    /// <param name="year">The copyright year.</param>
    /// <param name="holder">The copyright holder.</param>
    /// <param name="email">The holder's contact address.</param>
    /// <returns>The copyright line.</returns>
    /// <exception cref="ArgumentNullException">A string argument is <see langword="null"/>.</exception>
    public static string Copyright(int year, string holder, string email)
    {
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(email);

        return $"Copyright (C) {year} {holder} <{email}>";
    }

    /// <summary>Builds the full copyright and LGPL-3.0-or-later notice.</summary>
    /// <remarks>
    /// Kept under its original name for the 1.0 API; the text is the LGPL one the
    /// library has been relicensed to. <see cref="Lgpl"/> is the name a new caller
    /// should use.
    /// </remarks>
    /// <param name="year">The copyright year.</param>
    /// <param name="holder">The copyright holder.</param>
    /// <param name="email">The holder's contact address.</param>
    /// <returns>The notice, one paragraph per blank line.</returns>
    /// <exception cref="ArgumentNullException">A string argument is <see langword="null"/>.</exception>
    public static string Gpl(int year, string holder, string email) => Lgpl(year, holder, email);

    /// <summary>Builds the full copyright and LGPL-3.0-or-later notice.</summary>
    /// <param name="year">The copyright year.</param>
    /// <param name="holder">The copyright holder.</param>
    /// <param name="email">The holder's contact address.</param>
    /// <returns>The notice, one paragraph per blank line.</returns>
    /// <exception cref="ArgumentNullException">A string argument is <see langword="null"/>.</exception>
    public static string Lgpl(int year, string holder, string email)
    {
        var separator = string.Concat(Environment.NewLine, Environment.NewLine);

        return string.Join(
            separator,
            Copyright(year, holder, email),
            "This library is free software: you can redistribute it and/or modify it under the terms " +
            "of the GNU Lesser General Public License as published by the Free Software Foundation, " +
            "either version 3 of the License, or (at your option) any later version.",
            "This library is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; " +
            "without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. " +
            "See the GNU Lesser General Public License for more details.");
    }
}
