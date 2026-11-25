// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Platform.Linux;

/// <summary>
/// How a child process ended.
/// </summary>
/// <param name="Exited">Whether the child exited normally instead of dying from a signal.</param>
/// <param name="ExitCode">The exit code, or 128 plus the signal number when the child was signaled.</param>
/// <param name="Signal">The signal number when the child was signaled; 0 otherwise.</param>
public readonly record struct UnixExitStatus(bool Exited, int ExitCode, int Signal);
