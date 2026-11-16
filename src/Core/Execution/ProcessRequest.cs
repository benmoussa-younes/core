// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Execution;

/// <summary>
/// One process invocation: an executable and its already-split arguments. The
/// runner never builds a command line string, so no shell is involved and no
/// argument can be re-parsed.
/// </summary>
/// <param name="FileName">
/// The executable. A bare name is resolved against the trusted system
/// directories, never against <c>PATH</c>; a name that contains a separator is
/// used as given.
/// </param>
/// <param name="Arguments">The arguments, passed as a vector.</param>
/// <param name="StandardInput">Text to write to the process standard input, or <see langword="null"/>.</param>
/// <param name="Environment">Extra environment variables for the child, or <see langword="null"/>.</param>
/// <param name="Timeout">The maximum run time, or <see langword="null"/> for the runner's default.</param>
/// <param name="MaxOutputBytes">
/// The maximum bytes captured from each of standard output and standard error;
/// the rest is drained and reported as truncated.
/// </param>
public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? StandardInput = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    TimeSpan? Timeout = null,
    int MaxOutputBytes = 1_048_576)
{
    /// <summary>
    /// Describes the request without its arguments, standard input or environment
    /// values: those may carry a credential, and a record's default textual form
    /// is the one a log line would print.
    /// </summary>
    /// <returns>A redacted description.</returns>
    public override string ToString() =>
        $"ProcessRequest {{ FileName = {FileName}, ArgumentCount = {Arguments.Count}, " +
        $"StandardInput = {(StandardInput is null ? "null" : "[redacted]")}, " +
        $"EnvironmentCount = {Environment?.Count ?? 0}, Timeout = {Timeout}, " +
        $"MaxOutputBytes = {MaxOutputBytes} }}";
}
