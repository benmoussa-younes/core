// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Core.Mvp.Presentation;
using Core.Mvp.Services;

namespace Core.UI.Tray;

/// <summary>
/// The tray presence of the shell: a <see cref="TrayIcon"/> whose menu is built
/// from the same <see cref="CommandSet"/> the toolbar renders, so a tray entry
/// and a chrome entry cannot drift.
/// </summary>
public sealed class TrayService : ITrayService, IDisposable
{
    private readonly WindowIcon? _icon;
    private Action<string> _activated;
    private TrayIcon? _tray;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayService"/> class.
    /// </summary>
    /// <param name="icon">The tray icon, or <see langword="null"/> when none is available.</param>
    /// <param name="activated">Receives the id of an activated tray command.</param>
    /// <exception cref="ArgumentNullException"><paramref name="activated"/> is <see langword="null"/>.</exception>
    public TrayService(WindowIcon? icon, Action<string> activated)
    {
        ArgumentNullException.ThrowIfNull(activated);

        _icon = icon;
        _activated = activated;
    }

    /// <summary>
    /// Gets or sets the handler that receives an activated tray command id. It is
    /// settable after construction, so a product can wire the dispatcher once the
    /// content exists instead of wrapping the service.
    /// </summary>
    public Action<string> Activated
    {
        get => _activated;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _activated = value;
        }
    }

    /// <inheritdoc />
    public void Show(string toolTip, CommandSet menu)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(toolTip);
        ArgumentNullException.ThrowIfNull(menu);

        var tray = _tray ??= new TrayIcon();

        tray.Icon = _icon;
        tray.ToolTipText = toolTip;
        tray.Menu = BuildMenu(menu, id => _activated(id));
        tray.IsVisible = true;

        Register(tray);
    }

    /// <inheritdoc />
    public void Hide()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (_tray is not null)
        {
            _tray.IsVisible = false;
        }
    }

    /// <summary>
    /// Removes and disposes this service's tray icon, leaving any other icon the
    /// application registered in place.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (_tray is null)
        {
            return;
        }

        _tray.IsVisible = false;
        Unregister(_tray);
        _tray.Dispose();
        _tray = null;
    }

    internal static NativeMenu BuildMenu(CommandSet menu, Action<string> activated)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(activated);

        var native = new NativeMenu();
        string? group = null;

        foreach (var command in menu.Commands)
        {
            if (group is not null && !string.Equals(group, command.Group, StringComparison.Ordinal))
            {
                native.Items.Add(new NativeMenuItemSeparator());
            }

            group = command.Group;

            var item = new NativeMenuItem
            {
                Header = command.Label,
                IsEnabled = command.IsEnabled,
                IsChecked = command.IsChecked,
            };

            var id = command.Id;
            item.Command = new TrayCommand(command.IsEnabled, () => activated(id));
            native.Items.Add(item);
        }

        return native;
    }

    private static void Register(TrayIcon tray)
    {
        if (Application.Current is { } application)
        {
            var icons = TrayIcon.GetIcons(application);

            if (icons is null)
            {
                TrayIcon.SetIcons(application, new TrayIcons { tray });
            }
            else if (!icons.Contains(tray))
            {
                // Add to the application's set instead of replacing it, so another
                // tray icon the application registered is not clobbered.
                icons.Add(tray);
            }
        }
    }

    private static void Unregister(TrayIcon tray)
    {
        if (Application.Current is { } application
            && TrayIcon.GetIcons(application) is { } icons)
        {
            icons.Remove(tray);
        }
    }

    /// <summary>A minimal command that runs one action, so a tray item is drivable in a test.</summary>
    internal sealed class TrayCommand : System.Windows.Input.ICommand
    {
        private readonly bool _enabled;
        private readonly Action _action;

        public TrayCommand(bool enabled, Action action)
        {
            _enabled = enabled;
            _action = action;
        }

        /// <inheritdoc />
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public bool CanExecute(object? parameter)
        {
            return _enabled;
        }

        /// <inheritdoc />
        public void Execute(object? parameter)
        {
            _action();
        }
    }
}
