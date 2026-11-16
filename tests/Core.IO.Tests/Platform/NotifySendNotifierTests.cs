// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Execution;
using Core.Platform.Linux;
using Core.Results;

namespace Core.IO.Tests;

public sealed class NotifySendNotifierTests
{
    [Fact]
    public async Task It_sends_the_application_title_and_body_after_the_option_terminator()
    {
        var runner = new FakeRunner();

        await new NotifySendNotifier(runner, "Core Test")
            .NotifyAsync("Title", "Body", TestContext.Current.CancellationToken);

        var request = Assert.Single(runner.Requests);
        Assert.Equal("notify-send", request.FileName);
        Assert.Equal(["--app-name", "Core Test", "--", "Title", "Body"], request.Arguments);
        Assert.NotNull(request.Timeout);
    }

    [Fact]
    public async Task A_dash_led_title_is_an_operand_not_an_option()
    {
        var runner = new FakeRunner();

        await new NotifySendNotifier(runner)
            .NotifyAsync("--urgency=critical", "Body", TestContext.Current.CancellationToken);

        var request = Assert.Single(runner.Requests);
        Assert.Equal("--urgency=critical", request.Arguments[3]);
    }

    [Fact]
    public async Task Control_characters_are_reduced_to_one_line()
    {
        var runner = new FakeRunner();

        await new NotifySendNotifier(runner, "App\nName")
            .NotifyAsync("Title\nforged", "Body\r\nforged", TestContext.Current.CancellationToken);

        var request = Assert.Single(runner.Requests);
        Assert.Equal("App\\nName", request.Arguments[1]);
        Assert.Equal("Title\\nforged", request.Arguments[3]);
        Assert.Equal("Body\\r\\nforged", request.Arguments[4]);
    }

    [Fact]
    public async Task A_failed_or_missing_notification_is_swallowed()
    {
        await new NotifySendNotifier(new FakeRunner
        {
            Result = Result<ProcessResult>.Failure(
                new CoreError(CoreErrorCodes.ExecutionNotFound, "missing")),
        })
            .NotifyAsync("Title", "Body", TestContext.Current.CancellationToken);

        await new NotifySendNotifier(new FakeRunner { Failure = new IOException("broken pipe") })
            .NotifyAsync("Title", "Body", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task It_validates_its_arguments()
    {
        var runner = new FakeRunner();
        var notifier = new NotifySendNotifier(runner);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => notifier.NotifyAsync(null!, "Body", TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => notifier.NotifyAsync("Title", null!, TestContext.Current.CancellationToken).AsTask());

        Assert.Throws<ArgumentNullException>(() => new NotifySendNotifier(null!));
        Assert.Throws<ArgumentException>(() => new NotifySendNotifier(runner, string.Empty));
    }

    private sealed class FakeRunner : IProcessRunner
    {
        public Result<ProcessResult> Result { get; init; } =
            Result<ProcessResult>.Success(new ProcessResult(0, string.Empty, string.Empty));

        public Exception? Failure { get; init; }

        public List<ProcessRequest> Requests { get; } = [];

        public Task<Result<ProcessResult>> RunAsync(
            ProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return Failure is { } failure
                ? Task.FromException<Result<ProcessResult>>(failure)
                : Task.FromResult(Result);
        }
    }
}
