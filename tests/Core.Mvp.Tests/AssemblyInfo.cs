// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.Versioning;

// The suite runs on the Linux host; the atomic writer factory is the Linux
// adapter and the settings files use POSIX modes.
[assembly: SupportedOSPlatform("linux")]
