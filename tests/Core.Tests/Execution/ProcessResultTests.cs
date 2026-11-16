// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Execution;

namespace Core.Tests;

public sealed class ProcessResultTests
{
    [Fact]
    public void A_request_carries_its_defaults_and_can_be_changed_with()
    {
        var request = new ProcessRequest("tool", ["a", "b"]);

        Assert.Equal("tool", request.FileName);
        Assert.Equal(["a", "b"], request.Arguments);
        Assert.Null(request.StandardInput);
        Assert.Null(request.Environment);
        Assert.Null(request.Timeout);
        Assert.Equal(1_048_576, request.MaxOutputBytes);

        var changed = request with
        {
            StandardInput = "in",
            Environment = new Dictionary<string, string> { ["K"] = "V" },
            Timeout = TimeSpan.FromSeconds(1),
            MaxOutputBytes = 2,
        };

        Assert.NotEqual(request, changed);
        Assert.Equal("in", changed.StandardInput);
        Assert.Equal("V", changed.Environment!["K"]);
        Assert.Equal(TimeSpan.FromSeconds(1), changed.Timeout);
        Assert.Equal(2, changed.MaxOutputBytes);

        var (fileName, arguments, input, environment, timeout, maximum) = changed;
        Assert.Equal("tool", fileName);
        Assert.Equal(["a", "b"], arguments);
        Assert.Equal("in", input);
        Assert.Equal("V", environment!["K"]);
        Assert.Equal(TimeSpan.FromSeconds(1), timeout);
        Assert.Equal(2, maximum);
        Assert.Equal(changed, changed with { });
        _ = changed.ToString();
        _ = changed.GetHashCode();
    }

    [Fact]
    public void ProcessRequest_ToString_does_not_leak_arguments_input_or_environment()
    {
        var request = new ProcessRequest(
            "tool",
            ["--secret", "super-secret-argument"],
            StandardInput: "super-secret-input",
            Environment: new Dictionary<string, string> { ["TOKEN"] = "super-secret-environment" });

        var text = request.ToString();

        Assert.False(text.Contains("super-secret", StringComparison.Ordinal));
        Assert.Contains("ArgumentCount = 2", text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessResult_ToString_does_not_leak_captured_output()
    {
        var result = new ProcessResult(0, "super-secret-output", "super-secret-error");

        var text = result.ToString();

        Assert.False(text.Contains("super-secret", StringComparison.Ordinal));
        Assert.Contains("StandardOutputLength = 19", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Completed_carries_the_exit_code_streams_and_flags()
    {
        var result = new ProcessResult(2, "out", "err", TimedOut: true, OutputTruncated: true);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("out", result.StandardOutput);
        Assert.Equal("err", result.StandardError);
        Assert.True(result.TimedOut);
        Assert.True(result.OutputTruncated);
        Assert.Equal(result, result with { });
        Assert.NotEqual(result, result with { ExitCode = 3 });

        var (exitCode, output, error, timedOut, truncated) = result;
        Assert.Equal(2, exitCode);
        Assert.Equal("out", output);
        Assert.Equal("err", error);
        Assert.True(timedOut);
        Assert.True(truncated);
        _ = result.ToString();
        _ = result.GetHashCode();
    }
}
