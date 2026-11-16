// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;

namespace Core.Execution;

/// <summary>
/// Starts a process and captures its streams. This is the only seam between the
/// application and the operating system's process API; tests substitute a fake,
/// which is how the privileged paths are exercised without privileges.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Runs one process to completion.</summary>
    /// <param name="request">The executable, its arguments and its optional input.</param>
    /// <param name="cancellationToken">Cancels the wait; the process is killed.</param>
    /// <returns>
    /// A successful result holding the completed run, or a failure with code
    /// <c>execution.not_found</c> or <c>execution.start_failed</c>.
    /// </returns>
    Task<Result<ProcessResult>> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default);
}
