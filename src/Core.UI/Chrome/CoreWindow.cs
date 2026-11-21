// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Core.Mvp.Composition;
using Core.Mvp.Models;
using Core.Mvp.Presentation;
using Core.Mvp.Presenters;
using Core.Diagnostics;
using Core.Mvp.Services;
using Core.Security;
using Core.Mvp.Views;
using Core.UI.Hosting;
using Core.UI.Notifications;
using Core.UI.Dialogs;
using Core.UI.Platform;
using Core.UI.Adwaita;

namespace Core.UI.Chrome;

/// <summary>
/// The application shell: a client-side-decorated window whose fixed chrome is a
/// header bar, an optional tab strip and the single contextual toolbar, over the
/// active view's content, with the dialog, notification and tray services.
/// </summary>
/// <remarks>
/// There is no status bar: transient feedback is a toast and persistent feedback
/// is a banner, both owned by <see cref="Notifications"/>. A view publishes a
/// <see cref="ShellSnapshot"/> (title, subtitle, commands, busy) and receives
/// command activations back as <see cref="CommandInvoked"/>. Content enters
/// through <c>OpenTab</c> (tabs) or <c>Push</c> (a stack); the overload that
/// takes an <see cref="IViewPresenter"/> binds the presenter's initialize,
/// activate, deactivate and dispose lifecycle to the entry. An application that
/// uses only the stack shows no tab strip, which is the content-only mode. A
/// product sets <see cref="Window.Icon"/> directly on this window; the shell
/// does not override it.
/// </remarks>
public class CoreWindow : Window, IApplicationHost, INavigationHost, IDisposable
{
    private readonly HeaderBar _header = new();
    private readonly TabStrip _tabs = new();
    private readonly ActionBar _actions = new();
    private readonly ContentPresenter _content = new();
    private readonly NotificationHost _notifications = new();
    private readonly ConfirmDialog _dialog;
    private readonly PromptDialog _prompt;
    private readonly Button _back = new() { Content = "\u2190", IsVisible = false };
    private readonly Button _menu = new() { Content = "\u22EE" };
    private readonly WindowControls _windowControls = new();
    private readonly Border _toolbar;
    private readonly Border _busy;
    private readonly ProgressBar _busyProgress;
    private readonly IClipboardService _clipboard;
    private readonly DialogGate _dialogGate;
    private readonly Panel _contentRegion;
    private readonly Grid _regions;
    private readonly Dictionary<IView, NavigationEntry> _entries = [];
    private readonly Dictionary<string, NavigationEntry> _tabsById = new(StringComparer.Ordinal);
    private readonly Dictionary<IViewPresenter, Task> _lifecycle = [];
    private readonly Dictionary<IViewPresenter, CancellationTokenSource> _presenterTokens = [];
    private readonly HashSet<IViewPresenter> _activated = [];
    private readonly HashSet<IViewPresenter> _pendingActivation = [];
    private readonly HashSet<IViewPresenter> _initializedPresenters = [];
    private readonly ConditionalWeakTable<IViewPresenter, object?> _closedPresenters = new();
    private readonly List<NavigationEntry> _stack = [];
    private readonly ShellMenuBinder _menuBinder;
    private NavigationEntry? _activeEntry;
    private bool _snapshotCanGoBack;
    private bool _forceClose;
    private bool _closeInProgress;
    private bool _quitInProgress;
    private IInputElement? _previousFocus;
    private int _modalDepth;
    private Task? _shutdownTask;
    private readonly object _shutdownGate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CoreWindow"/> class.
    /// </summary>
    /// <param name="tray">The tray service.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tray"/> is <see langword="null"/>.</exception>
    public CoreWindow(ITrayService tray)
    {
        ArgumentNullException.ThrowIfNull(tray);

        Tray = tray;
        _dialog = new ConfirmDialog(BeginModal, EndModal);
        _prompt = new PromptDialog(BeginModal, EndModal);

        _dialogGate = new DialogGate();
        Dialogs = new DialogService(_dialog, _dialogGate);
        Prompts = new PromptService(_prompt, _dialogGate);
        Notifications = new NotificationService(_notifications);

        var picker = new StorageProviderFilePicker(StorageProvider);
        Files = picker;
        Folders = picker;

        _clipboard = new TopLevelClipboardService(Clipboard);

        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = GnomeMetrics.HeaderBarHeight;
        Background = ThemeBrushes.Resolve("BackgroundBrush");

        _back.Classes.Add("tool");
        ToolTip.SetTip(_back, "Back");
        AutomationProperties.SetName(_back, "Back");
        _back.Click += (_, _) => RequestBack();

        _menu.Classes.Add("flat");
        ToolTip.SetTip(_menu, "Main menu");
        AutomationProperties.SetName(_menu, "Main menu");
        _menu.Flyout = PrimaryMenu;

        _windowControls.CloseRequested += (_, _) => RequestClose();

        var end = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = GnomeMetrics.HeaderBarSpacing,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        };
        end.Children.Add(_menu);
        end.Children.Add(_windowControls);

        _header.StartContent = _back;
        _header.EndContent = end;

        _toolbar = new Border
        {
            Padding = new Thickness(GnomeMetrics.HeaderBarHorizontalPadding, 2),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = ThemeBrushes.Resolve("BorderBrush"),
            Child = _actions,
        };

        _regions = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };

        _regions.Children.Add(_header);
        _regions.Children.Add(_tabs);
        _regions.Children.Add(_toolbar);
        Grid.SetRow(_header, 0);
        Grid.SetRow(_tabs, 1);
        Grid.SetRow(_toolbar, 2);

        _busyProgress = new ProgressBar
        {
            IsIndeterminate = true,
            Width = 240,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        };

        _busy = new Border
        {
            IsVisible = false,
            Background = ThemeBrushes.Resolve("ScrimBrush"),
            Classes = { "busy" },
            Child = _busyProgress,
        };

        // The busy overlay is confined to the content region: the chrome (primary
        // menu, window controls, toolbar) stays interactive, so an application that
        // is stuck busy can still be quit. The banner sits under the toolbar and the
        // toasts float above the busy scrim, so feedback is not hidden by it.
        _contentRegion = new Panel();
        _contentRegion.Children.Add(_content);
        _contentRegion.Children.Add(_busy);
        _contentRegion.Children.Add(_notifications);
        Grid.SetRow(_contentRegion, 3);
        _regions.Children.Add(_contentRegion);

        // The dialog is the only full-window overlay: it is modal, and it is
        // dismissible through its own Cancel.
        var root = new Panel();
        root.Children.Add(_regions);
        root.Children.Add(new ResizeGrips());
        root.Children.Add(_dialog);
        root.Children.Add(_prompt);

        Content = root;

        _actions.CommandInvoked += OnCommandInvoked;
        _tabs.TabSelected += SelectTab;
        _tabs.TabCloseRequested += RequestTabClose;

        // The code-composed chrome resolves its brushes once, so a live theme
        // switch (the system appearance changing) must re-apply them.
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        Closing += OnClosing;

        _menuBinder = new ShellMenuBinder(this);
    }

    /// <inheritdoc />
    public IDialogService Dialogs { get; }

    /// <inheritdoc />
    public IPromptService Prompts { get; }

    /// <inheritdoc />
    public INotificationService Notifications { get; }

    /// <inheritdoc />
    public IFilePicker Files { get; }

    /// <inheritdoc />
    public IFolderPicker Folders { get; }

    /// <inheritdoc />
    IClipboardService IViewHost.Clipboard => _clipboard;

    /// <inheritdoc />
    public ITrayService Tray { get; }

    /// <inheritdoc />
    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.Quit;

    /// <inheritdoc />
    public INavigationHost Navigation => this;

    /// <inheritdoc />
    public IView? Active => _activeEntry?.View;

    /// <summary>Gets the snapshot last applied by the active view, or <see langword="null"/> before the first one.</summary>
    /// <remarks>A view composes an update from it (for example <c>CurrentSnapshot with { Busy = true }</c>) without tracking its own title.</remarks>
    public ShellSnapshot? CurrentSnapshot { get; private set; }

    /// <summary>
    /// Gets the primary menu of the header bar. The shell owns its standard tail
    /// (About, Preferences, Quit); a product publishes its own commands through
    /// <see cref="SetApplicationMenu"/> instead of mutating this flyout directly.
    /// </summary>
    public MenuFlyout PrimaryMenu { get; } = new();

    /// <summary>
    /// Gets or sets the icon shown in the header bar. The shell uses client-side
    /// decorations and draws its own title bar, so a product sets this as well as
    /// <see cref="Window.Icon"/> (the OS icon used by the task switcher).
    /// </summary>
    public IImage? HeaderIcon
    {
        get => _header.Icon;
        set => _header.Icon = value;
    }

    /// <summary>
    /// Raised when the operator asks to close a tab, before the shell closes it. A
    /// handler sets <see cref="TabCloseRequestedEventArgs.Cancel"/> to keep the tab
    /// open while it prompts the operator (for example to save unsaved changes).
    /// </summary>
    public event EventHandler<TabCloseRequestedEventArgs>? TabCloseRequested;

    /// <summary>
    /// Raised when the operator presses Back and the navigation stack is empty.
    /// A presenter that publishes <see cref="ShellSnapshot.CanGoBack"/> for its own
    /// document history handles this event instead of leaving a dead button.
    /// </summary>
    public event Action? BackNavigationRequested;

    /// <summary>Raised when the application is activated from outside; explicit, so it does not hide the window's own activation event.</summary>
    private event Action? ApplicationActivated;

    /// <inheritdoc />
    event Action? IApplicationHost.Activated
    {
        add => ApplicationActivated += value;
        remove => ApplicationActivated -= value;
    }

    /// <inheritdoc />
    public event Action<IView, string>? CommandInvoked;

    /// <summary>
    /// Raised by <see cref="Quit"/> and <see cref="QuitAsync"/>; the application
    /// base performs the shutdown. The handler returns a value task, so the shell
    /// can await a flush before its presenters are disposed and before
    /// <see cref="QuitAsync"/> completes.
    /// </summary>
    public event Func<ValueTask>? QuitRequested;

    /// <summary>
    /// Gets or sets the guard the shell asks before closing the window (the close
    /// button or the window manager's close). It returns <see langword="true"/> to
    /// close, the false to keep the window open — for example to prompt to save.
    /// <see cref="Quit"/> bypasses it.
    /// </summary>
    public Func<ValueTask<bool>>? ConfirmClose { get; set; }

    /// <summary>
    /// Gets or sets the guard the shell asks before quitting when the operator
    /// chose Quit. It returns <see langword="true"/> to quit, or <see langword="false"/>
    /// to keep the application open — for example to prompt to save. When it is
    /// <see langword="null"/>, <see cref="Quit"/> is immediate.
    /// </summary>
    public Func<ValueTask<bool>>? ConfirmQuit { get; set; }

    internal HeaderBar Header => _header;

    internal NotificationHost NotificationsHost => _notifications;

    internal ActionBar Actions => _actions;

    internal TabStrip Tabs => _tabs;

    internal ContentPresenter ContentHost => _content;

    internal Panel ContentRegion => _contentRegion;

    internal Border BusyOverlay => _busy;

    internal Button BackButton => _back;

    internal bool IsBusyVisible => _busy.IsVisible;

    /// <inheritdoc />
    public void SetSnapshot(ShellSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RequireCommands(snapshot);

        CurrentSnapshot = snapshot;

        if (_activeEntry is { } active)
        {
            active.Snapshot = snapshot;
        }

        Apply(snapshot);
    }

    /// <summary>
    /// Publishes a snapshot for the view a presenter owns, without assuming that
    /// the presenter is the active one. A background presenter must use this
    /// overload; the parameterless tab snapshot stays the active-tab shorthand.
    /// </summary>
    /// <param name="presenter">The presenter that owns the view.</param>
    /// <param name="snapshot">The snapshot to store for that view.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The presenter is not bound to this window.</exception>
    public void SetSnapshot(IViewPresenter presenter, ShellSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(snapshot);
        RequireCommands(snapshot);

        // A completion that races the tab close is normal: the presenter is no
        // longer bound, and its last snapshot must not resurrect the entry. Keep
        // the exception for a presenter that was never bound at all.
        if (_closedPresenters.TryGetValue(presenter, out _))
        {
            return;
        }

        var entry = FindEntryForPresenter(presenter);

        if (entry is null)
        {
            throw new ArgumentException(
                "The presenter is not bound to this window.",
                nameof(presenter));
        }

        entry.Snapshot = snapshot;
        MarkDirty(entry.View, snapshot.IsDirty);

        if (ReferenceEquals(_activeEntry, entry))
        {
            CurrentSnapshot = snapshot;
            Apply(snapshot);
        }
    }

    /// <inheritdoc />
    public void SetApplicationMenu(CommandSet menu) => _menuBinder.SetApplicationMenu(menu);

    /// <summary>
    /// Marks a tab as having unsaved changes. A presenter normally publishes it
    /// through <see cref="ShellSnapshot.IsDirty"/>; this setter is for a host that
    /// drives the tabs without a presenter.
    /// </summary>
    /// <param name="id">The tab identifier.</param>
    /// <param name="dirty">Whether the tab carries unsaved changes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
    public void SetTabDirty(string id, bool dirty)
    {
        ArgumentNullException.ThrowIfNull(id);

        _tabs.SetDirty(id, dirty);
    }

    /// <summary>
    /// Sets the About identity; when it is set the shell appends About… to the
    /// primary menu and shows the shared dialog itself. Pass <see langword="null"/>
    /// to remove it.
    /// </summary>
    /// <param name="info">The product identity, or <see langword="null"/>.</param>
    public void SetAbout(AboutInfo? info) => _menuBinder.SetAbout(info);

    /// <summary>
    /// Sets the appearance service; when it is set the shell appends Preferences… to
    /// the primary menu and shows the shared dialog itself. Pass <see langword="null"/>
    /// to remove it.
    /// </summary>
    /// <param name="service">The appearance service, or <see langword="null"/>.</param>
    public void SetAppearance(IAppearanceService? service) => _menuBinder.SetAppearance(service);

    /// <inheritdoc />
    /// <remarks>
    /// The <paramref name="view"/> must be an Avalonia <see cref="Control"/> as
    /// well as an <see cref="IView"/>, because the shell shows it in a content
    /// presenter; a view that is not a control cannot be rendered.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="view"/> is not an Avalonia <see cref="Control"/>.</exception>
    public void OpenTab(string id, string title, IView view)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(view);
        RequireControl(view);

        if (_entries.TryGetValue(view, out var existingEntry) && existingEntry.TabId is null)
        {
            throw new InvalidOperationException(
                "The view is already in the navigation stack; close it before opening it as a tab.");
        }

        if (existingEntry is { TabId: { } boundId } &&
            !string.Equals(boundId, id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A view cannot be shown in two tabs; create a separate view per tab.");
        }

        if (_tabsById.TryGetValue(id, out var previous) && !ReferenceEquals(previous.View, view))
        {
            if (previous.Presenter is not null)
            {
                throw new InvalidOperationException(
                    "The tab id is bound to a presenter; close it before binding a different view.");
            }

            RemoveEntry(previous);
        }

        var entry = existingEntry ?? new NavigationEntry(view, id);
        _entries[view] = entry;
        _tabsById[id] = entry;
        _tabs.Open(id, title);
    }

    /// <inheritdoc />
    public void OpenTab(string id, string title, IViewPresenter presenter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(presenter);
        RequireControl(presenter.View);

        if (FindTabId(presenter) is { } otherId && !string.Equals(otherId, id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A presenter cannot be bound to two tabs; create a separate presenter per tab.");
        }

        if (_entries.TryGetValue(presenter.View, out var viewEntry))
        {
            if (viewEntry.TabId is null)
            {
                throw new InvalidOperationException(
                    "The view is already in the navigation stack; close it before opening it as a tab.");
            }

            if (!string.Equals(viewEntry.TabId, id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A view cannot be shown in two tabs; create a separate view per tab.");
            }
        }

        var previousPresenter = viewEntry?.Presenter;

        if (_tabsById.TryGetValue(id, out var previous) && !ReferenceEquals(previous.Presenter, presenter))
        {
            if (previous.Presenter is { } oldPresenter)
            {
                previous.Presenter = null;
                ClosePresenter(oldPresenter);
            }

            if (!ReferenceEquals(previous.View, presenter.View))
            {
                RemoveEntry(previous);
            }
        }

        var entry = viewEntry ?? new NavigationEntry(presenter.View, id);
        var wasActive = ReferenceEquals(_activeEntry, entry);
        entry.Presenter = presenter;
        _entries[presenter.View] = entry;
        _tabsById[id] = entry;
        BindPresenter(presenter);
        _tabs.Open(id, title);

        // Replacing the presenter of the active tab keeps the same view, so the
        // TabSelected event does not reach Activate (same entry). Start the new
        // presenter explicitly; the old one was closed above. A first open is not
        // active before Open and is activated by the TabSelected path.
        if (wasActive && !ReferenceEquals(previousPresenter, presenter) && ReferenceEquals(_activeEntry, entry))
        {
            entry.Busy = presenter.IsBusy;
            ActivatePresenter(presenter);
        }
    }

    /// <inheritdoc />
    public void CloseTab(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (_tabsById.TryGetValue(id, out var entry))
        {
            if (entry.View is IBusyView busyView)
            {
                busyView.BusyChanged -= OnViewBusyChanged;
            }

            RemoveEntry(entry);

            if (entry.Presenter is { } presenter)
            {
                entry.Presenter = null;
                ClosePresenter(presenter);
            }

            _tabs.Close(id);
        }

        if (_tabsById.Count == 0)
        {
            var pending = ClearStack();

            // The active view can be a pushed page rather than a tab. Closing the
            // last tab must not leave that page displayed without a tab or a way
            // back; close it with the rest of the stack.
            if (_activeEntry is { TabId: null } active)
            {
                _entries.Remove(active.View);
                if (active.Presenter is { } activePresenter)
                {
                    pending.Add(ClosePresenter(activePresenter));
                }
            }

            _ = pending;
            ClearNavigation();
            _menuBinder.RebuildBindings(viewCommands: null, force: true);
        }

        UpdateBack();
        UpdateBusy();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The <paramref name="view"/> must be an Avalonia <see cref="Control"/> as
    /// well as an <see cref="IView"/>, because the shell shows it in a content
    /// presenter; a view that is not a control cannot be rendered.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="view"/> is not an Avalonia <see cref="Control"/>.</exception>
    public void Push(string title, string subtitle, IView view)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(subtitle);
        ArgumentNullException.ThrowIfNull(view);
        RequireControl(view);

        if (_entries.TryGetValue(view, out var existing))
        {
            throw new InvalidOperationException(
                existing.TabId is not null
                    ? "A tab view cannot also be pushed onto the navigation stack."
                    : "The view is already in the navigation stack.");
        }

        // The pushed page's title is part of the navigation state: a plain view
        // has no presenter to publish a snapshot, so Pop must not restore an
        // empty header. A presenter's own snapshot replaces this default.
        var entry = new NavigationEntry(view, tabId: null)
        {
            Snapshot = new ShellSnapshot(title, subtitle, CommandSet.Empty),
        };

        _entries[view] = entry;

        if (_activeEntry is not null)
        {
            _stack.Add(_activeEntry);
        }

        Activate(entry);
        _header.Title = title;
        _header.Subtitle = subtitle;
        UpdateBack();
    }

    /// <inheritdoc />
    public void Push(string title, string subtitle, IViewPresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(subtitle);
        ArgumentNullException.ThrowIfNull(presenter);
        RequireControl(presenter.View);

        if (FindTabId(presenter) is not null)
        {
            throw new InvalidOperationException(
                "A tab presenter cannot also be pushed onto the navigation stack.");
        }

        if (_entries.ContainsKey(presenter.View))
        {
            throw new InvalidOperationException("The presenter is already in the navigation stack.");
        }

        var entry = new NavigationEntry(presenter.View, tabId: null)
        {
            Presenter = presenter,
            Snapshot = new ShellSnapshot(title, subtitle, CommandSet.Empty),
        };

        _entries[presenter.View] = entry;
        BindPresenter(presenter);

        if (_activeEntry is not null)
        {
            _stack.Add(_activeEntry);
        }

        Activate(entry);
        _header.Title = title;
        _header.Subtitle = subtitle;
        UpdateBack();
    }

    /// <inheritdoc />
    public void Pop()
    {
        if (_stack.Count == 0)
        {
            return;
        }

        var previous = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        Activate(previous, disposePrevious: true);
    }

    private void RequestBack()
    {
        if (_stack.Count > 0)
        {
            Pop();
            return;
        }

        BackNavigationRequested?.Invoke();
    }

    /// <inheritdoc />
    public ValueTask WhenIdleAsync()
    {
        var pending = _lifecycle.Values.ToArray();

        return pending.Length == 0
            ? ValueTask.CompletedTask
            : new ValueTask(Task.WhenAll(pending));
    }

    /// <inheritdoc />
    public void ShowWindow()
    {
        Show();
        Activate();
        RaiseActivated();
    }

    /// <inheritdoc />
    public void HideWindow()
    {
        Hide();
    }

    /// <summary>Captures the window's current placement (position, size, maximised).</summary>
    /// <returns>The placement.</returns>
    /// <remarks>
    /// An unmeasured window reports a <see cref="double.NaN"/> size, which JSON
    /// cannot serialize; it is captured as zero, and <see cref="ApplyPlacement"/>
    /// ignores a zero size.
    /// </remarks>
    public WindowPlacement CapturePlacement() =>
        new(
            Position.X,
            Position.Y,
            double.IsFinite(Width) ? Width : 0,
            double.IsFinite(Height) ? Height : 0,
            WindowState == WindowState.Maximized);

    /// <summary>
    /// Applies a saved placement. A non-positive size is ignored, so a file written
    /// before the window had a size never collapses it.
    /// </summary>
    /// <param name="placement">The placement.</param>
    /// <exception cref="ArgumentNullException"><paramref name="placement"/> is <see langword="null"/>.</exception>
    public void ApplyPlacement(WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);

        // A placement is data from a file on disk: NaN compares false against
        // every bound, so `<= 0` alone would let it through, and a size far past
        // the int range wraps on the pixel conversion below.
        if (!double.IsFinite(placement.Width) ||
            !double.IsFinite(placement.Height) ||
            placement.Width <= 0 ||
            placement.Height <= 0)
        {
            return;
        }

        var width = Math.Min(placement.Width, MaxPlacementPixels);
        var height = Math.Min(placement.Height, MaxPlacementPixels);
        var x = double.IsFinite(placement.X) ? placement.X : 0;
        var y = double.IsFinite(placement.Y) ? placement.Y : 0;

        // A saved placement can name a monitor that is no longer attached. Bring
        // the window back onto a visible working area instead of opening it
        // off-screen, where the operator cannot reach it.
        var screens = Screens.All;

        if (screens.Count > 0)
        {
            var requested = new PixelRect(ToPixel(x), ToPixel(y), ToPixel(width), ToPixel(height));
            var target = screens.FirstOrDefault(screen => Intersects(screen.WorkingArea, requested))
                ?? Screens.Primary
                ?? screens[0];
            var working = target.WorkingArea;

            width = Math.Min(width, working.Width);
            height = Math.Min(height, working.Height);
            x = Math.Clamp(x, working.X, working.Right - width);
            y = Math.Clamp(y, working.Y, working.Bottom - height);
        }

        Width = width;
        Height = height;
        Position = new PixelPoint(ToPixel(x), ToPixel(y));
        WindowState = placement.Maximized ? WindowState.Maximized : WindowState.Normal;
    }

    // No real window is wider than this, and staying inside the int range keeps
    // the pixel conversion total when no screen is attached to clamp against.
    private const double MaxPlacementPixels = 100_000;

    private static int ToPixel(double value) =>
        (int)Math.Clamp(value, -MaxPlacementPixels, MaxPlacementPixels);

    private static bool Intersects(PixelRect left, PixelRect right) =>
        left.X < right.Right &&
        right.X < left.Right &&
        left.Y < right.Bottom &&
        right.Y < left.Bottom;

    /// <summary>
    /// Releases the dialog gate and closes the window. The asynchronous presenter
    /// shutdown remains <see cref="ShutdownAsync"/>; a product awaits that from its
    /// quit path and uses this only as the last-resort disposal of the window.
    /// </summary>
    public void Dispose()
    {
        _dialogGate.Dispose();
        Close();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public void Quit() => _ = QuitAsync().AsTask();

    /// <summary>
    /// Quits the application, awaiting the confirmation guard, every
    /// <see cref="QuitRequested"/> handler and the presenter shutdown.
    /// </summary>
    /// <remarks>
    /// Quit is the operator's explicit choice (the menu item or the tray), so it
    /// does not ask the close guard: the guard is for an accidental close. It may
    /// still ask <see cref="ConfirmQuit"/>, so a save prompt can cancel the quit.
    /// A hidden tray window is shown before the prompt, because the confirmation
    /// overlay is a child of this window and cannot be reached while it is hidden.
    /// </remarks>
    /// <returns>A value task that completes when the quit has finished or was cancelled.</returns>
    public async ValueTask QuitAsync()
    {
        if (_forceClose)
        {
            await ShutdownAsync().ConfigureAwait(true);
            return;
        }

        if (_quitInProgress)
        {
            return;
        }

        _quitInProgress = true;

        try
        {
            if (ConfirmQuit is { } confirm)
            {
                // The confirmation overlay is a child of this window; a hidden
                // tray window would show an unreachable dialog. Surface it only
                // when a guard actually needs to ask.
                if (!IsVisible)
                {
                    ShowWindow();
                }

                if (!await confirm().ConfigureAwait(true))
                {
                    return;
                }
            }

            await ForceQuitAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            ReportLifecycleFailure("quit", exception);
        }
        finally
        {
            _quitInProgress = false;
        }
    }

    private async ValueTask ForceQuitAsync()
    {
        _forceClose = true;

        if (QuitRequested is { } handlers)
        {
            foreach (var handler in handlers.GetInvocationList().Cast<Func<ValueTask>>())
            {
                try
                {
                    await handler().ConfigureAwait(true);
                }
                catch (Exception exception)
                {
                    // One failing flush must not prevent the remaining handlers or
                    // the presenter shutdown from running.
                    ReportLifecycleFailure("quit", exception);
                }
            }
        }

        await ShutdownAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Deactivates and disposes every presenter, waits for the lifecycle queue to
    /// drain, then leaves the window ready to close. It is idempotent; a product
    /// that owns the quit path awaits it before stopping the application.
    /// </summary>
    /// <returns>A value task that completes when every presenter has been disposed.</returns>
    public ValueTask ShutdownAsync()
    {
        lock (_shutdownGate)
        {
            return new ValueTask(_shutdownTask ??= ShutdownCoreAsync());
        }
    }

    private async Task ShutdownCoreAsync()
    {
        var pending = ClearStack();
        var presenters = _entries.Values
            .Select(entry => entry.Presenter)
            .Where(presenter => presenter is not null)
            .Distinct()
            .ToArray();

        foreach (var presenter in presenters)
        {
            pending.Add(ClosePresenter(presenter!));
        }

        if (pending.Count > 0)
        {
            await Task.WhenAll(pending).ConfigureAwait(true);
        }

        foreach (var entry in _entries.Values)
        {
            if (entry.View is IBusyView busyView)
            {
                busyView.BusyChanged -= OnViewBusyChanged;
            }
        }

        _tabsById.Clear();
        _entries.Clear();
        _presenterTokens.Clear();
        _activated.Clear();
        _pendingActivation.Clear();
        _initializedPresenters.Clear();
        _closedPresenters.Clear();
        _stack.Clear();
        ClearNavigation();
    }

    /// <summary>
    /// Raises <see cref="IApplicationHost.Activated"/> after <see cref="ShowWindow"/>
    /// surfaces the window, so a tray activation or a second launch that the product
    /// routes to <see cref="ShowWindow"/> also reaches the activation listeners.
    /// </summary>
    internal void RaiseActivated()
    {
        ApplicationActivated?.Invoke();
    }

    internal void RequestClose()
    {
        if (CloseBehavior == CloseBehavior.HideToTray)
        {
            Hide();
            return;
        }

        Close();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose)
        {
            return;
        }

        // A tray product hides on the window manager's close button (Alt+F4,
        // compositor close) just like on the shell's close button. A programmatic
        // Close() and an application shutdown still proceed, so a product that
        // owns the lifetime is not trapped.
        if (CloseBehavior == CloseBehavior.HideToTray
            && !e.IsProgrammatic
            && e.CloseReason is not WindowCloseReason.ApplicationShutdown
                and not WindowCloseReason.OSShutdown)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        e.Cancel = true;

        if (!_closeInProgress)
        {
            _ = CloseWithShutdownAsync(ConfirmClose);
        }
    }

    private async Task CloseWithShutdownAsync(Func<ValueTask<bool>>? confirm)
    {
        _closeInProgress = true;

        try
        {
            if (confirm is not null && !await confirm().ConfigureAwait(true))
            {
                _closeInProgress = false;
                return;
            }

            await ShutdownAsync().ConfigureAwait(true);
            _forceClose = true;
            Close();
        }
        catch (Exception exception)
        {
            _closeInProgress = false;
            ReportLifecycleFailure("close", exception);
        }
    }

    /// <summary>
    /// Re-resolves the brushes the code-composed chrome set once, so a live theme
    /// switch is reflected without rebuilding the window.
    /// </summary>
    internal void ApplyTheme()
    {
        Background = ThemeBrushes.Resolve("BackgroundBrush");
        _toolbar.BorderBrush = ThemeBrushes.Resolve("BorderBrush");
        _busy.Background = ThemeBrushes.Resolve("ScrimBrush");
        _header.ApplyTheme();
        _windowControls.ApplyTheme();
        _notifications.ApplyTheme();
        _dialog.ApplyTheme();
        _prompt.ApplyTheme();
        _actions.Rebuild(force: true);
    }

    private void BeginModal(Control dialog)
    {
        if (_modalDepth++ > 0)
        {
            return;
        }

        _previousFocus = FocusManager?.GetFocusedElement();
        _regions.IsEnabled = false;
        _menuBinder.SuspendBindings();
    }

    private void EndModal()
    {
        if (_modalDepth == 0 || --_modalDepth > 0)
        {
            return;
        }

        _regions.IsEnabled = true;
        _menuBinder.ResumeBindings();

        if (_previousFocus is Control previous)
        {
            previous.Focus();
        }

        _previousFocus = null;
    }

    private void SelectTab(string id)
    {
        var pending = ClearStack();

        // The active pushed page is not on the tab strip; switching tabs abandons
        // the stack, so dispose it rather than leave an unreachable presenter.
        if (_activeEntry is { TabId: null } active)
        {
            if (active.View is IBusyView activeBusy)
            {
                activeBusy.BusyChanged -= OnViewBusyChanged;
            }

            _entries.Remove(active.View);

            if (active.Presenter is { } presenter)
            {
                pending.Add(ClosePresenter(presenter));
            }

            _activeEntry = null;
        }

        _ = pending;

        if (_tabsById.TryGetValue(id, out var tab))
        {
            Activate(tab);
        }
    }

    private List<Task> ClearStack()
    {
        var pending = new List<Task>();

        if (_stack.Count == 0)
        {
            return pending;
        }

        var stacked = _stack.ToArray();
        _stack.Clear();

        foreach (var entry in stacked)
        {
            // A tab can be the previous page of a pushed detail. Abandoning the
            // stack does not close that tab; only stack-only pages are disposed.
            if (entry.TabId is not null)
            {
                continue;
            }

            _entries.Remove(entry.View);

            if (entry.Presenter is { } presenter)
            {
                pending.Add(ClosePresenter(presenter));
            }
        }

        return pending;
    }

    private void RequestTabClose(string id)
    {
        var args = new TabCloseRequestedEventArgs(id);
        TabCloseRequested?.Invoke(this, args);

        if (!args.Cancel)
        {
            CloseTab(id);
        }
    }

    private void Activate(NavigationEntry entry, bool disposePrevious = false)
    {
        if (ReferenceEquals(_activeEntry, entry))
        {
            return;
        }

        if (_activeEntry is { } previous)
        {
            if (previous.View is IBusyView previousBusy)
            {
                previousBusy.BusyChanged -= OnViewBusyChanged;
            }

            if (previous.Presenter is { } previousPresenter)
            {
                if (disposePrevious)
                {
                    previous.Presenter = null;
                    ClosePresenter(previousPresenter);
                }
                else
                {
                    DeactivatePresenter(previousPresenter);
                }
            }

            if (disposePrevious && previous.TabId is null)
            {
                _entries.Remove(previous.View);
            }
        }

        _activeEntry = entry;
        _content.Content = entry.View as Control;

        if (entry.View is IBusyView busyView)
        {
            busyView.BusyChanged += OnViewBusyChanged;
        }

        if (entry.Presenter is { } presenter)
        {
            // A completion that happened while the tab was in the background is
            // not delivered (the event is unsubscribed), so read the state back.
            entry.Busy = presenter.IsBusy;
            ActivatePresenter(presenter);
        }

        if (entry.Snapshot is { } snapshot)
        {
            CurrentSnapshot = snapshot;
            Apply(snapshot);
            return;
        }

        // No snapshot for this view: clear the previous tab's chrome instead of
        // leaking its title, commands, back flag and busy overlay.
        ClearChrome();
        _menuBinder.RebuildBindings(viewCommands: null, force: true);
        UpdateBack();
        UpdateBusy();
    }

    private void RemoveEntry(NavigationEntry entry)
    {
        _entries.Remove(entry.View);
        _stack.Remove(entry);

        if (entry.TabId is { } id &&
            _tabsById.TryGetValue(id, out var current) &&
            ReferenceEquals(current, entry))
        {
            _tabsById.Remove(id);
        }
    }

    // Clears the fixed chrome without changing which view is active.
    private void ClearChrome()
    {
        CurrentSnapshot = null;
        _header.Title = string.Empty;
        _header.Subtitle = string.Empty;
        _actions.Commands = null;
        _toolbar.IsVisible = false;
        _snapshotCanGoBack = false;
        _busyProgress.IsIndeterminate = true;
    }

    // Clears the active view and its content, then the chrome.
    private void ClearNavigation()
    {
        if (_activeEntry?.View is IBusyView activeBusy)
        {
            activeBusy.BusyChanged -= OnViewBusyChanged;
        }

        _activeEntry = null;
        _content.Content = null;
        ClearChrome();
    }

    private void MarkDirty(IView view, bool dirty)
    {
        if (_entries.TryGetValue(view, out var entry) && entry.TabId is { } id)
        {
            _tabs.SetDirty(id, dirty);
        }
    }

    private void BindPresenter(IViewPresenter presenter)
    {
        _closedPresenters.Remove(presenter);

        if (!_presenterTokens.ContainsKey(presenter))
        {
            _presenterTokens[presenter] = new CancellationTokenSource();
        }
    }

    private void ActivatePresenter(IViewPresenter presenter)
    {
        BindPresenter(presenter);
        _pendingActivation.Add(presenter);

        Enqueue(
            presenter,
            async cancellationToken =>
            {
                var activated = false;

                try
                {
                    // The contract says "initialized once": PresenterBase is
                    // idempotent, but a hand-written IViewPresenter is not. Only a
                    // successful initialization enters the set, so a failed one is
                    // retried on the next activation.
                    if (_initializedPresenters.Add(presenter))
                    {
                        try
                        {
                            await presenter.InitializeAsync(cancellationToken).ConfigureAwait(true);
                        }
                        catch
                        {
                            _initializedPresenters.Remove(presenter);
                            throw;
                        }
                    }

                    await presenter.ActivateAsync(cancellationToken).ConfigureAwait(true);
                    activated = true;
                }
                finally
                {
                    // A switch-away removed the pending marker and already queued
                    // the deactivate; do not re-add the presenter as active then.
                    var stillPending = _pendingActivation.Remove(presenter);

                    if (activated && stillPending && _presenterTokens.ContainsKey(presenter))
                    {
                        _activated.Add(presenter);
                    }
                }
            },
            "activate",
            _presenterTokens[presenter].Token);
    }

    private void DeactivatePresenter(IViewPresenter presenter)
    {
        var wasActive = _activated.Remove(presenter);
        var wasPending = _pendingActivation.Remove(presenter);

        if (!wasActive && !wasPending)
        {
            return;
        }

        Enqueue(presenter, presenter.DeactivateAsync, "deactivate", CancellationToken.None);
    }

    private Task ClosePresenter(IViewPresenter presenter)
    {
        _initializedPresenters.Remove(presenter);
        // A weak key: the marker must not keep a closed presenter, its view and its
        // models alive for the lifetime of the window.
        _closedPresenters.TryAdd(presenter, null);
        _presenterTokens.Remove(presenter, out var cancellation);
        var wasActive = _activated.Remove(presenter);
        var wasPending = _pendingActivation.Remove(presenter);
        cancellation?.Cancel();

        if (wasActive || wasPending)
        {
            Enqueue(presenter, presenter.DeactivateAsync, "deactivate", CancellationToken.None);
        }

        Enqueue(presenter, _ => presenter.DisposeAsync(), "dispose", CancellationToken.None);
        var pending = _lifecycle[presenter];
        _ = RemoveWhenDoneAsync(presenter, cancellation);
        return pending;
    }

    private string? FindTabId(IViewPresenter presenter)
    {
        foreach (var (id, entry) in _tabsById)
        {
            if (ReferenceEquals(entry.Presenter, presenter))
            {
                return id;
            }
        }

        return null;
    }

    private NavigationEntry? FindEntryForPresenter(IViewPresenter presenter)
    {
        foreach (var entry in _entries.Values)
        {
            if (ReferenceEquals(entry.Presenter, presenter))
            {
                return entry;
            }
        }

        return null;
    }

    // One presenter's lifecycle calls are serialized: a deactivate that follows
    // an initialize must not overtake it, and a dispose must follow both.
    private void Enqueue(
        IViewPresenter presenter,
        Func<CancellationToken, ValueTask> operation,
        string action,
        CancellationToken cancellationToken)
    {
        var previous = _lifecycle.TryGetValue(presenter, out var running) ? running : Task.CompletedTask;
        _lifecycle[presenter] = ContinueAsync(previous, operation, action, cancellationToken);
    }

    private async Task ContinueAsync(
        Task previous,
        Func<CancellationToken, ValueTask> operation,
        string action,
        CancellationToken cancellationToken)
    {
        await previous.ConfigureAwait(true);

        try
        {
            await operation(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            // Closing a tab cancels an in-flight initialization or activation; it
            // is not a failure the operator should see as a banner.
            if (exception is not OperationCanceledException)
            {
                ReportLifecycleFailure(action, exception);
            }
        }
    }

    private async Task RemoveWhenDoneAsync(
        IViewPresenter presenter,
        CancellationTokenSource? cancellation)
    {
        try
        {
            if (_lifecycle.TryGetValue(presenter, out var task))
            {
                await task.ConfigureAwait(true);
            }
        }
        finally
        {
            _lifecycle.Remove(presenter);
            _activated.Remove(presenter);
            cancellation?.Dispose();
        }
    }

    internal void ReportLifecycleFailure(string action, Exception exception)
    {
        var message =
            $"The view could not {action}: {SecretRedaction.Scrub(LogRedaction.SingleLine(exception.Message, 256))}";
        var application = Application.Current as CoreApplication;

        // WHY: an I/O, cancellation or disposal failure is a view error the operator
        // can act on, so it is a warning in the application log; a programming fault
        // must reach the crash log. The banner stays the feedback in both cases.
        // WHY: a product that is not a CoreApplication, or one without diagnostics,
        // has nowhere to write these two branches: the banner is the only feedback.
        if (exception is IOException or UnauthorizedAccessException or
            OperationCanceledException or ObjectDisposedException)
        {
            application?.Diagnostics?.Logger.Log(
                LogLevel.Warning,
                string.Concat(message, " (", exception.GetType().Name, ")"));
        }
        else
        {
            application?.ReportUnhandledException(exception);
        }

        Notifications.Banner(message, MessageSeverity.Error);
    }

    private void OnViewBusyChanged(bool busy)
    {
        if (_activeEntry is { } active)
        {
            active.Busy = busy;
        }

        UpdateBusy();
    }

    // The overlay is on when the active view asks for it (through IBusyView) or its
    // snapshot says so; either path drives the same overlay.
    private void UpdateBusy()
    {
        _busy.IsVisible = (CurrentSnapshot?.Busy ?? false) || (_activeEntry?.Busy ?? false);
    }

    private void Apply(ShellSnapshot snapshot)
    {
        _header.Title = snapshot.Title;
        _header.Subtitle = snapshot.Subtitle;
        _actions.Commands = snapshot.Commands;
        _toolbar.IsVisible = snapshot.Commands.Commands.Count > 0;
        _snapshotCanGoBack = snapshot.CanGoBack;
        _menuBinder.RebuildBindings(snapshot.Commands.Commands);

        if (_activeEntry is { } active)
        {
            MarkDirty(active.View, snapshot.IsDirty);
        }

        _busyProgress.IsIndeterminate = snapshot.Progress is null;

        if (snapshot.Progress is { } progress)
        {
            _busyProgress.Value = Math.Clamp(progress, 0, 1);
        }

        UpdateBack();
        UpdateBusy();
    }

    // The dialog factories are a seam for tests: the shell opens the real shared
    // dialogs, a headless test substitutes a stub so it can assert the routing.
    internal Func<AboutInfo, Window> AboutFactory
    {
        set => _menuBinder.AboutFactory = value;
    }

    internal Func<IAppearanceService, Window> PreferencesFactory
    {
        set => _menuBinder.PreferencesFactory = value;
    }

    internal void OnCommandInvoked(string id) => _menuBinder.OnCommandInvoked(id);

    /// <summary>Raises <see cref="CommandInvoked"/> for a command the shell did not handle itself.</summary>
    /// <param name="id">The command id.</param>
    internal void RaiseCommandInvoked(string id)
    {
        if (_activeEntry is { } active)
        {
            CommandInvoked?.Invoke(active.View, id);
        }
    }

    private void UpdateBack()
    {
        // A CanGoBack flag with no handler would be a dead affordance; the stack
        // always has a handler because Pop owns it.
        _back.IsVisible = _stack.Count > 0 || (_snapshotCanGoBack && BackNavigationRequested is not null);
    }

    private sealed class NavigationEntry(IView view, string? tabId)
    {
        public IView View { get; } = view;

        public string? TabId { get; } = tabId;

        public IViewPresenter? Presenter { get; set; }

        public ShellSnapshot? Snapshot { get; set; }

        public bool Busy { get; set; }
    }

    private static void RequireCommands(ShellSnapshot snapshot)
    {
        if (snapshot.Commands is null || snapshot.Commands.Commands is null)
        {
            throw new ArgumentException(
                "The snapshot must carry a non-null command set and command list.",
                nameof(snapshot));
        }
    }

    private static void RequireControl(IView view)
    {
        if (view is not Control)
        {
            throw new ArgumentException(
                $"The view '{view.GetType().Name}' must be an Avalonia Control to be shown by the shell.",
                nameof(view));
        }
    }

}
