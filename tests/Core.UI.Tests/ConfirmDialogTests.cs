// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Controls;
using Core.Mvp.Services;
using Core.UI.Dialogs;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class ConfirmDialogTests
{
    [Fact]
    public async Task The_confirm_button_completes_with_accept()
    {
        var dialog = new ConfirmDialog();
        var task = dialog.ShowAsync(new ConfirmRequest("Title", "Body", "Yes", "No", "Cancel"));

        Assert.True(dialog.IsOpen);
        Click(dialog.ConfirmButton);

        Assert.Equal(ConfirmResult.Accept, await task);
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public async Task The_alternative_button_completes_with_alternative()
    {
        var dialog = new ConfirmDialog();
        var task = dialog.ShowAsync(new ConfirmRequest("Title", "Body", "Yes", "No", "Cancel"));

        Click(dialog.AlternativeButton);

        Assert.Equal(ConfirmResult.Alternative, await task);
    }

    [Fact]
    public async Task The_cancel_button_completes_with_cancel()
    {
        var dialog = new ConfirmDialog();
        var task = dialog.ShowAsync(new ConfirmRequest("Title", "Body", "Yes", "No", "Cancel"));

        Click(dialog.CancelButton);

        Assert.Equal(ConfirmResult.Cancel, await task);
    }

    [Fact]
    public async Task A_second_answer_is_ignored()
    {
        var dialog = new ConfirmDialog();
        var task = dialog.ShowAsync(new ConfirmRequest("Title"));

        Click(dialog.ConfirmButton);
        Click(dialog.CancelButton);

        Assert.Equal(ConfirmResult.Accept, await task);
    }

    [Fact]
    public async Task An_empty_body_is_hidden()
    {
        var dialog = new ConfirmDialog();
        var task = dialog.ShowAsync(new ConfirmRequest("Title"));

        Assert.False(dialog.BodyBlock.IsVisible);
        Click(dialog.CancelButton);
        await task;
    }

    [Fact]
    public async Task A_destructive_prompt_marks_the_confirm_button()
    {
        var dialog = new ConfirmDialog();
        var task = dialog.ShowAsync(new ConfirmRequest("Title", "Body", "Delete", "Keep", "Cancel", Destructive: true));

        Assert.Contains("destructive", dialog.ConfirmButton.Classes);

        Click(dialog.CancelButton);
        await task;
    }

    [Fact]
    public async Task An_empty_alternative_is_hidden()
    {
        var dialog = new ConfirmDialog();
        var task = dialog.ShowAsync(new ConfirmRequest("Title", "Body", "Save", string.Empty, "Cancel"));

        Assert.False(dialog.AlternativeButton.IsVisible);

        Click(dialog.ConfirmButton);
        await task;
    }

    [Fact]
    public void A_null_request_is_rejected()
    {
        var dialog = new ConfirmDialog();

        Assert.Throws<ArgumentNullException>(() => { _ = dialog.ShowAsync(null!); });
    }

    [Fact]
    public async Task A_second_show_is_rejected_while_one_is_open()
    {
        var dialog = new ConfirmDialog();
        var task = dialog.ShowAsync(new ConfirmRequest("Title"));

        Assert.Throws<InvalidOperationException>(() => { _ = dialog.ShowAsync(new ConfirmRequest("Again")); });

        Click(dialog.CancelButton);
        Assert.Equal(ConfirmResult.Cancel, await task);
    }

    [Fact]
    public async Task Dismiss_answers_cancel_and_is_a_noop_when_closed()
    {
        var dialog = new ConfirmDialog();

        dialog.Dismiss();

        var task = dialog.ShowAsync(new ConfirmRequest("Title"));
        dialog.Dismiss();

        Assert.Equal(ConfirmResult.Cancel, await task);
        Assert.False(dialog.IsOpen);
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }
}
