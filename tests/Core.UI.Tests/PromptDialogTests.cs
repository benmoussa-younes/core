// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Core.UI.Dialogs;
using Core.Mvp.Services;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class PromptDialogTests
{
    [Fact]
    public async Task It_returns_the_typed_value()
    {
        var result = await Headless.Dispatch(() =>
        {
            var window = Show(out var dialog);
            var task = dialog.ShowAsync(new PromptRequest("Rename", "Name", "old"));

            dialog.Input.Text = "new value";
            Click(dialog.ConfirmButton);
            window.Close();

            return task;
        });

        Assert.Equal("new value", await result);
    }

    [Fact]
    public async Task Cancel_and_dismiss_return_null()
    {
        var cancelled = await Headless.Dispatch(() =>
        {
            var window = Show(out var dialog);
            var task = dialog.ShowAsync(new PromptRequest("Rename", "Name"));

            Click(dialog.CancelButton);
            window.Close();

            return task;
        });

        Assert.Null(await cancelled);

        var dismissed = await Headless.Dispatch(() =>
        {
            var window = Show(out var dialog);
            var task = dialog.ShowAsync(new PromptRequest("Rename", "Name"));

            dialog.Dismiss();
            window.Close();

            return task;
        });

        Assert.Null(await dismissed);
    }

    [Fact]
    public async Task Enter_confirms_and_Escape_cancels()
    {
        var entered = await Headless.Dispatch(() =>
        {
            var window = Show(out var dialog);
            var task = dialog.ShowAsync(new PromptRequest("Rename", "Name"));

            dialog.Input.Text = "typed";
            dialog.Input.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
            });
            window.Close();

            return task;
        });

        Assert.Equal("typed", await entered);

        var escaped = await Headless.Dispatch(() =>
        {
            var window = Show(out var dialog);
            var task = dialog.ShowAsync(new PromptRequest("Rename", "Name"));

            dialog.Input.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape,
            });
            window.Close();

            return task;
        });

        Assert.Null(await escaped);
    }

    [Fact]
    public async Task The_confirm_and_cancel_buttons_receive_a_real_mouse_click()
    {
        var confirmed = await Headless.Dispatch(() =>
        {
            var window = Show(out var dialog);
            var task = dialog.ShowAsync(new PromptRequest("Rename", "Name", "old"));

            dialog.Input.Text = "mouse";
            window.UpdateLayout();
            window.Activate();

            // Hit testing uses the rendered scene: the prompt was hidden at the
            // window's first frame, so it is invisible to InputHitTest until a
            // render pass even though its layout is valid.
            _ = window.CaptureRenderedFrame();

            var centre = ButtonCentre(dialog, dialog.ConfirmButton);
            var windowCentre = dialog.TranslatePoint(centre, window);
            Assert.NotNull(windowCentre);
            AssertHit(window, dialog.ConfirmButton, windowCentre.Value);
            window.MouseDown(windowCentre.Value, MouseButton.Left);
            window.MouseUp(windowCentre.Value, MouseButton.Left);
            window.Close();

            return task;
        });

        Assert.Equal("mouse", await confirmed);

        var cancelled = await Headless.Dispatch(() =>
        {
            var window = Show(out var dialog);
            var task = dialog.ShowAsync(new PromptRequest("Rename", "Name"));

            window.UpdateLayout();
            window.Activate();
            _ = window.CaptureRenderedFrame();

            var centre = ButtonCentre(dialog, dialog.CancelButton);
            var windowCentre = dialog.TranslatePoint(centre, window);
            Assert.NotNull(windowCentre);
            AssertHit(window, dialog.CancelButton, windowCentre.Value);
            window.MouseDown(windowCentre.Value, MouseButton.Left);
            window.MouseUp(windowCentre.Value, MouseButton.Left);
            window.Close();

            return task;
        });

        Assert.Null(await cancelled);
    }

    [Fact]
    public async Task A_password_prompt_masks_the_field()
    {
        await Headless.Dispatch(() =>
        {
            var window = Show(out var dialog);
            _ = dialog.ShowAsync(new PromptRequest("Secret", "Token", IsPassword: true));

            Assert.NotEqual('\0', dialog.Input.PasswordChar);
            Assert.True(dialog.IsOpen);

            dialog.Dismiss();
            window.Close();
        });
    }

    [Fact]
    public async Task It_rejects_a_second_prompt_and_a_null_request()
    {
        await Headless.Dispatch(() =>
        {
            var window = Show(out var dialog);

            Assert.Throws<ArgumentNullException>((Action)(() => _ = dialog.ShowAsync(null!)));

            _ = dialog.ShowAsync(new PromptRequest("One", "Name"));
            Assert.Throws<InvalidOperationException>((Action)(() => _ = dialog.ShowAsync(new PromptRequest("Two", "Name"))));

            dialog.Dismiss();
            window.Close();
        });
    }

    [Fact]
    public async Task The_service_returns_the_value_and_cancels_to_null()
    {
        var value = await Headless.Dispatch(() =>
        {
            var window = Show(out var dialog);
            var service = new PromptService(dialog);
            var task = service.PromptAsync(new PromptRequest("Rename", "Name")).AsTask();

            dialog.Input.Text = "typed";
            Click(dialog.ConfirmButton);
            window.Close();

            return task;
        });

        Assert.Equal("typed", await value);

        using var cancellation = new CancellationTokenSource();
        await Headless.Dispatch(async () =>
        {
            var window = Show(out var dialog);
            var service = new PromptService(dialog);
            var task = service.PromptAsync(new PromptRequest("Rename", "Name"), cancellation.Token).AsTask();
            cancellation.Cancel();

            Assert.Null(await task);
            window.Close();
        });
    }

    [Fact]
    public void The_service_validates_its_request()
    {
        var dialog = new PromptDialog();
        var service = new PromptService(dialog);

        Assert.Throws<ArgumentNullException>((Action)(() => _ = service.PromptAsync(null!, TestContext.Current.CancellationToken)));
        Assert.Throws<ArgumentNullException>(() => new PromptService(null!));
    }

    [Fact]
    public void The_request_carries_its_defaults_and_can_be_changed_with()
    {
        var request = new PromptRequest("Rename", "Name");

        Assert.Equal("Rename", request.Title);
        Assert.Equal("Name", request.Label);
        Assert.Equal(string.Empty, request.Value);
        Assert.Equal("OK", request.ConfirmLabel);
        Assert.Equal("Cancel", request.CancelLabel);
        Assert.False(request.IsPassword);

        var changed = request with { Value = "v", IsPassword = true, ConfirmLabel = "Save" };
        Assert.Equal("v", changed.Value);
        Assert.True(changed.IsPassword);
        Assert.Equal("Save", changed.ConfirmLabel);
        Assert.NotEqual(request, changed);
        Assert.Equal(changed, changed with { });

        var (title, label, value, confirm, cancel, password) = changed;
        Assert.Equal("Rename", title);
        Assert.Equal("Name", label);
        Assert.Equal("v", value);
        Assert.Equal("Save", confirm);
        Assert.Equal("Cancel", cancel);
        Assert.True(password);
        _ = changed.ToString();
        _ = changed.GetHashCode();
    }

    private static Window Show(out PromptDialog dialog)
    {
        dialog = new PromptDialog();
        var window = new Window { Width = 800, Height = 600, Content = dialog };
        window.Show();
        window.Activate();
        return window;
    }

    private static void AssertHit(Control relativeTo, Button button, Point centre)
    {
        // The topmost hit may be the button's content presenter; the click route
        // still bubbles through the button, so assert the hit is its descendant.
        var hit = relativeTo.InputHitTest(centre) as Visual;

        Assert.True(
            hit is not null,
            $"centre={centre} root={relativeTo.Bounds} button={button.Bounds} visible={button.IsEffectivelyVisible}");
        Assert.True(
            ReferenceEquals(hit, button) || button.IsVisualAncestorOf(hit!),
            $"centre={centre} root={relativeTo.Bounds} button={button.Bounds} hit={hit}");
    }

    private static Point ButtonCentre(Control relativeTo, Button button)
    {
        Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0, "The button was not laid out.");
        Assert.True(relativeTo.Bounds.Width > 0, $"The root was not arranged: {relativeTo.Bounds}.");

        var centre = button.TranslatePoint(
            new Point(button.Bounds.Width / 2, button.Bounds.Height / 2),
            relativeTo);

        Assert.NotNull(centre);
        return centre.Value;
    }

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
