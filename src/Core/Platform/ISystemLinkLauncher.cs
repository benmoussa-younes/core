// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;

namespace Core.Platform;

/// <summary>
/// Hands an absolute <c>http</c>, <c>https</c> or <c>mailto</c> URL to the
/// desktop's handler. The launcher never opens a socket and never invokes a
/// shell; the desktop decides what the URL means.
/// </summary>
public interface ISystemLinkLauncher
{
    /// <summary>Opens a URL with the desktop handler.</summary>
    /// <param name="url">The absolute URL.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>
    /// Success, or a failure with code <c>link.invalid</c> or <c>link.launch_failed</c>.
    /// A successful value is <see langword="true"/> when the desktop handler
    /// process started and exited successfully; it does not prove that the target
    /// application handled the URL, only that the desktop accepted it.
    /// </returns>
    ValueTask<Result<bool>> OpenAsync(string url, CancellationToken cancellationToken = default);
}
