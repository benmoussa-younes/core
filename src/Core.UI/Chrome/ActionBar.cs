// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Core.Mvp.Presentation;
using Core.UI.Adwaita;

namespace Core.UI.Chrome;

/// <summary>
/// The single contextual toolbar of the shell: it renders the active view's
/// <see cref="CommandSet"/> as flat buttons, separated into the command groups,
/// and raises the activated id back to the presenter.
/// </summary>
/// <remarks>
/// The bar is rebuilt whenever the command set changes, so switching tabs or
/// navigation pages swaps the toolbar in place. A command with an
/// <see cref="AppCommand.IconKey"/> that resolves to a glyph renders as a square
/// icon button (its label moves to the tooltip); a command without one renders as
/// a text button. A command with <see cref="AppCommand.IsChecked"/> is rendered as
/// a toggled-on button, and <see cref="AppCommand.Shortcut"/> is shown in the
/// tooltip, so the accelerator the window wires is discoverable.
/// </remarks>
internal class ActionBar : StackPanel
{
    /// <summary>Identifies the <see cref="Commands"/> property.</summary>
    public static readonly StyledProperty<CommandSet?> CommandsProperty =
        AvaloniaProperty.Register<ActionBar, CommandSet?>(nameof(Commands));

    private IReadOnlyList<AppCommand>? _rendered;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActionBar"/> class.
    /// </summary>
    public ActionBar()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 2;
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
    }

    /// <summary>Raised when a command is activated; the argument is the command id.</summary>
    public event Action<string>? CommandInvoked;

    /// <summary>
    /// Gets or sets the commands to render.
    /// </summary>
    public CommandSet? Commands
    {
        get => GetValue(CommandsProperty);
        set => SetValue(CommandsProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CommandsProperty)
        {
            Rebuild();
        }
    }

    /// <summary>
    /// Rebuilds the buttons so resolved brushes and glyphs follow the active theme.
    /// </summary>
    /// <param name="force">
    /// Rebuild even when the command sequence is unchanged, so a live theme switch
    /// re-resolves the brushes; a plain command update skips an unchanged set so a
    /// presenter that re-publishes identical commands does not churn the toolbar.
    /// </param>
    internal void Rebuild(bool force = false)
    {
        if (Commands is not { } set)
        {
            _rendered = null;
            Children.Clear();
            return;
        }

        if (!force && SameCommands(_rendered, set.Commands))
        {
            return;
        }

        _rendered = set.Commands;
        Children.Clear();

        string? group = null;

        foreach (var command in set.Commands)
        {
            if (group is not null && !string.Equals(group, command.Group, StringComparison.Ordinal))
            {
                Children.Add(new Border
                {
                    Width = 1,
                    Margin = new Thickness(4, 6),
                    Background = ThemeBrushes.Resolve("BorderBrush"),
                });
            }

            group = command.Group;
            Children.Add(CreateButton(command));
        }
    }

    private Button CreateButton(AppCommand command)
    {
        var button = new Button { IsEnabled = command.IsEnabled };

        button.Classes.Add("tool");

        if (command.IsChecked)
        {
            button.Classes.Add("active");
        }

        if (command.IconKey is { } iconKey && ThemeIcons.Resolve(iconKey) is { } geometry)
        {
            var glyph = new global::Avalonia.Controls.Shapes.Path { Data = geometry };
            glyph.Classes.Add("icon");
            button.Content = glyph;
        }
        else
        {
            button.Content = command.Label;
            button.Classes.Add("text");
        }

        var tip = command.ToolTip ?? command.Label;

        if (!string.IsNullOrEmpty(command.Shortcut))
        {
            tip = $"{tip} ({command.Shortcut})";
        }

        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => CommandInvoked?.Invoke(command.Id);

        return button;
    }

    /// <summary>
    /// Compares two command sequences by value, so an unchanged sequence can be
    /// skipped without rebuilding the toolbar or re-parsing its shortcuts.
    /// </summary>
    /// <param name="left">The rendered commands, or <see langword="null"/>.</param>
    /// <param name="right">The incoming commands, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when both hold equal commands in the same order.</returns>
    internal static bool SameCommands(IReadOnlyList<AppCommand>? left, IReadOnlyList<AppCommand>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!EqualityComparer<AppCommand>.Default.Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }
}
