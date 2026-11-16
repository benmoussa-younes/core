// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.Versioning;
using Core.Diagnostics;
using Core.Execution;
using Core.Notifications;

namespace Core.Platform.Linux;

/// <summary>
/// The GNOME desktop notification over <c>notify-send</c>: no D-Bus binding is
/// needed, so the Native AOT build stays dependency-free. The title and body are
/// placed after <c>--</c>, so a value that starts with a dash is an operand, not
/// an option, and every value is reduced to one bounded line.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class NotifySendNotifier : ISystemNotifier
{
    private const string Executable = "notify-send";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly IProcessRunner _runner;
    private readonly string _applicationName;

    /// <summary>Initializes the notifier.</summary>
    /// <param name="runner">The process seam.</param>
    /// <param name="applicationName">The name shown by the notification daemon.</param>
    /// <exception cref="ArgumentNullException"><paramref name="runner"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="applicationName"/> is empty or whitespace.</exception>
    public NotifySendNotifier(IProcessRunner runner, string applicationName = "Core")
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        _runner = runner;
        _applicationName = applicationName;
    }

    /// <inheritdoc />
    public async ValueTask NotifyAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            // A notification is best effort: a start failure is as uninteresting
            // as a non-zero exit, and the caller has no recovery to offer.
            _ = await _runner
                .RunAsync(
                    new ProcessRequest(
                        Executable,
                        [
                            "--app-name",
                            LogRedaction.SingleLine(_applicationName, 128),
                            "--",
                            LogRedaction.SingleLine(title, 256),
                            LogRedaction.SingleLine(body, 4_096),
                        ],
                        Timeout: Timeout),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A notification is best effort; the operation that raised it already
            // succeeded (or failed for its own reason).
        }
    }
}
