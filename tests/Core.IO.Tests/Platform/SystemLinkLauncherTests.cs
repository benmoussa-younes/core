// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Execution;
using Core.Platform.Linux;
using Core.Results;

namespace Core.IO.Tests;

public sealed class SystemLinkLauncherTests
{
    [Theory]
    [InlineData("https://example.org")]
    [InlineData("http://example.org/path?q=1")]
    [InlineData("mailto:hello@example.org")]
    public async Task It_hands_an_allowed_url_to_xdg_open(string url)
    {
        var runner = new FakeRunner();

        var result = await new SystemLinkLauncher(runner).OpenAsync(url, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(runner.Requests);
        Assert.Equal("xdg-open", request.FileName);
        Assert.Equal([url], request.Arguments);
        Assert.NotNull(request.Timeout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("--help")]
    [InlineData("relative/path")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://example.org/\nforged")]
    public async Task It_refuses_anything_but_an_allowed_absolute_url(string? url)
    {
        var runner = new FakeRunner();

        var result = await new SystemLinkLauncher(runner).OpenAsync(url!, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("link.invalid", result.Error.Code);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task It_reports_a_process_that_does_not_start_fails_or_times_out()
    {
        var notStarted = new FakeRunner
        {
            Result = Result<ProcessResult>.Failure(
                new CoreError(CoreErrorCodes.ExecutionNotFound, "missing")),
        };
        Assert.Equal(
            "link.launch_failed",
            (await new SystemLinkLauncher(notStarted).OpenAsync("https://example.org", TestContext.Current.CancellationToken)).Error.Code);

        var failed = new FakeRunner { Result = Result<ProcessResult>.Success(new ProcessResult(1, string.Empty, "error")) };
        Assert.Equal(
            "link.launch_failed",
            (await new SystemLinkLauncher(failed).OpenAsync("https://example.org", TestContext.Current.CancellationToken)).Error.Code);

        var timedOut = new FakeRunner
        {
            Result = Result<ProcessResult>.Success(new ProcessResult(0, string.Empty, string.Empty, TimedOut: true)),
        };
        Assert.Equal(
            "link.launch_failed",
            (await new SystemLinkLauncher(timedOut).OpenAsync("https://example.org", TestContext.Current.CancellationToken)).Error.Code);
    }

    [Fact]
    public async Task It_refuses_a_url_over_the_length_limit()
    {
        var runner = new FakeRunner();
        var url = string.Concat("https://example.org/", new string('a', 4_096));

        var result = await new SystemLinkLauncher(runner).OpenAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal("link.invalid", result.Error.Code);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void It_validates_the_runner()
    {
        Assert.Throws<ArgumentNullException>(() => new SystemLinkLauncher(null!));
    }

    private sealed class FakeRunner : IProcessRunner
    {
        public Result<ProcessResult> Result { get; init; } =
            Result<ProcessResult>.Success(new ProcessResult(0, string.Empty, string.Empty));

        public List<ProcessRequest> Requests { get; } = [];

        public Task<Result<ProcessResult>> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Result);
        }
    }
}
