// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Controls;
using Core.Mvp.Services;
using Core.UI.Dialogs;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class DialogServiceTests
{
    [Fact]
    public async Task Confirm_passes_the_request_to_the_dialog()
    {
        var dialog = new ConfirmDialog();
        var service = new DialogService(dialog);

        var task = service.ConfirmAsync(new ConfirmRequest("Q", string.Empty, "Yes"), TestContext.Current.CancellationToken).AsTask();
        Click(dialog.ConfirmButton);

        Assert.Equal(ConfirmResult.Accept, await task);
    }

    [Theory]
    [InlineData(ConfirmResult.Accept, SavePromptChoice.Save)]
    [InlineData(ConfirmResult.Alternative, SavePromptChoice.Discard)]
    [InlineData(ConfirmResult.Cancel, SavePromptChoice.Cancel)]
    public async Task Save_maps_every_answer(ConfirmResult answer, SavePromptChoice expected)
    {
        var dialog = new ConfirmDialog();
        var service = new DialogService(dialog);

        var task = service.ConfirmSaveAsync("report.csv", TestContext.Current.CancellationToken).AsTask();
        Click(answer switch
        {
            ConfirmResult.Accept => dialog.ConfirmButton,
            ConfirmResult.Alternative => dialog.AlternativeButton,
            _ => dialog.CancelButton,
        });

        Assert.Equal(expected, await task);
    }

    [Theory]
    [InlineData(ConfirmResult.Accept, QuitChoice.Quit)]
    [InlineData(ConfirmResult.Cancel, QuitChoice.Cancel)]
    public async Task Quit_maps_every_answer(ConfirmResult answer, QuitChoice expected)
    {
        var dialog = new ConfirmDialog();
        var service = new DialogService(dialog);

        var task = service.ConfirmQuitAsync(TestContext.Current.CancellationToken).AsTask();
        Click(answer == ConfirmResult.Accept ? dialog.ConfirmButton : dialog.CancelButton);

        Assert.Equal(expected, await task);
    }

    [Fact]
    public async Task Invalid_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new DialogService(null!));

        var service = new DialogService(new ConfirmDialog());
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.ConfirmSaveAsync(null!, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Cancelling_the_token_answers_cancel()
    {
        await Headless.Dispatch(async () =>
        {
            var dialog = new ConfirmDialog();
            var service = new DialogService(dialog);
            using var source = new CancellationTokenSource();

            var task = service.ConfirmAsync(new ConfirmRequest("Q", string.Empty, "Yes"), source.Token).AsTask();
            source.Cancel();

            Assert.Equal(ConfirmResult.Cancel, await task);
        });
    }

    [Fact]
    public async Task Confirmation_requests_are_serialized()
    {
        await Headless.Dispatch(async () =>
        {
            var dialog = new ConfirmDialog();
            var service = new DialogService(dialog);

            var first = service.ConfirmAsync(new ConfirmRequest("First", string.Empty, "Yes"), TestContext.Current.CancellationToken).AsTask();
            var second = service.ConfirmAsync(new ConfirmRequest("Second", string.Empty, "Yes"), TestContext.Current.CancellationToken).AsTask();

            Assert.True(dialog.IsOpen);
            Assert.False(second.IsCompleted);

            Click(dialog.CancelButton);
            Assert.Equal(ConfirmResult.Cancel, await first);

            await WaitUntilAsync(() => dialog.IsOpen);
            Assert.True(dialog.IsOpen);

            Click(dialog.CancelButton);
            Assert.Equal(ConfirmResult.Cancel, await second);
        });
    }

    [Fact]
    public async Task A_cancelled_confirmation_releases_the_gate_for_the_next_one()
    {
        await Headless.Dispatch(async () =>
        {
            var dialog = new ConfirmDialog();
            var service = new DialogService(dialog);
            using var source = new CancellationTokenSource();

            var first = service.ConfirmAsync(new ConfirmRequest("First", string.Empty, "Yes"), source.Token).AsTask();
            source.Cancel();
            Assert.Equal(ConfirmResult.Cancel, await first);

            var second = service.ConfirmAsync(new ConfirmRequest("Second", string.Empty, "Yes"), TestContext.Current.CancellationToken).AsTask();

            Assert.True(dialog.IsOpen);
            Click(dialog.CancelButton);
            Assert.Equal(ConfirmResult.Cancel, await second);
        });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }
}
