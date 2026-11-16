// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Composition;
using Core.Mvp.Presentation;
using Core.Mvp.Presenters;
using Core.Mvp.Services;
using Core.Mvp.Views;
using Core.Results;

namespace Core.Mvp.Tests;

public sealed class ShellContractsTests
{
    [Fact]
    public void AppCommand_carries_its_identity_and_state()
    {
        var command = new AppCommand(
            "save",
            "Save",
            "IconSave",
            "Save the document",
            "Ctrl+S",
            IsEnabled: true,
            IsChecked: false,
            Group: "file");

        Assert.Equal("save", command.Id);
        Assert.Equal("Save", command.Label);
        Assert.Equal("IconSave", command.IconKey);
        Assert.Equal("Save the document", command.ToolTip);
        Assert.Equal("Ctrl+S", command.Shortcut);
        Assert.True(command.IsEnabled);
        Assert.False(command.IsChecked);
        Assert.Equal("file", command.Group);
    }

    [Fact]
    public void CommandSet_lists_commands_and_has_an_empty_instance()
    {
        var command = new AppCommand("quit", "Quit");
        var set = new CommandSet([command]);

        Assert.Same(command, Assert.Single(set.Commands));
        Assert.Empty(CommandSet.Empty.Commands);
    }

    [Fact]
    public void ShellSnapshot_carries_the_chrome_state()
    {
        var snapshot = new ShellSnapshot("Title", "Subtitle", CommandSet.Empty, Busy: true, CanGoBack: true);

        Assert.Equal("Title", snapshot.Title);
        Assert.Equal("Subtitle", snapshot.Subtitle);
        Assert.Same(CommandSet.Empty, snapshot.Commands);
        Assert.True(snapshot.Busy);
        Assert.True(snapshot.CanGoBack);
    }

    [Fact]
    public async Task A_host_can_keep_the_legacy_snapshot_and_shutdown_contract()
    {
        var host = new StubApplicationHost();
        var snapshot = new ShellSnapshot("Title", "Subtitle", CommandSet.Empty);

        IViewHost viewHost = host;
        Assert.Throws<NotSupportedException>(() => viewHost.SetSnapshot(new StubPresenter(), snapshot));
        host.SetSnapshot(snapshot);

        Assert.Same(snapshot, host.LastSnapshot);
        Assert.Throws<NotSupportedException>(() => ((IViewHost)host).Prompts);
        Assert.Throws<NotSupportedException>(() => ((IViewHost)host).Folders);
        await ((IApplicationHost)host).ShutdownAsync();
    }

    [Fact]
    public void A_host_without_presenter_binding_refuses_the_presenter_overloads()
    {
        var navigation = new StubNavigationHost();
        var presenter = new StubPresenter();

        INavigationHost defaults = navigation;

        Assert.Throws<NotSupportedException>(() => defaults.OpenTab("one", "One", presenter));
        Assert.Throws<NotSupportedException>(() => defaults.Push("Detail", "Subtitle", presenter));

        Assert.Null(navigation.LastView);
        Assert.Null(navigation.LastTitle);
    }

    [Fact]
    public void ConfirmRequest_carries_the_question_and_labels()
    {
        var request = new ConfirmRequest(
            "Delete?",
            "This cannot be undone.",
            "Delete",
            "Keep",
            "Cancel",
            Destructive: true);

        Assert.Equal("Delete?", request.Title);
        Assert.Equal("This cannot be undone.", request.Body);
        Assert.Equal("Delete", request.ConfirmLabel);
        Assert.Equal("Keep", request.AlternativeLabel);
        Assert.Equal("Cancel", request.CancelLabel);
        Assert.True(request.Destructive);
    }

    private sealed class StubView : IView
    {
        public void SetBusy(bool busy)
        {
        }

        public void ShowError(CoreError error)
        {
        }
    }

    private sealed class StubPresenter : IViewPresenter
    {
        public IView View { get; } = new StubView();

        public bool IsBusy => false;

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) => default;

        public ValueTask ActivateAsync(CancellationToken cancellationToken = default) => default;

        public ValueTask DeactivateAsync(CancellationToken cancellationToken = default) => default;

        public ValueTask DisposeAsync() => default;
    }

    private class StubHost : IViewHost
    {
        public IDialogService Dialogs => null!;

        public INotificationService Notifications => null!;

        public IFilePicker Files => null!;

        public IClipboardService Clipboard => null!;

        public ShellSnapshot? LastSnapshot { get; private set; }

        public event Action<IView, string> CommandInvoked
        {
            add { }
            remove { }
        }

        public void SetSnapshot(ShellSnapshot snapshot) => LastSnapshot = snapshot;
    }

    private sealed class StubNavigationHost : INavigationHost
    {
        public IView? Active => null;

        public IView? LastView { get; private set; }

        public string? LastTitle { get; private set; }

        public void OpenTab(string id, string title, IView view)
        {
            LastView = view;
            LastTitle = title;
        }

        public void CloseTab(string id)
        {
        }

        public void Push(string title, string subtitle, IView view)
        {
            LastView = view;
            LastTitle = title;
        }

        public void Pop()
        {
        }
    }

    private sealed class StubApplicationHost : StubHost, IApplicationHost
    {
        public INavigationHost Navigation => null!;

        public ITrayService Tray => null!;

        public CloseBehavior CloseBehavior { get; set; }

        public event Action? Activated
        {
            add { }
            remove { }
        }

        public void SetApplicationMenu(CommandSet menu)
        {
        }

        public void ShowWindow()
        {
        }

        public void HideWindow()
        {
        }

        public void Quit()
        {
        }
    }
}
