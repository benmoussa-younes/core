// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Core.Platform.Linux;

namespace Core.IO.Tests;

/// <summary>
/// The single-instance lock is an OS resource, so these are integration tests over
/// a real Unix domain socket in a temporary directory.
/// </summary>
public sealed class SingleInstanceLockTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), string.Concat("core-lock-", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task The_first_instance_takes_the_claim_and_the_second_asks_it_to_surface()
    {
        var path = SocketPath();
        using var first = new SingleInstanceLock(path);
        Assert.True(first.TryAcquire());

        var surfaced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.ActivationRequested += () => surfaced.TrySetResult();

        using var second = new SingleInstanceLock(path);
        Assert.False(second.TryAcquire());

        await surfaced.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void A_stale_regular_file_is_replaced_by_the_claim()
    {
        var path = SocketPath();
        File.WriteAllText(path, "stale");

        using var instance = new SingleInstanceLock(path);

        Assert.Equal(path, instance.SocketPath);
        Assert.True(instance.TryAcquire());
    }

    [Fact]
    public void An_undeletable_stale_entry_is_left_for_the_bind()
    {
        var path = SocketPath();
        File.WriteAllText(path, "stale");

        var parent = Path.GetDirectoryName(path)!;
        var mode = File.GetUnixFileMode(parent);
        File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            using var instance = new SingleInstanceLock(path);

            Assert.False(instance.TryAcquire());
        }
        finally
        {
            File.SetUnixFileMode(parent, mode);
        }
    }

    [Fact]
    public void A_directory_at_the_socket_path_is_refused()
    {
        var path = SocketPath();
        Directory.CreateDirectory(path);

        using var instance = new SingleInstanceLock(path);

        Assert.False(instance.TryAcquire());
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void Dispose_removes_the_socket_and_allows_reacquire()
    {
        var path = SocketPath();
        var first = new SingleInstanceLock(path);
        Assert.True(first.TryAcquire());

        first.Dispose();
        first.Dispose();
        Assert.False(File.Exists(path));

        using var second = new SingleInstanceLock(path);
        Assert.True(second.TryAcquire());
    }

    [Fact]
    public void Acquiring_after_dispose_throws()
    {
        var path = SocketPath();
        var instance = new SingleInstanceLock(path);
        instance.Dispose();

        Assert.Throws<ObjectDisposedException>(() => instance.TryAcquire());
    }

    [Fact]
    public void The_default_path_names_the_application_and_the_user()
    {
        var path = SingleInstanceLock.DefaultSocketPath("widget");

        Assert.Contains("core-widget-", path, StringComparison.Ordinal);
        Assert.EndsWith(".sock", path, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => SingleInstanceLock.DefaultSocketPath(string.Empty));
        Assert.Throws<ArgumentException>(() => new SingleInstanceLock(string.Empty));
    }

    [Fact]
    public async Task A_later_launch_hands_its_payload_to_the_running_instance()
    {
        var path = SocketPath();
        using var first = new SingleInstanceLock(path);
        Assert.True(first.TryAcquire());

        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Activated += payload => received.TrySetResult(payload.ToArray());

        using var second = new SingleInstanceLock(path);
        Assert.False(second.TryAcquire("document.md"u8));

        var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal("document.md", Encoding.UTF8.GetString(payload));
    }

    [Fact]
    public async Task An_empty_payload_raises_both_activation_events()
    {
        var path = SocketPath();
        using var first = new SingleInstanceLock(path);
        Assert.True(first.TryAcquire());

        var activated = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Activated += payload => activated.TrySetResult(payload.ToArray());
        first.ActivationRequested += () => requested.TrySetResult();

        using var second = new SingleInstanceLock(path);
        Assert.False(second.TryAcquire());

        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Empty(await activated.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_old_one_byte_client_is_an_empty_payload()
    {
        var path = SocketPath();
        using var first = new SingleInstanceLock(path);
        Assert.True(first.TryAcquire());

        var activated = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Activated += payload => activated.TrySetResult(payload.ToArray());

        using (var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
        {
            client.Connect(new UnixDomainSocketEndPoint(path));
            client.Send([1]);
        }

        Assert.Empty(await activated.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_group_or_world_writable_runtime_directory_is_refused()
    {
        Directory.CreateDirectory(_directory);
        File.SetUnixFileMode(
            _directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);

        var path = Path.Combine(_directory, "app.sock");
        using var instance = new SingleInstanceLock(path);

        Assert.Throws<IOException>(() => instance.TryAcquire());
    }

    [Fact]
    public void The_socket_directory_is_created_owner_only()
    {
        var path = Path.Combine(_directory, "nested", "app.sock");
        using var instance = new SingleInstanceLock(path);

        Assert.True(instance.TryAcquire());

        var directory = Path.GetDirectoryName(path)!;
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(directory));
    }

    [Fact]
    public async Task A_throwing_subscriber_does_not_kill_the_accept_loop()
    {
        var path = SocketPath();
        using var first = new SingleInstanceLock(path);
        Assert.True(first.TryAcquire());

        first.Activated += _ => throw new InvalidOperationException("boom");

        var calls = 0;
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.ActivationRequested += () =>
        {
            calls++;

            if (calls == 1)
            {
                throw new InvalidOperationException("boom");
            }

            delivered.TrySetResult();
        };

        using (var second = new SingleInstanceLock(path))
        {
            Assert.False(second.TryAcquire("document.md"u8));
        }

        using (var third = new SingleInstanceLock(path))
        {
            Assert.False(third.TryAcquire());
        }

        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_stalled_client_does_not_wedge_the_next_activation()
    {
        var path = SocketPath();
        using var first = new SingleInstanceLock(path);
        Assert.True(first.TryAcquire());

        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.ActivationRequested += () => activated.TrySetResult();

        using (var stalled = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
        {
            stalled.Connect(new UnixDomainSocketEndPoint(path));
            stalled.Send([1]);

            // The server drops an incomplete frame after its activation timeout.
            await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        }

        using var second = new SingleInstanceLock(path);
        Assert.False(second.TryAcquire());

        await activated.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void A_second_acquire_on_the_owner_is_a_noop()
    {
        var path = SocketPath();
        using var instance = new SingleInstanceLock(path);

        Assert.True(instance.TryAcquire());
        Assert.True(instance.TryAcquire());
    }

    [Fact]
    public void Disposing_a_lock_that_never_acquired_is_harmless()
    {
        var path = SocketPath();
        using var instance = new SingleInstanceLock(path);

        instance.Dispose();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void An_oversized_payload_is_rejected_before_any_socket_work()
    {
        var path = SocketPath();
        using var instance = new SingleInstanceLock(path);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => instance.TryAcquire(new byte[SingleInstanceLock.MaxPayloadBytes + 1]));
    }

    [Fact]
    public async Task A_listener_closed_without_cancellation_stops_the_accept_loop()
    {
        var path = SocketPath();
        using var instance = new SingleInstanceLock(path);
        Assert.True(instance.TryAcquire());

        // Close the listener directly, without cancelling the lifetime token: this
        // is the production path where AcceptAsync raises ObjectDisposedException
        // or SocketException. The accept loop must absorb it and not fault.
        var listener = (Socket)typeof(SingleInstanceLock)
            .GetField("_listener", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(instance)!;

        listener.Dispose();
        await Task.Delay(100, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void The_default_path_rejects_a_separator_and_a_too_long_name()
    {
        Assert.Throws<ArgumentException>(() => SingleInstanceLock.DefaultSocketPath("../escape"));
        Assert.Throws<ArgumentException>(() => SingleInstanceLock.DefaultSocketPath(new string('a', 200)));
    }

    [Fact]
    public void The_constructor_rejects_a_path_the_kernel_cannot_hold()
    {
        var path = Path.Combine(Path.GetTempPath(), string.Concat(new string('a', 200), ".sock"));

        Assert.Throws<ArgumentException>(() => new SingleInstanceLock(path));
    }

    private string SocketPath()
    {
        Directory.CreateDirectory(_directory);
        File.SetUnixFileMode(
            _directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return Path.Combine(_directory, "app.sock");
    }
}
