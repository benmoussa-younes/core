// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Controls;
using Core.UI.Platform;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class ClipboardServiceTests
{
    // A 1x1 RGBA PNG, valid for the real Bitmap decoder (the headless tests use Skia).
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

    [Fact]
    public async Task Text_round_trips_through_the_shell_clipboard()
    {
        var result = await Headless.Dispatch(() =>
        {
            var window = new Window();
            window.Show();
            var service = new TopLevelClipboardService(window.Clipboard);

            service.SetTextAsync("hello").AsTask().GetAwaiter().GetResult();
            var text = service.GetTextAsync().AsTask().GetAwaiter().GetResult();
            window.Close();

            return text;
        });

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task Image_round_trips_through_the_shell_clipboard()
    {
        var png = Convert.FromBase64String(OnePixelPng);

        var result = await Headless.Dispatch(() =>
        {
            var window = new Window();
            window.Show();
            var service = new TopLevelClipboardService(window.Clipboard);

            service.SetImageAsync(png).AsTask().GetAwaiter().GetResult();
            var image = service.GetImageAsync().AsTask().GetAwaiter().GetResult();
            window.Close();

            return image;
        });

        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GetImage_returns_null_when_the_clipboard_holds_no_image()
    {
        var token = TestContext.Current.CancellationToken;

        var result = await Headless.Dispatch(() =>
        {
            var window = new Window();
            window.Show();
            var service = new TopLevelClipboardService(window.Clipboard);

            service.SetTextAsync("text only", token).AsTask().GetAwaiter().GetResult();
            var image = service.GetImageAsync(token).AsTask().GetAwaiter().GetResult();
            window.Close();

            return image;
        });

        Assert.Null(result);
    }

    [Fact]
    public async Task A_null_clipboard_is_a_noop()
    {
        var service = new TopLevelClipboardService(null);
        var token = TestContext.Current.CancellationToken;

        await service.SetTextAsync("x", token);
        Assert.Null(await service.GetTextAsync(token));

        await service.SetImageAsync([1, 2, 3], token);
        Assert.Null(await service.GetImageAsync(token));
    }

    [Fact]
    public async Task It_validates_and_honours_cancellation()
    {
        var service = new TopLevelClipboardService(null);
        var token = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SetTextAsync(null!, token).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SetImageAsync(null!, token).AsTask());

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SetTextAsync("x", canceled.Token).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetTextAsync(canceled.Token).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SetImageAsync([1], canceled.Token).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetImageAsync(canceled.Token).AsTask());
    }
}
