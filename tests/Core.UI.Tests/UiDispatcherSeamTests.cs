// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Platform.Storage;
using Core.Mvp.Presentation;
using Core.Mvp.Services;
using Core.UI.Dialogs;
using Core.UI.Hosting;
using Core.UI.Notifications;
using Core.UI.Platform;
using Core.UI.Preferences;
using Core.UI.Tray;

namespace Core.UI.Tests;

// The headless dispatcher reports CheckAccess on every thread, so the thread
// affinity of the services can only be proven through the seam they call.
[Collection("headless")]
public sealed class UiDispatcherSeamTests
{
    [Fact]
    public async Task The_dialog_services_refuse_a_foreign_thread()
    {
        DialogService? dialogs = null;
        PromptService? prompts = null;

        await Headless.Dispatch(() =>
        {
            dialogs = new DialogService(new ConfirmDialog());
            prompts = new PromptService(new PromptDialog());
        });

        WithDispatcher(new RejectingDispatcher(), () =>
        {
            Assert.Throws<InvalidOperationException>(
                () => { _ = dialogs!.ConfirmAsync(new ConfirmRequest("Q"), TestContext.Current.CancellationToken); });
            Assert.Throws<InvalidOperationException>(
                () => { _ = prompts!.PromptAsync(new PromptRequest("Rename", "Name"), TestContext.Current.CancellationToken); });
        });
    }

    [Fact]
    public async Task The_shell_services_refuse_a_foreign_thread()
    {
        NotificationService? notifications = null;
        TrayService? tray = null;
        AppearanceService? appearance = null;

        await Headless.Dispatch(() =>
        {
            notifications = new NotificationService(new NotificationHost());
            tray = new TrayService(icon: null, activated: _ => { });
            appearance = new AppearanceService(new EmptyAppearanceStore());
        });

        WithDispatcher(new RejectingDispatcher(), () =>
        {
            Assert.Throws<InvalidOperationException>(() => notifications!.Toast("message"));
            Assert.Throws<InvalidOperationException>(() => tray!.Show("Core", CommandSet.Empty));
            Assert.Throws<InvalidOperationException>(() => appearance!.ChooseTheme(ThemePreference.Dark));
        });
    }

    [Fact]
    public async Task The_platform_services_refuse_a_foreign_thread()
    {
        TopLevelClipboardService? clipboard = null;
        StorageProviderFilePicker? picker = null;

        await Headless.Dispatch(() =>
        {
            clipboard = new TopLevelClipboardService(null);
            picker = new StorageProviderFilePicker(
                _ => Task.FromResult<IReadOnlyList<IStorageFile>>([]),
                _ => Task.FromResult<IStorageFile?>(null));
        });

        // The clipboard and picker members are async, so the guard surfaces on the
        // returned task rather than synchronously.
        var previous = UiServices.Dispatcher;
        UiServices.Dispatcher = new RejectingDispatcher();

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => clipboard!.GetTextAsync(TestContext.Current.CancellationToken).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => picker!.OpenFilesAsync(new FilePickerOptions("Open"), TestContext.Current.CancellationToken).AsTask());
        }
        finally
        {
            UiServices.Dispatcher = previous;
        }
    }

    [Fact]
    public async Task A_cancellation_completes_when_the_dispatcher_is_shutting_down()
    {
        ConfirmDialog? dialog = null;
        DialogService? service = null;

        await Headless.Dispatch(() =>
        {
            dialog = new ConfirmDialog();
            service = new DialogService(dialog);
        });

        var previous = UiServices.Dispatcher;
        UiServices.Dispatcher = new UnavailableDispatcher();

        try
        {
            using var source = new CancellationTokenSource();
            var task = service!.ConfirmAsync(new ConfirmRequest("Q"), source.Token).AsTask();
            source.Cancel();

            Assert.Equal(ConfirmResult.Cancel, await task);
        }
        finally
        {
            UiServices.Dispatcher = previous;
        }

        await Headless.Dispatch(() => dialog!.Dismiss());
    }

    private static void WithDispatcher(IUiDispatcher dispatcher, Action body)
    {
        var previous = UiServices.Dispatcher;
        UiServices.Dispatcher = dispatcher;

        try
        {
            body();
        }
        finally
        {
            UiServices.Dispatcher = previous;
        }
    }

    private sealed class RejectingDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => false;

        public void VerifyAccess() => throw new InvalidOperationException("The call is not on the UI thread.");

        public void Post(Action action) => action();

        public bool IsAvailable => true;
    }

    private sealed class UnavailableDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => false;

        public void VerifyAccess()
        {
        }

        // The loop is gone: a posted action never runs.
        public void Post(Action action)
        {
        }

        public bool IsAvailable => false;
    }

    private sealed class EmptyAppearanceStore : IAppearanceStore
    {
        public AppearanceSettings? Load() => null;

        public void Save(AppearanceSettings settings)
        {
        }

        public void Clear()
        {
        }
    }
}
