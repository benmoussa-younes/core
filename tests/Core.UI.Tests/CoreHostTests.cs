// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;
using Avalonia;
using Core.UI.Hosting;
using Core.Diagnostics;
using Core.Platform.Linux;
using Core.Paths;
using Core.Platform;
using Core.Results;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class CoreHostTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), string.Concat("bc-", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task It_creates_the_paths_diagnostics_and_single_start()
    {
        var host = CreateHost().Value;

        Assert.True(Directory.Exists(host.Paths.StateDirectory));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(host.Paths.StateDirectory));
        Assert.True(host.TryStart());
        Assert.True(host.TryStart());

        host.Logger.Log(LogLevel.Information, "host probe");
        Assert.Contains("host probe", File.ReadAllText(host.Paths.StateFile("application.log")), StringComparison.Ordinal);

        Assert.NotNull(host.Journal);
        Assert.NotNull(host.CrashLog);
        Assert.NotNull(host.Processes);
        Assert.NotNull(host.Links);
        Assert.NotNull(host.Notifier);
        Assert.Null(host.SingleInstance);
        Assert.False(host.Stopping.IsCancellationRequested);

        await host.DisposeAsync();
        await host.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => host.TryStart());
    }

    [Fact]
    public async Task Two_launches_of_the_same_application_share_one_instance()
    {
        var application = string.Concat("lock-", Guid.NewGuid().ToString("N")[..8]);
        var firstResult = CreateHost(application, singleInstance: true);
        Assert.True(firstResult.IsSuccess, firstResult.IsSuccess ? string.Empty : firstResult.Error.Message);
        var first = firstResult.Value;
        var secondResult = CreateHost(application, singleInstance: true);
        Assert.True(secondResult.IsSuccess, secondResult.IsSuccess ? string.Empty : secondResult.Error.Message);
        var second = secondResult.Value;

        Assert.True(first.TryStart());
        Assert.False(second.TryStart());
        Assert.NotNull(first.SingleInstance);

        await second.ShutdownAsync();
        await first.ShutdownAsync();
    }

    [Fact]
    public async Task A_second_launch_forwards_the_activation_and_payload()
    {
        var application = string.Concat("forward-", Guid.NewGuid().ToString("N")[..8]);
        var host = CreateHost(application, singleInstance: true).Value;
        Assert.True(host.TryStart());

        var requested = new TaskCompletionSource();
        var received = new TaskCompletionSource<byte[]>();
        host.ActivationRequested += () => requested.TrySetResult();
        host.Activated += bytes => received.TrySetResult(bytes);

        var payload = Encoding.UTF8.GetBytes("document.md");
        using var sender = new SingleInstanceLock(((SingleInstanceLock)host.SingleInstance!).SocketPath);
        Assert.False(sender.TryAcquire(payload));

        await received.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.False(requested.Task.IsCompleted);

        await host.ShutdownAsync();
    }

    [Fact]
    public async Task A_payload_without_a_subscriber_is_ignored()
    {
        var host = CreateHost().Value;

        host.OnActivated(Encoding.UTF8.GetBytes("document.md"));
        host.OnActivated(ReadOnlyMemory<byte>.Empty);

        await host.ShutdownAsync();
    }

    [Fact]
    public async Task A_payloadless_second_launch_requests_surface()
    {
        var application = string.Concat("surface-", Guid.NewGuid().ToString("N")[..8]);
        var host = CreateHost(application, singleInstance: true).Value;
        Assert.True(host.TryStart());

        var requested = new TaskCompletionSource();
        var received = new TaskCompletionSource<byte[]>();
        host.ActivationRequested += () => requested.TrySetResult();
        host.Activated += bytes => received.TrySetResult(bytes);

        using var sender = new SingleInstanceLock(((SingleInstanceLock)host.SingleInstance!).SocketPath);
        Assert.False(sender.TryAcquire());

        await requested.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.False(received.Task.IsCompleted);

        await host.ShutdownAsync();
    }

    [Fact]
    public async Task Attaching_the_crash_handler_writes_the_crash_log()
    {
        var host = CreateHost().Value;
        var crashPath = host.CrashLog.Path;

        await Headless.Dispatch(() =>
        {
            var application = Assert.IsAssignableFrom<CoreApplication>(Application.Current);
            var previous = application.UnhandledExceptionHandler;

            host.Attach(application);
            application.ReportUnhandledException(new InvalidOperationException("host probe"));

            application.UnhandledExceptionHandler = previous;
            host.ShutdownAsync().AsTask().GetAwaiter().GetResult();
            Assert.Throws<ObjectDisposedException>(() => host.Attach(application));
        });

        Assert.Contains("host probe", File.ReadAllText(crashPath), StringComparison.Ordinal);

        // The operational log is what an operator reads: the same failure must be
        // there, not only in the capped crash log.
        Assert.Contains(
            "host probe",
            File.ReadAllText(host.Paths.StateFile("application.log")),
            StringComparison.Ordinal);

        await host.ShutdownAsync();
    }

    [Fact]
    public async Task A_lock_start_failure_returns_false_and_is_logged()
    {
        var blocked = Path.Combine(_root, "r");
        Directory.CreateDirectory(_root);
        File.WriteAllText(blocked, "not a directory");

        var result = CoreHost.Create(new CoreHostOptions(
            "core",
            "lockfail",
            SingleInstance: true,
            Platform: Platform(new XdgEnvironment(
                _root,
                Path.Combine(_root, "config"),
                Path.Combine(_root, "state"),
                Path.Combine(_root, "data"),
                Path.Combine(_root, "cache"),
                blocked,
                null,
                null))));

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Error.Message);
        var host = result.Value;

        Assert.False(host.TryStart());
        Assert.Contains(
            "single-instance lock could not start",
            File.ReadAllText(host.Paths.StateFile("application.log")),
            StringComparison.Ordinal);
        await host.ShutdownAsync();
    }

    [Fact]
    public async Task A_preexisting_state_directory_keeps_its_mode()
    {
        var state = Path.Combine(_root, "state", "core", "host-test");
        Directory.CreateDirectory(state);
        File.SetUnixFileMode(
            state,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute);

        var host = CreateHost().Value;

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute,
            File.GetUnixFileMode(state));

        await host.ShutdownAsync();
    }

    [Fact]
    public void Invalid_options_are_typed_failures()
    {
        Assert.Equal(
            "host.platform_required",
            CoreHost.Create(new CoreHostOptions(
                "vendor",
                "app",
                Platform: new CorePlatform(new XdgPathResolver(Environment())))).Error.Code);

        Assert.Equal(
            "host.invalid_options",
            CoreHost.Create(new CoreHostOptions("vendor", "app", LogMaxBytes: 0, Platform: Platform())).Error.Code);

        Assert.Equal(
            "paths.invalid_component",
            CoreHost.Create(new CoreHostOptions("bad/vendor", "app", Platform: Platform())).Error.Code);

        Assert.Equal(
            "xdg.home_unset",
            CoreHost.Create(new CoreHostOptions(
                "vendor",
                "app",
                Platform: Platform(new XdgEnvironment(null, null, null, null, null, null, null, null)))).Error.Code);

        Assert.Equal(
            "host.socket_path_invalid",
            CoreHost.Create(new CoreHostOptions(
                "vendor",
                new string('a', 200),
                Platform: Platform())).Error.Code);

        Assert.Equal(
            "host.runtime_dir_unset",
            CoreHost.Create(new CoreHostOptions(
                "vendor",
                "app",
                SingleInstance: true,
                Platform: Platform(new XdgEnvironment(
                    _root,
                    Path.Combine(_root, "config"),
                    Path.Combine(_root, "state"),
                    Path.Combine(_root, "data"),
                    Path.Combine(_root, "cache"),
                    null,
                    null,
                    null)))).Error.Code);
    }

    [Fact]
    public void An_unwritable_state_directory_is_a_typed_failure()
    {
        var blocked = Path.Combine(_root, "blocked");
        Directory.CreateDirectory(_root);
        File.WriteAllText(blocked, "not a directory");

        var environment = new XdgEnvironment(
            _root,
            Path.Combine(_root, "config"),
            blocked,
            Path.Combine(_root, "data"),
            Path.Combine(_root, "cache"),
            null,
            null,
            null);

        var result = CoreHost.Create(new CoreHostOptions(
            "vendor",
            "app",
            SingleInstance: false,
            Platform: Platform(environment)));

        Assert.Equal("host.state_unwritable", result.Error.Code);
    }

    [Fact]
    public void A_group_or_world_writable_state_directory_is_refused()
    {
        var state = Path.Combine(_root, "state", "vendor", "app");
        Directory.CreateDirectory(state);
        File.SetUnixFileMode(
            state,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);

        var result = CoreHost.Create(new CoreHostOptions(
            "vendor",
            "app",
            SingleInstance: false,
            Platform: Platform()));

        Assert.Equal("host.state_unwritable", result.Error.Code);
    }

    [Fact]
    public async Task It_creates_one_window_and_shuts_it_down()
    {
        await Headless.Dispatch(() =>
        {
            var host = CreateHost().Value;
            var window = host.CreateWindow();

            Assert.Same(window, host.Window);
            Assert.Throws<InvalidOperationException>(() => host.CreateWindow());

            host.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task Two_vendors_sharing_an_application_name_get_distinct_socket_paths()
    {
        // ApplicationPaths isolates a product under <vendor>/<application>; the
        // single-instance socket must isolate it the same way.
        var first = CoreHost.Create(new CoreHostOptions(
            "acme",
            "tool",
            SingleInstance: true,
            Platform: Platform())).Value;
        var second = CoreHost.Create(new CoreHostOptions(
            "globex",
            "tool",
            SingleInstance: true,
            Platform: Platform())).Value;

        var firstPath = ((SingleInstanceLock)first.SingleInstance!).SocketPath;
        var secondPath = ((SingleInstanceLock)second.SingleInstance!).SocketPath;

        Assert.NotEqual(firstPath, secondPath);

        await first.ShutdownAsync();
        await second.ShutdownAsync();
    }

    [Fact]
    public async Task A_host_that_has_shut_down_ignores_a_late_activation()
    {
        var host = CreateHost(singleInstance: true).Value;
        await host.ShutdownAsync();

        var raised = false;
        host.Activated += _ => raised = true;

        host.OnActivated(new byte[] { 1 });

        Assert.False(raised);
    }

    [Fact]
    public async Task Shutdown_restores_the_application_crash_handler()
    {
        await Headless.Dispatch(async () =>
        {
            var application = Assert.IsAssignableFrom<CoreApplication>(Application.Current);
            var previous = application.UnhandledExceptionHandler;

            var host = CreateHost().Value;
            host.Attach(application);
            Assert.NotSame(previous, application.UnhandledExceptionHandler);

            await host.ShutdownAsync();

            Assert.Same(previous, application.UnhandledExceptionHandler);
        });
    }

    private Result<CoreHost> CreateHost(string application = "host-test", bool singleInstance = false) =>
        CoreHost.Create(new CoreHostOptions(
            "core",
            application,
            SingleInstance: singleInstance,
            Platform: Platform()));

    private CorePlatform Platform() => LinuxPlatform.Create(new XdgPathResolver(Environment()));

    private static CorePlatform Platform(XdgEnvironment environment) =>
        LinuxPlatform.Create(new XdgPathResolver(environment));

    private XdgEnvironment Environment() =>
        new(
            _root,
            Path.Combine(_root, "config"),
            Path.Combine(_root, "state"),
            Path.Combine(_root, "data"),
            Path.Combine(_root, "cache"),
            Path.Combine(_root, "run"),
            null,
            null);
}
