// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.Versioning;

// The suite runs on the Linux host only; the Unix file-mode assertions in
// JsonSettingsStoreTests need the platform contract to be explicit.
[assembly: SupportedOSPlatform("linux")]
