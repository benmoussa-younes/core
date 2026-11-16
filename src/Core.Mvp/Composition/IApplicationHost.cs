// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Presentation;
using Core.Mvp.Services;

namespace Core.Mvp.Composition;

/// <summary>
/// The application-level shell a presenter drives: the fixed chrome of
/// <see cref="IViewHost"/> plus navigation, the tray presence and the quit
/// lifecycle.
/// </summary>
/// <remarks>
/// A background application closes to the tray (<see cref="CloseBehavior.HideToTray"/>)
/// and stops only on <see cref="Quit"/>; the shell then holds the process alive
/// after the main window is hidden. A product routes a tray activation and a
/// second launch to <see cref="ShowWindow"/>, which surfaces the window again and
/// raises <see cref="Activated"/>.
/// </remarks>
public interface IApplicationHost : IViewHost
{
    /// <summary>Gets the navigation host (tabs or stack).</summary>
    INavigationHost Navigation { get; }

    /// <summary>Gets the tray service.</summary>
    ITrayService Tray { get; }

    /// <summary>Gets or sets what closing the main window does.</summary>
    CloseBehavior CloseBehavior { get; set; }

    /// <summary>
    /// Publishes the application menu: the commands the shell renders in its
    /// primary menu, above the standard tail it always appends. Activating one
    /// raises <see cref="IViewHost.CommandInvoked"/> with the active view and the command id.
    /// </summary>
    /// <param name="menu">The application commands, grouped like the toolbar.</param>
    void SetApplicationMenu(CommandSet menu);

    /// <summary>
    /// Raised when the application is activated from outside: a tray activation or
    /// a second launch that the product routed to <see cref="ShowWindow"/>.
    /// </summary>
    event Action? Activated;

    /// <summary>Brings the main window to the foreground and activates it.</summary>
    void ShowWindow();

    /// <summary>Hides the main window; the application keeps running (for example in the tray).</summary>
    void HideWindow();

    /// <summary>Quits the application, ignoring <see cref="CloseBehavior"/>.</summary>
    /// <remarks>
    /// This is the fire-and-forget convenience for a menu or tray callback. A
    /// product that must flush before the process exits awaits
    /// <see cref="QuitAsync"/> instead; a synchronous handler cannot wait for the
    /// shell's asynchronous presenter shutdown.
    /// </remarks>
    void Quit() => _ = QuitAsync().AsTask();

    /// <summary>
    /// Quits the application, ignoring <see cref="CloseBehavior"/>, and completes
    /// after the shell has deactivated and disposed its presenters.
    /// </summary>
    /// <remarks>
    /// The default is a no-op for a host without presenters. A host that owns a
    /// lifecycle overrides it, and a product awaits it from its quit path before
    /// stopping the application lifetime.
    /// </remarks>
    /// <returns>A value task that completes when the quit and the shutdown have finished.</returns>
    ValueTask QuitAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Deactivates and disposes every presenter and drains the lifecycle queue.
    /// A product that owns the quit path awaits this before stopping the
    /// application; a host without presenters may keep the default no-op.
    /// </summary>
    /// <returns>A value task that completes when the shell has released its presenters.</returns>
    ValueTask ShutdownAsync() => ValueTask.CompletedTask;
}
