// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Core.UI.Dialogs;
using Core.UI.Preferences;
using Core.Mvp.Presentation;
using Core.Mvp.Services;

namespace Core.UI.Chrome;

/// <summary>
/// Builds and owns the shell's primary menu and its keyboard accelerators: the
/// product commands, the standard About/Preferences/Quit tail, and the single
/// deduplicated <see cref="KeyBinding"/> set shared by the toolbar and the menu.
/// Extracted from <see cref="CoreWindow"/> so the window stays composition
/// and routing.
/// </summary>
internal sealed class ShellMenuBinder
{
    private readonly CoreWindow _window;
    private readonly MenuFlyout _menu;
    private readonly List<KeyBinding> _bindings = [];
    private IReadOnlyList<AppCommand>? _boundViewCommands;
    private IReadOnlyList<AppCommand>? _boundMenuCommands;
    private CommandSet _applicationMenu = CommandSet.Empty;
    private AboutInfo? _about;
    private IAppearanceService? _appearance;
    private bool _suspended;

    internal ShellMenuBinder(CoreWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _window = window;
        _menu = window.PrimaryMenu;
        RebuildApplicationMenu();
    }

    // The dialog factories are a seam for tests: the shell opens the real shared
    // dialogs, a headless test substitutes a stub so it can assert the routing.
    internal Func<AboutInfo, Window> AboutFactory { get; set; } = static info => new AboutDialog(info);

    internal Func<IAppearanceService, Window> PreferencesFactory { get; set; } =
        static service => new PreferencesDialog(service);

    internal void SetApplicationMenu(CommandSet menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        _applicationMenu = menu;
        RebuildApplicationMenu();
        RebuildBindings(CurrentViewCommands(), force: true);
    }

    internal void SetAbout(AboutInfo? info)
    {
        _about = info;
        RebuildApplicationMenu();
        RebuildBindings(CurrentViewCommands(), force: true);
    }

    internal void SetAppearance(IAppearanceService? service)
    {
        _appearance = service;
        RebuildApplicationMenu();
        RebuildBindings(CurrentViewCommands(), force: true);
    }

    internal void RebuildBindings(IReadOnlyList<AppCommand>? viewCommands, bool force = false)
    {
        var menuCommands = _applicationMenu.Commands;

        // A presenter that re-publishes an unchanged command set must not re-parse
        // every shortcut and swap the accelerators on each snapshot.
        if (!force
            && ActionBar.SameCommands(_boundViewCommands, viewCommands)
            && ActionBar.SameCommands(_boundMenuCommands, menuCommands))
        {
            return;
        }

        _boundViewCommands = viewCommands;
        _boundMenuCommands = menuCommands;

        // A modal overlay suspends the accelerators; the last snapshot is kept so
        // ResumeBindings can rebuild the same set once the dialog closes.
        if (_suspended)
        {
            return;
        }

        ClearBindings();

        // One key press runs one command: a gesture is bound at most once, the
        // first declaration winning, so a command that appears both in the toolbar
        // and the application menu does not fire twice. The gesture, not its
        // string spelling, is the identity: Ctrl+S and ctrl+s are the same key.
        var gestures = new HashSet<KeyGesture>();
        AddBindings(viewCommands, gestures);
        AddBindings(StandardCommands(), gestures);
        AddBindings(menuCommands, gestures);
    }

    internal void SuspendBindings()
    {
        if (_suspended)
        {
            return;
        }

        _suspended = true;
        ClearBindings();
    }

    internal void ResumeBindings()
    {
        if (!_suspended)
        {
            return;
        }

        _suspended = false;
        RebuildBindings(CurrentViewCommands(), force: true);
    }

    private void ClearBindings()
    {
        foreach (var binding in _bindings)
        {
            _window.KeyBindings.Remove(binding);
        }

        _bindings.Clear();
    }

    internal void OnCommandInvoked(string id)
    {
        switch (id)
        {
            case ShellCommandIds.Quit:
                _window.Quit();
                return;
            case ShellCommandIds.About when _about is not null:
                _ = ShowAboutAsync(_about);
                return;
            case ShellCommandIds.Preferences when _appearance is not null:
                _ = ShowPreferencesAsync(_appearance);
                return;
            default:
                _window.RaiseCommandInvoked(id);
                return;
        }
    }

    private async Task ShowAboutAsync(AboutInfo info)
    {
        try
        {
            await AboutFactory(info).ShowDialog(_window).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _window.ReportLifecycleFailure("open About", exception);
        }
    }

    private async Task ShowPreferencesAsync(IAppearanceService appearance)
    {
        try
        {
            await PreferencesFactory(appearance).ShowDialog(_window).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _window.ReportLifecycleFailure("open Preferences", exception);
        }
    }

    private IReadOnlyList<AppCommand>? CurrentViewCommands() => _window.CurrentSnapshot?.Commands.Commands;

    private void AddBindings(IReadOnlyList<AppCommand>? commands, HashSet<KeyGesture> gestures)
    {
        if (commands is null)
        {
            return;
        }

        foreach (var command in commands)
        {
            if (string.IsNullOrEmpty(command.Shortcut) || !command.IsEnabled)
            {
                continue;
            }

            KeyGesture gesture;

            try
            {
                gesture = KeyGesture.Parse(command.Shortcut);
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            {
                continue;
            }

            if (!gestures.Add(gesture))
            {
                continue;
            }

            var id = command.Id;
            var binding = new KeyBinding
            {
                Gesture = gesture,
                Command = new CommandRelay(() => OnCommandInvoked(id)),
            };

            _bindings.Add(binding);
            _window.KeyBindings.Add(binding);
        }
    }

    // The standard tail the shell always offers, above whatever the product
    // published: About and Preferences once they are wired, and Quit always.
    private List<AppCommand> StandardCommands()
    {
        var commands = new List<AppCommand>();

        if (_about is not null)
        {
            commands.Add(new AppCommand(ShellCommandIds.About, "About\u2026"));
        }

        if (_appearance is not null)
        {
            commands.Add(new AppCommand(ShellCommandIds.Preferences, "Preferences\u2026"));
        }

        commands.Add(new AppCommand(ShellCommandIds.Quit, "Quit"));
        return commands;
    }

    private void RebuildApplicationMenu()
    {
        _menu.Items.Clear();

        foreach (var command in _applicationMenu.Commands)
        {
            _menu.Items.Add(CreateMenuItem(command));
        }

        _menu.Items.Add(new Separator());

        foreach (var command in StandardCommands())
        {
            _menu.Items.Add(CreateMenuItem(command));
        }
    }

    private MenuItem CreateMenuItem(AppCommand command)
    {
        var item = new MenuItem
        {
            Header = BuildMenuHeader(command),
            IsEnabled = command.IsEnabled,
            IsChecked = command.IsChecked,
        };

        var id = command.Id;
        item.Click += (_, _) => OnCommandInvoked(id);
        return item;
    }

    private static Control BuildMenuHeader(AppCommand command)
    {
        if (string.IsNullOrEmpty(command.Shortcut))
        {
            return new TextBlock { Text = command.Label };
        }

        // The shortcut is shown next to the label but is not registered here: the
        // window's single KeyBinding set owns the accelerator, so the menu never
        // double-registers it.
        var label = new TextBlock { Text = command.Label };
        var shortcut = new TextBlock
        {
            Text = command.Shortcut,
            Classes = { "muted" },
            Margin = new Thickness(24, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(shortcut, 1);
        grid.Children.Add(label);
        grid.Children.Add(shortcut);
        return grid;
    }

    private sealed class CommandRelay(Action invoke) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => invoke();
    }
}
