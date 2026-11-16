// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.Versioning;
using Core.Diagnostics;
using Core.Execution;
using Core.Results;

namespace Core.Platform.Linux;

/// <summary>
/// The <see cref="ISystemLinkLauncher"/> over <c>xdg-open</c>: the URL is one
/// argument of a trusted process, never a shell word, and only an absolute
/// <c>http</c>, <c>https</c> or <c>mailto</c> URI is accepted.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class SystemLinkLauncher : ISystemLinkLauncher
{
    private static readonly string[] AllowedSchemes = ["http", "https", "mailto"];
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly IProcessRunner _runner;

    /// <summary>Initializes the launcher over a process seam.</summary>
    /// <param name="runner">The process runner.</param>
    /// <exception cref="ArgumentNullException"><paramref name="runner"/> is <see langword="null"/>.</exception>
    public SystemLinkLauncher(IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);

        _runner = runner;
    }

    /// <inheritdoc />
    public async ValueTask<Result<bool>> OpenAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!IsAllowed(url, out var reason))
        {
            return Result<bool>.Failure(new CoreError(
                CoreErrorCodes.LinkInvalid,
                $"The URL is not an allowed http, https or mailto URI: {reason}."));
        }

        var run = await _runner
            .RunAsync(new ProcessRequest("xdg-open", [url], Timeout: Timeout), cancellationToken)
            .ConfigureAwait(false);

        if (run.IsFailure)
        {
            return Result<bool>.Failure(new CoreError(
                CoreErrorCodes.LinkLaunchFailed,
                $"The desktop could not open the URL ({run.Error.Code})."));
        }

        var result = run.Value;

        return !result.TimedOut && result.ExitCode == 0
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(new CoreError(
                CoreErrorCodes.LinkLaunchFailed,
                "The desktop could not open the URL."));
    }

    private static bool IsAllowed(string? url, out string reason)
    {
        reason = "the value is empty";

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (url.Length > 4_096)
        {
            reason = "the value is too long";
            return false;
        }

        if (url[0] == '-')
        {
            reason = "a URL cannot start with a dash";
            return false;
        }

        foreach (var character in url)
        {
            if (char.IsControl(character))
            {
                reason = "a URL cannot contain a control character";
                return false;
            }
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            reason = "the value is not an absolute URI";
            return false;
        }

        foreach (var scheme in AllowedSchemes)
        {
            if (string.Equals(uri.Scheme, scheme, StringComparison.OrdinalIgnoreCase))
            {
                reason = string.Empty;
                return true;
            }
        }

        reason = string.Concat("'", LogRedaction.SingleLine(uri.Scheme, 32), "' is not an allowed scheme");
        return false;
    }
}
