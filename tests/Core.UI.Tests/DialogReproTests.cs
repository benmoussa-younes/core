// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Controls;
using Avalonia.Interactivity;
using Core.Mvp.Services;
using Core.UI.Dialogs;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class DialogReproTests
{
    [Fact]
    public async Task A_password_prompt_forgets_the_secret_when_it_closes()
    {
        await Headless.Dispatch(() =>
        {
            var dialog = new PromptDialog();
            var window = new Window { Content = dialog };
            window.Show();

            _ = dialog.ShowAsync(new PromptRequest("Secret", "Token", IsPassword: true));
            dialog.Input.Text = "s3cr3t";
            Click(dialog.ConfirmButton);

            Assert.False(dialog.IsOpen);
            Assert.Equal(string.Empty, dialog.Input.Text);
            Assert.Equal('\0', dialog.Input.PasswordChar);

            window.Close();
        });
    }

    [Fact]
    public async Task A_stale_completion_does_not_close_the_modal_layer_of_another_dialog()
    {
        await Headless.Dispatch(async () =>
        {
            // CoreWindow gives both dialogs the same begin/end callbacks, so they
            // share one depth counter. A completion that arrives after the dialog
            // closed must not decrement the depth the other dialog owns.
            var depth = 0;
            var confirm = new ConfirmDialog(_ => depth++, () => depth--);
            var prompt = new PromptDialog(_ => depth++, () => depth--);
            var window = new Window { Content = new Panel { Children = { confirm, prompt } } };
            window.Show();

            var first = confirm.ShowAsync(new ConfirmRequest("First"));
            Click(confirm.CancelButton);
            await first;
            Assert.Equal(0, depth);

            _ = prompt.ShowAsync(new PromptRequest("Second", "Value"));
            Assert.Equal(1, depth);

            confirm.Dismiss();

            Assert.Equal(1, depth);

            prompt.Dismiss();
            window.Close();
        });
    }

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
