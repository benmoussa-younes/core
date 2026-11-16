// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Core.UI.Hosting;

[assembly: AvaloniaTestApplication(typeof(Core.UI.Tests.TestAppBuilder))]

namespace Core.UI.Tests;

/// <summary>
/// The headless session owns one UI thread and is not reentrant, so every
/// headless test shares this collection and never runs in parallel.
/// </summary>
[CollectionDefinition("headless", DisableParallelization = true)]
public sealed class HeadlessCollection
{
}

public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        // Real drawing (Skia) rather than the no-op headless renderer, so a test
        // that touches a Bitmap (the image clipboard) exercises the real codec.
        return AppBuilder.Configure<HeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia();
    }
}

/// <summary>
/// The application the headless tests run on: the real Core application base
/// plus the shared theme, so a test sees the same layout, hit-testing, brushes and
/// appearance behaviour as the desktop application.
/// </summary>
internal sealed partial class HeadlessApp : CoreApplication
{
    /// <inheritdoc />
    protected override string ApplicationId => "core-headless";

    /// <inheritdoc />
    public override void Initialize()
    {
        base.Initialize();
        AvaloniaXamlLoader.Load(this);
    }
}

/// <summary>
/// Runs a test body on the headless Avalonia UI thread. This uses the
/// framework-agnostic session, not the xUnit integration, so the tests do not
/// bind the framework to one test runner.
/// </summary>
internal static class Headless
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(Headless).Assembly);

    public static Task Dispatch(Action action)
    {
        return Session.Dispatch(action, CancellationToken.None);
    }

    public static Task<T> Dispatch<T>(Func<T> func)
    {
        return Session.Dispatch(func, CancellationToken.None);
    }

    public static async Task Dispatch(Func<Task> action)
    {
        // Dispatch the async body on the UI thread and await it there, so a
        // posted dismissal or continuation is pumped while the test waits.
        await Session.Dispatch(
            async () =>
            {
                await action().ConfigureAwait(true);
                return true;
            },
            CancellationToken.None);
    }
}
