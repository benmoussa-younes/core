// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Execution;

/// <summary>
/// The result of a process the operating system started and that ran to
/// completion, including the cases where it was stopped at its time limit or its
/// output was capped. A process that could not be started is a
/// <see cref="Core.Results.Result{T}"/> failure instead, so an expected
/// start failure is not encoded as a sentinel value.
/// </summary>
/// <param name="ExitCode">The exit code.</param>
/// <param name="StandardOutput">The captured standard output.</param>
/// <param name="StandardError">The captured standard error.</param>
/// <param name="TimedOut">Whether the run was stopped at its time limit.</param>
/// <param name="OutputTruncated">Whether standard output or standard error exceeded the capture cap.</param>
public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    bool OutputTruncated = false)
{
    /// <summary>
    /// Describes the result without the captured streams: a tool may print a
    /// credential, and a record's default textual form is the one a log line
    /// would print.
    /// </summary>
    /// <returns>A redacted description.</returns>
    public override string ToString() =>
        $"ProcessResult {{ ExitCode = {ExitCode}, " +
        $"StandardOutputLength = {StandardOutput.Length}, " +
        $"StandardErrorLength = {StandardError.Length}, " +
        $"TimedOut = {TimedOut}, OutputTruncated = {OutputTruncated} }}";
}
