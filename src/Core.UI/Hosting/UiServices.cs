// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Threading;

namespace Core.UI.Hosting;

// The dispatcher the shell's services depend on. It exists so the thread-affinity
// guard is testable: the headless dispatcher reports CheckAccess on every thread,
// so a test can only prove the guard through a substitute.
internal interface IUiDispatcher
{
    bool CheckAccess();

    void VerifyAccess();

    void Post(Action action);

    // False once the dispatcher has started shutting down: a posted action then
    // never runs, so a caller that awaits one must not wait.
    bool IsAvailable { get; }
}

internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    private volatile bool _shuttingDown;

    private AvaloniaUiDispatcher()
    {
        // WHY: Avalonia exposes the shutdown as an event, not a state, and the flag
        // has to be readable from any thread a cancellation callback runs on.
        Dispatcher.UIThread.ShutdownStarted += (_, _) => _shuttingDown = true;
    }

    public static AvaloniaUiDispatcher Instance { get; } = new();

    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void VerifyAccess() => Dispatcher.UIThread.VerifyAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public bool IsAvailable => !_shuttingDown;
}

internal static class UiServices
{
    // WHY: the only assignment is in Core.UI.Tests (InternalsVisibleTo); a product
    // never replaces it, and the headless collection serializes the tests that do.
    internal static IUiDispatcher Dispatcher { get; set; } = AvaloniaUiDispatcher.Instance;
}
