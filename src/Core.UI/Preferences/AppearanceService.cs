// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using Core.UI.Adwaita;
using Core.UI.Hosting;
using Core.Mvp.Services;

namespace Core.UI.Preferences;

/// <summary>
/// The <see cref="IAppearanceService"/>: it owns the appearance choices, applies
/// the theme variant and the accent to the running <see cref="Application"/> and
/// persists them through an <see cref="IAppearanceStore"/> when the operator asked
/// to remember.
/// </summary>
public sealed class AppearanceService : IAppearanceService
{
    private readonly IAppearanceStore _store;
    private AppearanceSettings _current;

    /// <summary>Initializes a new instance of the <see cref="AppearanceService"/> class.</summary>
    /// <param name="store">The persistence seam.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public AppearanceService(IAppearanceStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _current = store.Load() ?? AppearanceSettings.Default;
    }

    /// <inheritdoc />
    public AppearanceSettings Current => _current;

    /// <inheritdoc />
    public void ChooseTheme(ThemePreference theme)
    {
        if (!Enum.IsDefined(theme))
        {
            throw new ArgumentOutOfRangeException(nameof(theme));
        }

        Dispatcher.UIThread.VerifyAccess();

        _current = _current with { Theme = theme };
        ApplyTheme();
        Persist();
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="key"/> is empty or not in the accent palette.</exception>
    public void ChooseAccent(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (AccentPalette.Find(key) is null)
        {
            throw new ArgumentException($"'{key}' is not in the accent palette.", nameof(key));
        }

        Dispatcher.UIThread.VerifyAccess();

        _current = _current with { AccentKey = key };
        ApplyAccent(key);
        Persist();
    }

    /// <inheritdoc />
    public void SetRemember(bool remember)
    {
        Dispatcher.UIThread.VerifyAccess();

        _current = _current with { Remember = remember };

        if (remember)
        {
            TrySave();
        }
        else
        {
            _store.Clear();
        }
    }

    /// <inheritdoc />
    public void Apply()
    {
        Dispatcher.UIThread.VerifyAccess();

        ApplyTheme();

        // A settings file from an older release can name an accent that has since
        // been removed or renamed. Loading is best-effort, so an unknown key must
        // not turn startup into an ArgumentException; the operator can choose a
        // new accent, and ChooseAccent still rejects an unknown key.
        if (_current.AccentKey is { Length: > 0 } key && AccentPalette.Find(key) is not null)
        {
            ApplyAccent(key);
        }
    }

    private void Persist()
    {
        if (_current.Remember)
        {
            TrySave();
        }
    }

    private void TrySave()
    {
        // A state directory can be read-only or full. Persisting an appearance
        // preference is best-effort, like loading it; it must not turn a theme
        // change or a window close into an unhandled platform exception.
        try
        {
            _store.Save(_current);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void ApplyTheme()
    {
        if (Application.Current is CoreApplication application)
        {
            application.ApplyThemePreference(_current.Theme);
            return;
        }

        if (Application.Current is { } current)
        {
            current.RequestedThemeVariant = _current.Theme switch
            {
                ThemePreference.Light => ThemeVariant.Light,
                ThemePreference.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }

    private static void ApplyAccent(string key)
    {
        if (Application.Current is CoreApplication application)
        {
            application.ApplyAccent(key);
        }
    }
}
