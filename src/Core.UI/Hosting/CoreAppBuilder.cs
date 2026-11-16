// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;

namespace Core.UI.Hosting;

/// <summary>
/// Builds an Avalonia <see cref="AppBuilder"/> with the Core defaults: the
/// platform selection, the embedded Inter font and the requested backend.
/// </summary>
public static class CoreAppBuilder
{
    /// <summary>
    /// Configures the application builder for <typeparamref name="TApp"/> with
    /// the backend selected from the current process environment.
    /// </summary>
    /// <typeparam name="TApp">The application type.</typeparam>
    /// <returns>The configured builder.</returns>
    public static AppBuilder Configure<TApp>()
        where TApp : Application, new()
    {
        return Configure<TApp>(PlatformSelection.FromEnvironment());
    }

    /// <summary>
    /// Configures the application builder for <typeparamref name="TApp"/> with the
    /// application id: the backend selected from the environment, the embedded
    /// Inter font, and the X11 window class set to <paramref name="appId"/> so a
    /// Linux shell ties the window to its desktop entry.
    /// </summary>
    /// <typeparam name="TApp">The application type.</typeparam>
    /// <param name="appId">The application id, the base name of the desktop entry.</param>
    /// <returns>The configured builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="appId"/> is empty or whitespace.</exception>
    public static AppBuilder Configure<TApp>(string appId)
        where TApp : Application, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);

        return Configure<TApp>(PlatformSelection.FromEnvironment())
            .With(new X11PlatformOptions { WmClass = appId })
            .LogToTrace();
    }

    /// <summary>
    /// Configures the application builder for <typeparamref name="TApp"/> with
    /// an explicit backend.
    /// </summary>
    /// <typeparam name="TApp">The application type.</typeparam>
    /// <param name="backend">The backend to use.</param>
    /// <param name="allowFallback">
    /// When <see langword="true"/> and <paramref name="backend"/> is Wayland, fall
    /// back to the previously configured windowing backend when the compositor
    /// connection fails. This is the safe default for the environment-selected
    /// path: a stale <c>WAYLAND_DISPLAY</c> must not abort startup when X11 is
    /// available. Pass <see langword="false"/> to force Wayland.
    /// </param>
    /// <returns>The configured builder.</returns>
    public static AppBuilder Configure<TApp>(GraphicsBackend backend, bool allowFallback = true)
        where TApp : Application, new()
    {
        var builder = AppBuilder.Configure<TApp>()
            .UsePlatformDetect()
            .WithInterFont();

        return backend == GraphicsBackend.Wayland
            ? allowFallback ? builder.UseWaylandWithFallback() : builder.UseWayland()
            : builder;
    }
}
