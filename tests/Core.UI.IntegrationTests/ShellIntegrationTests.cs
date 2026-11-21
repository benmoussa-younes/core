// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Threading;
using Core.Mvp.Services;
using Core.UI.Chrome;
using Core.UI.Tray;

namespace Core.UI.IntegrationTests;

// The proof the headless suite cannot give: a real dispatcher that enforces the
// thread, and a window that reaches the compositor.
[Collection("desktop")]
public sealed class ShellIntegrationTests
{
    [GuiFact]
    public void The_real_dispatcher_refuses_a_foreign_thread()
    {
        Exception? seen = null;

        var thread = new Thread(() =>
        {
            try
            {
                Dispatcher.UIThread.VerifyAccess();
            }
            catch (Exception exception)
            {
                seen = exception;
            }
        });

        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10));

        Assert.IsType<InvalidOperationException>(seen);
    }

    [GuiFact]
    public async Task The_shell_window_opens_and_closes()
    {
        var (shown, closed) = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = CreateWindow();
            window.Show();
            var visible = window.IsVisible;
            window.Close();
            return (visible, window.IsVisible);
        });

        Assert.True(shown, "the window did not become visible");
        Assert.False(closed, "the window did not close");
    }

    [GuiFact]
    public async Task The_shell_refuses_a_dialog_call_from_a_foreign_thread()
    {
        var window = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var created = CreateWindow();
            created.Show();
            return created;
        });

        Exception? seen = null;

        var thread = new Thread(() =>
        {
            try
            {
                _ = window.Dialogs.ConfirmAsync(new ConfirmRequest("Q"));
            }
            catch (Exception exception)
            {
                seen = exception;
            }
        });

        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10));

        Assert.IsType<InvalidOperationException>(seen);

        await Dispatcher.UIThread.InvokeAsync(window.Close);
    }

    private static CoreWindow CreateWindow() => new(new TrayService(icon: null, activated: _ => { }));
}
