// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.UI.Chrome;
using Core.UI.Tray;
using Core.Platform.Linux;
using Core.Mvp.Models;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class WindowPlacementServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), string.Concat("core-placement-", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task It_restores_on_open_and_saves_on_close()
    {
        var path = Path.Combine(_directory, "placement.json");

        var (width, height) = await Headless.Dispatch(() =>
        {
            var first = new TestWindow { Width = 500, Height = 400 };
            _ = new WindowPlacementService(first, path, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());
            first.Show();
            first.UpdateLayout();
            first.Close();

            var second = new TestWindow();
            _ = new WindowPlacementService(second, path, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());
            second.Show();
            second.UpdateLayout();
            var size = (second.Width, second.Height);
            second.Close();

            return size;
        });

        Assert.Equal(500, width);
        Assert.Equal(400, height);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task A_window_without_a_saved_placement_keeps_its_size()
    {
        var path = Path.Combine(_directory, "missing.json");

        var width = await Headless.Dispatch(() =>
        {
            var window = new TestWindow { Width = 321 };
            _ = new WindowPlacementService(window, path, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());
            window.Show();
            window.UpdateLayout();
            var current = window.Width;
            window.Close();

            return current;
        });

        Assert.Equal(321, width);
    }

    [Fact]
    public async Task A_detached_service_stops_tracking()
    {
        await Headless.Dispatch(() =>
        {
            var window = new TestWindow();
            var service = new WindowPlacementService(window, Path.Combine(_directory, "detached.json"), new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());
            service.Detach();

            window.Show();
            window.Close();
        });

        Assert.False(File.Exists(Path.Combine(_directory, "detached.json")));
    }

    [Fact]
    public async Task It_saves_on_quit_without_closing_the_window()
    {
        var path = Path.Combine(_directory, "quit.json");

        await Headless.Dispatch(async () =>
        {
            var window = new TestWindow { Width = 500, Height = 400 };
            _ = new WindowPlacementService(window, path, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());
            window.Show();
            window.UpdateLayout();

            await window.QuitAsync();
            window.Close();
        });

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task It_captures_and_applies_a_placement()
    {
        await Headless.Dispatch(() =>
        {
            var window = new TestWindow { Width = 640, Height = 480 };
            window.Show();

            var captured = window.CapturePlacement();
            Assert.Equal(640, captured.Width);
            Assert.Equal(480, captured.Height);
            Assert.False(captured.Maximized);

            window.ApplyPlacement(new WindowPlacement(10, 20, 800, 600));
            Assert.Equal(800, window.Width);
            Assert.Equal(600, window.Height);

            Assert.Throws<ArgumentNullException>(() => window.ApplyPlacement(null!));

            window.ApplyPlacement(new WindowPlacement(0, 0, 0, 0));
            Assert.Equal(800, window.Width);

            window.Close();
        });
    }

    private sealed class TestWindow : CoreWindow
    {
        public TestWindow()
            : base(new TrayService(icon: null, activated: _ => { }))
        {
        }
    }
}
