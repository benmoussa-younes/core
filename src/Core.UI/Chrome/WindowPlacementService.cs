// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Models;
using Core.Mvp.Services;
using Core.Platform;

namespace Core.UI.Chrome;

/// <summary>
/// Remembers a window's placement across runs: it applies the saved placement when
/// the window opens and saves the current one when it closes.
/// </summary>
/// <remarks>
/// The application names the settings file (typically below its XDG state
/// directory). Attach it to the window before the first <c>Show</c>.
/// </remarks>
public sealed class WindowPlacementService
{
    private readonly CoreWindow _window;
    private readonly JsonSettingsStore<WindowPlacement> _store;
    private WindowPlacement? _lastWritten;

    /// <summary>Initializes the service for a window and a settings file.</summary>
    /// <param name="window">The window to track.</param>
    /// <param name="path">The settings file path.</param>
    /// <param name="atomicFiles">The platform atomic-writer factory.</param>
    /// <param name="fileSystem">The platform file system, used for a bounded no-follow read.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/>, <paramref name="atomicFiles"/> or <paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    public WindowPlacementService(
        CoreWindow window,
        string path,
        IAtomicFileWriterFactory atomicFiles,
        IFileSystem fileSystem)
        : this(window, new JsonSettingsStore<WindowPlacement>(
            path,
            WindowPlacementJsonContext.Default.WindowPlacement,
            atomicFiles,
            fileSystem))
    {
    }

    internal WindowPlacementService(CoreWindow window, JsonSettingsStore<WindowPlacement> store)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(store);

        _window = window;
        _store = store;

        window.Opened += OnOpened;
        window.Closed += OnClosed;
        window.QuitRequested += OnQuitRequested;
    }

    /// <summary>Stops tracking the window.</summary>
    public void Detach()
    {
        _window.Opened -= OnOpened;
        _window.Closed -= OnClosed;
        _window.QuitRequested -= OnQuitRequested;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_store.Load() is { } placement)
        {
            _window.ApplyPlacement(placement);
            _lastWritten = placement;
        }
    }

    private void OnClosed(object? sender, EventArgs e) => Save();

    private ValueTask OnQuitRequested()
    {
        // The documented quit path awaits ShutdownAsync without calling Close(),
        // so Closed is not guaranteed to run; save before the presenters drain.
        Save();
        return ValueTask.CompletedTask;
    }

    private void Save()
    {
        var placement = _window.CapturePlacement();

        // Quit then close is the normal exit, and both run this: writing the same
        // placement twice costs two fsyncs on the UI thread for no change.
        if (placement == _lastWritten)
        {
            return;
        }

        try
        {
            _store.Save(placement);
            _lastWritten = placement;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Placement is cosmetic; a read-only state directory must not break
            // the quit or close path.
        }
    }
}
