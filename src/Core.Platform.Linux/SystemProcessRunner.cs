// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Text;
using Core.Execution;
using Core.Primitives;
using Core.Results;


namespace Core.Platform.Linux;

/// <summary>
/// The real process runner. It starts the executable directly with an argument
/// vector: <c>UseShellExecute</c> is false, no shell is spawned, and no
/// privilege-escalation program is ever invoked here.
/// </summary>
/// <remarks>
/// A bare executable name is resolved against a fixed list of trusted system
/// directories, never against the caller's <c>PATH</c>, so a writable directory
/// earlier in <c>PATH</c> cannot shadow <c>nmcli</c>, <c>wg</c> or
/// <c>notify-send</c> with a rogue binary. The output is captured up to
/// <see cref="ProcessRequest.MaxOutputBytes"/> and the remainder is drained, so a
/// chatty child cannot block on a full pipe or exhaust memory. A run that exceeds
/// <see cref="ProcessRequest.Timeout"/> is killed as a process tree.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class SystemProcessRunner : IProcessRunner
{
    /// <summary>The default run time when the request does not set one.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private static readonly string[] TrustedDirectories =
        ["/usr/bin", "/bin", "/usr/sbin", "/sbin", "/usr/local/bin"];

    private static readonly string[] ForbiddenEnvironment =
        ["LD_PRELOAD", "LD_LIBRARY_PATH", "LD_AUDIT", "BASH_ENV", "ENV", "GCONV_PATH"];

    /// <inheritdoc />
    public async Task<Result<ProcessResult>> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentNullException.ThrowIfNull(request.Arguments);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.MaxOutputBytes);

        if (request.FileName.Contains('\0') ||
            request.Arguments.Any(static argument => argument.Contains('\0')) ||
            request.Environment?.Any(static pair =>
                pair.Key.Contains('\0') ||
                pair.Value.Contains('\0') ||
                ForbiddenEnvironment.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)) == true)
        {
            throw new ArgumentException(
                "A process request cannot carry a NUL or a code-injection environment variable.",
                nameof(request));
        }

        var timeout = request.Timeout ?? DefaultTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        var executable = ResolveExecutable(request.FileName);
        if (executable is null)
        {
            return Result<ProcessResult>.Failure(new CoreError(
                CoreErrorCodes.ExecutionNotFound,
                $"'{PathRedaction.ForMessage(request.FileName)}' was not found as an executable in the trusted system directories."));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = request.StandardInput is not null,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is not null)
        {
            foreach (var (key, value) in request.Environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        var startFailure = Start(process, executable);

        if (startFailure is not null)
        {
            return Result<ProcessResult>.Failure(startFailure);
        }

        // Start draining immediately: a child that writes more than the pipe
        // buffer holds would otherwise block before we ever wait for it. The
        // reads are not tied to the caller token: a cancellation kills the child,
        // which closes the pipes, so the reads finish with the partial output.
        var standardOutput = ReadCappedAsync(process.StandardOutput.BaseStream, request.MaxOutputBytes);
        var standardError = ReadCappedAsync(process.StandardError.BaseStream, request.MaxOutputBytes);
        var timedOut = false;

        // One deadline covers the stdin write and the wait. A child that never
        // reads its input must not hold the caller past ProcessRequest.Timeout.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            if (request.StandardInput is not null)
            {
                try
                {
                    await process.StandardInput
                        .WriteAsync(request.StandardInput.AsMemory(), deadline.Token)
                        .ConfigureAwait(false);
                    await process.StandardInput.FlushAsync(deadline.Token).ConfigureAwait(false);
                    process.StandardInput.Close();
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    timedOut = true;
                }
                catch (OperationCanceledException)
                {
                    CancelProcess(process);
                    _ = await standardOutput.ConfigureAwait(false);
                    _ = await standardError.ConfigureAwait(false);
                    throw;
                }
                catch (IOException)
                {
                    // The child exited before reading its input; the exit code and
                    // the captured output still describe the run.
                }
            }

            if (!timedOut)
            {
                try
                {
                    await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    timedOut = true;
                }
                catch (OperationCanceledException)
                {
                    CancelProcess(process);
                    _ = await standardOutput.ConfigureAwait(false);
                    _ = await standardError.ConfigureAwait(false);
                    throw;
                }
            }

            if (timedOut)
            {
                CancelProcess(process);
            }
            else
            {
                // A daemonised grandchild can inherit the write end and survive
                // the parent; bound the drain so the caller is not held forever.
                try
                {
                    await Task.WhenAll(standardOutput, standardError)
                        .WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    TryClose(process.StandardOutput);
                    TryClose(process.StandardError);
                }
            }

            var output = await standardOutput.ConfigureAwait(false);
            var error = await standardError.ConfigureAwait(false);

            return Result<ProcessResult>.Success(new ProcessResult(
                process.HasExited ? process.ExitCode : -1,
                Encoding.UTF8.GetString(output.Bytes),
                Encoding.UTF8.GetString(error.Bytes),
                timedOut,
                output.Truncated || error.Truncated));
        }
        finally
        {
            // Closing our read ends unblocks a drain held open by a grandchild
            // that survived the process-tree kill; the partial output is kept.
            if (timedOut || cancellationToken.IsCancellationRequested)
            {
                TryClose(process.StandardOutput);
                TryClose(process.StandardError);
            }
        }
    }

    private static void CancelProcess(System.Diagnostics.Process process)
    {
        Kill(process);
        TryClose(process.StandardOutput);
        TryClose(process.StandardError);

        try
        {
            if (process.StartInfo.RedirectStandardInput)
            {
                process.StandardInput.Close();
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The child may already have closed its input.
        }
    }

    private static void TryClose(StreamReader reader)
    {
        try
        {
            reader.Close();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The pipe is already gone.
        }
    }

    private static string? ResolveExecutable(string fileName)
    {
        if (fileName.Contains('/'))
        {
            // A relative path resolves against the caller's CWD, outside the
            // trusted-directory policy; only an absolute explicit path is accepted.
            return Path.IsPathRooted(fileName) ? fileName : null;
        }

        foreach (var directory in TrustedDirectories)
        {
            var candidate = Path.Combine(directory, fileName);

            if (LinuxFileSystem.TryStat(candidate, out var status) &&
                status.Type == LinuxFileType.RegularFile &&
                LinuxFileSystem.TryAccess(candidate, LinuxAccessMode.Execute))
            {
                return candidate;
            }
        }

        return null;
    }

    [ExcludeFromCodeCoverage(Justification = "Process.Start returns false only when the OS starts no process resource; with UseShellExecute=false it either starts or throws, so that arm cannot be provoked deterministically.")]
    private static CoreError? Start(System.Diagnostics.Process process, string executable)
    {
        try
        {
            if (!process.Start())
            {
                return new CoreError(
                    CoreErrorCodes.ExecutionStartFailed,
                    $"'{PathRedaction.ForMessage(executable)}' could not be started.");
            }

            return null;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // The exception message can carry the absolute path; it stays out of
            // the code and the message.
            return new CoreError(
                CoreErrorCodes.ExecutionStartFailed,
                $"'{PathRedaction.ForMessage(executable)}' could not be started.");
        }
    }

    private static void Kill(System.Diagnostics.Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        // Reap the child so ExitCode is observable even after a timeout.
        _ = process.WaitForExit(5_000);
    }

    private static async Task<CapturedOutput> ReadCappedAsync(Stream stream, int maximumBytes)
    {
        var buffer = new byte[8_192];
        using var captured = new MemoryStream();
        var truncated = false;

        while (true)
        {
            int read;

            try
            {
                read = await stream.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                // CancelProcess closes our read end to unblock a grandchild that
                // still holds the write end; the bytes already captured are kept.
                break;
            }

            if (read == 0)
            {
                break;
            }

            if (captured.Length < maximumBytes)
            {
                var take = (int)Math.Min(read, maximumBytes - captured.Length);
                captured.Write(buffer, 0, take);
                truncated |= take < read;
            }
            else
            {
                truncated = true;
            }
        }

        return new CapturedOutput(captured.ToArray(), truncated);
    }

    private readonly record struct CapturedOutput(byte[] Bytes, bool Truncated);
}
