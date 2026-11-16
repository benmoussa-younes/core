// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Core.UI.Chrome;

/// <summary>
/// The navigation strip of the shell: one tab per open document or sheet. It
/// carries only the identity (id and title); the shell maps the id to the view,
/// so the strip never holds a view tree.
/// </summary>
/// <remarks>
/// The strip is hidden while it has no tab, so a single-document application can
/// use the same shell without an empty row. Selecting a tab or closing one is
/// raised to the shell; the shell decides what closing means (a save prompt, for
/// example) and calls <see cref="Close"/> only once the operator is done.
/// </remarks>
internal class TabStrip : StackPanel
{
    private readonly Dictionary<string, TabEntry> _tabs = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private string? _selectedId;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabStrip"/> class.
    /// </summary>
    public TabStrip()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 2;
        IsVisible = false;
    }

    /// <summary>Raised when the operator selects a tab; the argument is the tab id.</summary>
    public event Action<string>? TabSelected;

    /// <summary>Raised when the operator presses a tab's close button; the argument is the tab id.</summary>
    public event Action<string>? TabCloseRequested;

    /// <summary>Gets the selected tab id, or <see langword="null"/> when no tab is open.</summary>
    public string? SelectedId => _selectedId;

    /// <summary>Gets the number of open tabs.</summary>
    public int TabCount => _order.Count;

    /// <summary>Gets the open tab ids, in strip order.</summary>
    public IReadOnlyList<string> TabIds => _order;

    /// <summary>Gets whether a tab is marked as having unsaved changes.</summary>
    /// <param name="id">The tab identifier.</param>
    /// <returns><see langword="true"/> when the marker is visible.</returns>
    internal bool IsDirty(string id) =>
        _tabs.TryGetValue(id, out var tab) && tab.Dirty.IsVisible;

    internal IReadOnlyList<Button> SelectButtons =>
        [.. _order.Select(id => _tabs[id].Select)];

    internal IReadOnlyList<Button> CloseButtons =>
        [.. _order.Select(id => _tabs[id].Close)];

    /// <summary>Opens a tab, or updates and selects it when it is already open.</summary>
    /// <param name="id">The stable tab id.</param>
    /// <param name="title">The tab title.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> is <see langword="null"/>.</exception>
    public void Open(string id, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(title);

        if (_tabs.TryGetValue(id, out var existing))
        {
            existing.Title.Text = title;
            AutomationProperties.SetName(existing.Select, title);
            AutomationProperties.SetName(existing.Close, $"Close {title}");
            SetSelected(id, raise: true);
            return;
        }

        var titleBlock = new TextBlock { Text = title, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
        var dirty = new TextBlock
        {
            Text = "\u25CF",
            IsVisible = false,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        };

        var titleContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        titleContent.Children.Add(dirty);
        titleContent.Children.Add(titleBlock);

        var select = new Button { Content = titleContent };
        select.Classes.Add("tab");
        AutomationProperties.SetName(select, title);
        select.Click += (_, _) => SetSelected(id, raise: true);

        var close = new Button { Content = "\u2715" };
        close.Classes.Add("tabclose");
        AutomationProperties.SetName(close, $"Close {title}");
        close.Click += (_, _) => TabCloseRequested?.Invoke(id);

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        content.Children.Add(select);
        content.Children.Add(close);

        var container = new Border { Child = content };
        container.Classes.Add("tabitem");

        _tabs[id] = new TabEntry(container, titleBlock, dirty, select, close);
        _order.Add(id);
        Children.Add(container);
        IsVisible = true;

        SetSelected(id, raise: true);
    }

    /// <summary>Marks a tab as having unsaved changes.</summary>
    /// <param name="id">The tab identifier.</param>
    /// <param name="dirty">Whether the tab carries unsaved changes.</param>
    internal void SetDirty(string id, bool dirty)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (_tabs.TryGetValue(id, out var tab))
        {
            tab.Dirty.IsVisible = dirty;
        }
    }

    /// <summary>Closes a tab; a new selection is raised when the closed tab was selected.</summary>
    /// <param name="id">The tab id.</param>
    public void Close(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (!_tabs.Remove(id, out var entry))
        {
            return;
        }

        Children.Remove(entry.Container);
        _order.Remove(id);
        IsVisible = _order.Count > 0;

        if (string.Equals(_selectedId, id, StringComparison.Ordinal))
        {
            var next = _order.Count > 0 ? _order[^1] : null;
            SetSelected(next, raise: next is not null);
        }
    }

    private void SetSelected(string? id, bool raise)
    {
        _selectedId = id;

        foreach (var (key, tab) in _tabs)
        {
            if (string.Equals(key, id, StringComparison.Ordinal))
            {
                tab.Container.Classes.Add("active");
            }
            else
            {
                tab.Container.Classes.Remove("active");
            }
        }

        if (raise && id is not null)
        {
            TabSelected?.Invoke(id);
        }
    }

    private sealed record TabEntry(Border Container, TextBlock Title, TextBlock Dirty, Button Select, Button Close);
}
