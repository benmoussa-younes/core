// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Execution;
using Core.Platform.Linux;
using Core.Results;

namespace Core.IO.Tests;

public sealed class SystemProcessRunnerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), string.Concat("core-process-", Guid.NewGuid().ToString("N")));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void The_default_timeout_is_thirty_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), SystemProcessRunner.DefaultTimeout);
    }

    [Fact]
    public async Task It_captures_stdout_and_the_exit_code()
    {
        var result = (await new SystemProcessRunner()
            .RunAsync(new ProcessRequest("printf", ["%s", "hello"]), Ct)).Value;

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello", result.StandardOutput);
        Assert.Empty(result.StandardError);
        Assert.False(result.TimedOut);
        Assert.False(result.OutputTruncated);
    }

    [Fact]
    public async Task It_passes_arguments_as_a_vector_without_a_shell()
    {
        var result = (await new SystemProcessRunner()
            .RunAsync(new ProcessRequest("printf", ["%s|%s", "a b", "$(id)"]), Ct)).Value;

        Assert.Equal("a b|$(id)", result.StandardOutput);
    }

    [Fact]
    public async Task It_captures_standard_error_and_a_non_zero_exit()
    {
        var result = (await new SystemProcessRunner()
            .RunAsync(new ProcessRequest("ls", ["/definitely-not-a-core-path"]), Ct)).Value;

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(string.IsNullOrEmpty(result.StandardError));
    }

    [Fact]
    public async Task It_writes_standard_input()
    {
        var result = (await new SystemProcessRunner()
            .RunAsync(new ProcessRequest("cat", [], StandardInput: "hello"), Ct)).Value;

        Assert.Equal("hello", result.StandardOutput);
    }

    [Fact]
    public async Task A_child_that_exits_before_reading_its_input_is_not_a_failure()
    {
        // `true` exits immediately; the write into its closed pipe raises a
        // broken-pipe IOException, which must not replace the captured result.
        var result = (await new SystemProcessRunner()
            .RunAsync(new ProcessRequest("true", [], StandardInput: new string('x', 1_000_000)), Ct)).Value;

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task It_adds_environment_variables()
    {
        var environment = new Dictionary<string, string> { ["CORE_TEST_VALUE"] = "42" };

        var result = (await new SystemProcessRunner()
            .RunAsync(new ProcessRequest("printenv", ["CORE_TEST_VALUE"], Environment: environment), Ct)).Value;

        Assert.Equal("42\n", result.StandardOutput);
    }

    [Fact]
    public async Task A_bare_name_outside_the_trusted_directories_is_not_resolved()
    {
        WriteExecutable("tool", "#!/bin/sh\necho hi\n");

        var result = await new SystemProcessRunner()
            .RunAsync(new ProcessRequest("tool", []), Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(CoreErrorCodes.ExecutionNotFound, result.Error.Code);
        Assert.Contains("trusted", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_absolute_path_is_honoured()
    {
        var script = WriteExecutable("tool", "#!/bin/sh\necho hi\n");

        var result = (await new SystemProcessRunner()
            .RunAsync(new ProcessRequest(script, []), Ct)).Value;

        Assert.Equal("hi\n", result.StandardOutput);
    }

    [Fact]
    public async Task A_relative_explicit_path_is_not_resolved()
    {
        var result = await new SystemProcessRunner()
            .RunAsync(new ProcessRequest("./tool", []), Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(CoreErrorCodes.ExecutionNotFound, result.Error.Code);
    }

    [Fact]
    public async Task A_NUL_in_a_file_name_or_argument_is_rejected()
    {
        var runner = new SystemProcessRunner();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            runner.RunAsync(new ProcessRequest("printf\0--evil", []), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            runner.RunAsync(new ProcessRequest("printf", ["A\0B"]), Ct));
    }

    [Fact]
    public async Task A_forbidden_environment_variable_is_rejected()
    {
        var environment = new Dictionary<string, string> { ["LD_PRELOAD"] = "/tmp/evil.so" };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SystemProcessRunner().RunAsync(
                new ProcessRequest("printf", ["x"], Environment: environment),
                Ct));
    }

    [Fact]
    public async Task A_missing_bare_executable_is_not_found()
    {
        var result = await new SystemProcessRunner()
            .RunAsync(new ProcessRequest("definitely-not-a-core-tool", []), Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(CoreErrorCodes.ExecutionNotFound, result.Error.Code);
        Assert.Contains("not found", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_executable_that_cannot_start_is_reported_as_a_start_failure()
    {
        var result = await new SystemProcessRunner()
            .RunAsync(new ProcessRequest("/definitely/not/a/tool", []), Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(CoreErrorCodes.ExecutionStartFailed, result.Error.Code);
        Assert.False(string.IsNullOrEmpty(result.Error.Message));
    }

    [Fact]
    public async Task A_timeout_kills_the_process_and_reports_it()
    {
        var result = (await new SystemProcessRunner().RunAsync(
            new ProcessRequest("sleep", ["10"], Timeout: TimeSpan.FromMilliseconds(100)),
            Ct)).Value;

        Assert.True(result.TimedOut);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task A_timeout_covers_a_blocked_stdin_write()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var result = (await new SystemProcessRunner().RunAsync(
            new ProcessRequest(
                "sleep",
                ["10"],
                StandardInput: new string('x', 2_000_000),
                Timeout: TimeSpan.FromMilliseconds(200)),
            Ct)).Value;

        Assert.True(result.TimedOut);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"elapsed {watch.Elapsed}");
    }

    [Fact]
    public async Task A_grandchild_holding_the_output_pipe_does_not_hang_the_drain()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        // setsid detaches the grandchild from the shell's process group, so the
        // process-tree kill cannot reap it; it keeps the inherited stdout pipe.
        var result = (await new SystemProcessRunner().RunAsync(
            new ProcessRequest(
                "sh",
                ["-c", "(setsid sleep 2 &); sleep 10"],
                Timeout: TimeSpan.FromMilliseconds(100)),
            Ct)).Value;

        Assert.True(result.TimedOut);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"elapsed {watch.Elapsed}");
    }

    [Fact]
    public async Task An_output_flood_is_capped_and_drained()
    {
        var result = (await new SystemProcessRunner().RunAsync(
            new ProcessRequest(
                "yes",
                ["x"],
                Timeout: TimeSpan.FromMilliseconds(200),
                MaxOutputBytes: 1_024),
            Ct)).Value;

        Assert.True(result.TimedOut);
        Assert.True(result.OutputTruncated);
        Assert.Equal(1_024, result.StandardOutput.Length);
    }

    [Fact]
    public async Task A_cancelled_stdin_write_kills_the_process_and_is_observed()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SystemProcessRunner().RunAsync(
                new ProcessRequest("sleep", ["10"], StandardInput: new string('x', 1_000_000)),
                cancellation.Token));
    }

    [Fact]
    public async Task A_caller_cancellation_kills_the_process_and_is_observed()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SystemProcessRunner().RunAsync(
                new ProcessRequest("sleep", ["10"]),
                cancellation.Token));
    }

    [Fact]
    public async Task It_rejects_invalid_requests()
    {
        var runner = new SystemProcessRunner();

        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.RunAsync(null!, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(new ProcessRequest(" ", []), Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.RunAsync(new ProcessRequest("printf", null!), Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            runner.RunAsync(new ProcessRequest("printf", [], MaxOutputBytes: 0), Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            runner.RunAsync(new ProcessRequest("printf", [], Timeout: TimeSpan.Zero), Ct));
    }

    private string WriteExecutable(string name, string script)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, script);
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
