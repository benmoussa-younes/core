// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Platform;

namespace Core.UI.Hosting;

/// <summary>
/// The explicit composition-root options: the application identity, the state
/// retention and the platform composition used to resolve the base directories.
/// </summary>
/// <param name="Vendor">The vendor path component, for example <c>core</c>.</param>
/// <param name="Application">The application path component, for example <c>documents</c>.</param>
/// <param name="SingleInstance">Whether the host acquires the per-user instance lock.</param>
/// <param name="LogMaxBytes">The size at which the application log rotates.</param>
/// <param name="RetainedFiles">How many rotated log and journal archives to keep.</param>
/// <param name="CrashMaximumLines">How many stack lines a crash entry keeps.</param>
/// <param name="Platform">The platform composition; required, so the host never picks an implicit Linux implementation.</param>
/// <param name="IncludeUserInSingleInstanceName">
/// Whether the single-instance socket name carries the user. The default keeps the
/// released <c>&lt;vendor&gt;-&lt;application&gt;-&lt;user&gt;.sock</c>; <see langword="false"/>
/// uses <c>&lt;vendor&gt;-&lt;application&gt;.socket</c> and reads no user name, which is
/// correct only when <c>XDG_RUNTIME_DIR</c> is per-user as XDG requires.
/// </param>
public sealed record CoreHostOptions(
    string Vendor,
    string Application,
    bool SingleInstance = true,
    long LogMaxBytes = 1_048_576,
    int RetainedFiles = 5,
    int CrashMaximumLines = 200,
    CorePlatform? Platform = null,
    bool IncludeUserInSingleInstanceName = true);
