// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Core.Diagnostics;
using Core.Primitives;
using Core.Security;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Core.UI.Adwaita;
using Core.UI.Chrome;
using Core.Mvp.Services;

namespace Core.UI.Hosting;

/// <summary>
/// The default Core application: it loads Fluent, follows the system light or
/// dark variant (dark-first when the platform cannot report one) and names the
/// shell after the application id. A product derives from it, includes the shared
/// tokens and control styles in its <c>App.axaml</c>, and hosts its
/// <see cref="CoreWindow"/> in the lifetime callback.
/// </summary>
/// <remarks>
/// The shared resources are included by the product's <c>App.axaml</c>: merge the
/// resource dictionary <c>avares://Core.UI/Themes/Tokens.axaml</c>
/// into <c>Application.Resources</c>, merge
/// <c>avares://Core.UI/Themes/Icons.axaml</c> for the toolbar
/// glyphs, and <c>&lt;StyleInclude&gt;</c>
/// <c>avares://Core.UI/Themes/Controls.axaml</c>. A XAML include is
/// resolved at build time and is AOT-safe; a code-based
/// <c>StyleInclude</c>/<c>ResourceInclude</c> uses the dynamic XAML loader, which
/// the trimming analyzer rejects (IL2026).
/// </remarks>
public abstract class CoreApplication : Application
{
    private static int _unhandledWired;

    /// <summary>
    /// Initializes a new instance of the <see cref="CoreApplication"/> class.
    /// </summary>
    protected CoreApplication()
    {
        Name = ApplicationId;
    }

    /// <summary>
    /// Gets or sets the last-chance handler for an exception that escaped the UI
    /// loop. When it is <see langword="null"/>, <see cref="ReportUnhandledException"/>
    /// writes to <see cref="Diagnostics"/> when one is set, and to
    /// <see cref="Console.Error"/> otherwise.
    /// </summary>
    public Action<Exception>? UnhandledExceptionHandler { get; set; }

    /// <summary>
    /// Gets or sets the durable diagnostics of this application: when no
    /// <see cref="UnhandledExceptionHandler"/> is set, an unhandled exception is
    /// written to its crash log and application log instead of the console.
    /// </summary>
    /// <remarks>
    /// A product that creates a <see cref="CoreHost"/> gets this through
    /// <see cref="CoreHost.Attach"/>. A product without a host assigns
    /// <c>CoreDiagnostics.Create(options).Value</c> here, so the files land in its
    /// XDG state directory instead of being lost. Both write the same redacted
    /// entry; the console is the last resort when neither is set.
    /// <para>
    /// The application does not own the instance: dispose it from the product's
    /// quit path, after the last failure it can report.
    /// </para>
    /// </remarks>
    public CoreDiagnostics? Diagnostics { get; set; }

    /// <summary>Gets the application id: the base name of the desktop entry.</summary>
    protected abstract string ApplicationId { get; }

    /// <summary>
    /// Gets the per-application typography overrides, or <see langword="null"/> to
    /// keep the shared defaults. The base applies them after the resources load, so
    /// the product does not depend on the <c>App.axaml</c> merge order.
    /// </summary>
    protected virtual ThemeOptions? ThemeOverrides => null;

    /// <inheritdoc />
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Default;
        Styles.Add(new FluentTheme());
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        // Dark-first. When the platform cannot report the system appearance (a
        // missing desktop portal or a locked dconf), the variant is null and
        // Avalonia's Default resolves to Light, so a white window on a dark desktop
        // is glaring. Pin Dark then; a known variant keeps following the system
        // (themable as Default).
        RequestedThemeVariant = StartupVariant(PlatformSettings?.GetColorValues().ThemeVariant);
        ApplyThemeOverrides(ThemeOverrides);
        WireUnhandledExceptions();

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Reports an exception that escaped the application to the last-chance handler,
    /// to <see cref="Diagnostics"/> when no handler is set, or to
    /// <see cref="Console.Error"/> when neither is. The process-level hooks
    /// (<see cref="AppDomain.UnhandledException"/>,
    /// <see cref="TaskScheduler.UnobservedTaskException"/> and Avalonia's
    /// <c>Dispatcher.UnhandledException</c>) route here.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public void ReportUnhandledException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (UnhandledExceptionHandler is { } handler)
        {
            handler(exception);
        }
        else if (Diagnostics is { } diagnostics)
        {
            diagnostics.Report(exception);
        }
        else
        {
            // Same redaction as CrashLog.Write: a message can carry the home path and
            // control characters, and stderr is still a durable sink for a service.
            Console.Error.WriteLine(
                PathRedaction.Scrub(
                    LogRedaction.SingleLine(SecretRedaction.Scrub(exception.ToString()), 2_048)));
        }
    }

    /// <summary>
    /// Applies one accent from the studio palette (<see cref="AccentPalette.Catalog"/>)
    /// without replacing the whole token dictionary.
    /// </summary>
    /// <param name="key">The palette token key (for example <c>AccentBlue</c>).</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is empty or not in the palette.</exception>
    /// <exception cref="KeyNotFoundException">The application did not merge <c>Themes/Accents.axaml</c>.</exception>
    public void ApplyAccent(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (AccentPalette.Find(key) is null)
        {
            throw new ArgumentException($"'{key}' is not in the accent palette.", nameof(key));
        }

        SetAccent(ThemeResources.ResolveColor(key));
    }

    /// <summary>Selects the startup variant from the platform's reported variant.</summary>
    /// <param name="platformVariant">The variant the platform reports, or <see langword="null"/> when it cannot.</param>
    /// <returns>Dark when the platform cannot report a variant; otherwise Default, so the system keeps driving it live.</returns>
    internal static ThemeVariant StartupVariant(PlatformThemeVariant? platformVariant) =>
        platformVariant is null ? ThemeVariant.Dark : ThemeVariant.Default;

    /// <summary>
    /// Applies one of the three appearance choices, keeping the dark-first
    /// fallback when "System" has no platform variant to follow.
    /// </summary>
    /// <param name="preference">The chosen appearance.</param>
    public void ApplyThemePreference(ThemePreference preference)
    {
        RequestedThemeVariant = ResolveVariant(
            preference,
            PlatformSettings?.GetColorValues().ThemeVariant);
    }

    internal static ThemeVariant ResolveVariant(
        ThemePreference preference,
        PlatformThemeVariant? platformVariant) =>
        preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => StartupVariant(platformVariant),
        };

    /// <summary>
    /// Overrides the accent colour at run time, so a product can apply its brand
    /// without replacing the whole token dictionary. It sets the fill
    /// (<c>PrimaryBrush</c>), the WCAG-contrasting on-fill text
    /// (<c>PrimaryForegroundBrush</c>) and the hover/selection washes derived from
    /// the accent. <c>AccentTextBrush</c> stays the theme-defined text colour: the
    /// fill is not a text colour, and replacing it with the accent breaks contrast
    /// in the light theme.
    /// </summary>
    /// <param name="accent">The accent colour.</param>
    /// <remarks>Call it after the application resources are loaded (from <c>Initialize</c> or later).</remarks>
    public void SetAccent(Color accent)
    {
        // Pick the foreground that maximises contrast, not the one selected by a
        // raw sRGB average: for AccentBlue, black is 5.71:1 and white is 3.68:1.
        var onAccent = ContrastRatio(accent, Colors.Black) >= ContrastRatio(accent, Colors.White)
            ? Colors.Black
            : Colors.White;

        Resources["PrimaryBrush"] = new SolidColorBrush(accent);
        Resources["PrimaryForegroundBrush"] = new SolidColorBrush(onAccent);
        Resources["HoverBrush"] = new SolidColorBrush(Color.FromArgb(0x1A, accent.R, accent.G, accent.B));
        Resources["SelectedBrush"] = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B));
    }

    // The process hooks are global, so they are wired once, on the first
    // application; they route to whichever application is current.
    private static void WireUnhandledExceptions()
    {
        if (Interlocked.Exchange(ref _unhandledWired, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
    }

    internal static void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (Application.Current is CoreApplication application)
        {
            application.ReportUnhandledException(e.Exception);

            // The failure is reported, so letting it escape the dispatcher would
            // both kill the process and write the same entry to the crash log a
            // second time through the AppDomain hook.
            e.Handled = true;
        }
    }

    internal static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception && Application.Current is CoreApplication application)
        {
            application.ReportUnhandledException(exception);
        }
    }

    internal static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();

        if (Application.Current is CoreApplication application)
        {
            application.ReportUnhandledException(e.Exception);
        }
    }

    // WCAG 2 relative luminance: linearise each sRGB channel before weighting.
    private static double RelativeLuminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linear(color.R))
            + (0.7152 * Linear(color.G))
            + (0.0722 * Linear(color.B));
    }

    private static double ContrastRatio(Color first, Color second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);

        return (Math.Max(firstLuminance, secondLuminance) + 0.05)
            / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private void ApplyThemeOverrides(ThemeOptions? options)
    {
        if (options is null)
        {
            return;
        }

        if (options.UiFont is { } font)
        {
            Resources["FontFamily"] = font;
        }

        if (options.MonospaceFont is { } mono)
        {
            Resources["MonospaceFont"] = mono;
        }

        if (options.BaseFontSize is { } size)
        {
            Resources["FontSize"] = size;
        }
    }
}
