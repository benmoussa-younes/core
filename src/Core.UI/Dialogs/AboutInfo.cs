// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Media;

namespace Core.UI.Dialogs;

/// <summary>
/// The identity a product supplies to the shared <see cref="AboutDialog"/>: its
/// name, version, legal notice and the links to its licence and corresponding
/// source.
/// </summary>
/// <param name="ProductName">The product name.</param>
/// <param name="Version">The version string.</param>
/// <param name="Notice">The legal notice, usually from <see cref="LegalNotice.Lgpl"/>.</param>
/// <param name="LicenseName">The licence label; <see cref="LegalNotice.LicenseName"/> by default.</param>
/// <param name="LicenseUrl">The licence URL; <see cref="LegalNotice.LicenseUrl"/> by default.</param>
/// <param name="SourceUrl">The URL of the corresponding source.</param>
/// <param name="Icon">The optional product mark shown in the header.</param>
public sealed record AboutInfo(
    string ProductName,
    string Version,
    string Notice,
    Uri SourceUrl,
    string LicenseName = LegalNotice.LicenseName,
    Uri? LicenseUrl = null,
    IImage? Icon = null);
