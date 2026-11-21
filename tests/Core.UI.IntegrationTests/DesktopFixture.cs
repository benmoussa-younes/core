// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Core.UI.Hosting;

namespace Core.UI.IntegrationTests;

// Hosts Avalonia with the real Linux backend on its own thread, so the dispatcher
// enforces thread affinity and a window reaches the compositor. The headless suite
// cannot prove either: its dispatcher reports CheckAccess on every thread.
public sealed class DesktopFixture : IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly ManualResetEventSlim _started = new();
    private readonly Thread _thread;

    public DesktopFixture()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "avalonia-desktop" };
        _thread.Start();

        if (!_started.Wait(TimeSpan.FromSeconds(60)))
        {
            throw new InvalidOperationException("The desktop backend did not start within 60 seconds.");
        }

        if (StartupFailure is { } failure)
        {
            throw new InvalidOperationException("The desktop backend failed to start.", failure);
        }
    }

    public Exception? StartupFailure { get; private set; }

    public void Dispose()
    {
        _stopping.Cancel();
        _thread.Join(TimeSpan.FromSeconds(10));
        _stopping.Dispose();
        _started.Dispose();
    }

    private void Run()
    {
        try
        {
            AppBuilder.Configure<IntegrationApp>()
                .UsePlatformDetect()
                .SetupWithoutStarting();
        }
        catch (Exception exception)
        {
            StartupFailure = exception;
            _started.Set();
            return;
        }

        _started.Set();
        Dispatcher.UIThread.MainLoop(_stopping.Token);
    }
}

[CollectionDefinition("desktop", DisableParallelization = true)]
public sealed class DesktopCollection : ICollectionFixture<DesktopFixture>
{
}

internal sealed class IntegrationApp : CoreApplication
{
    protected override string ApplicationId => "core-integration";

    public override void Initialize()
    {
        base.Initialize();
        AvaloniaXamlLoader.Load(this);
    }
}
