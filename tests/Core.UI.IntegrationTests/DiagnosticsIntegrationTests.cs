// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.Versioning;
using Avalonia.Threading;
using Core.Platform.Linux;
using Core.UI.Chrome;
using Core.UI.Hosting;
using Core.UI.Tray;

namespace Core.UI.IntegrationTests;

// The headless suite proves the classification in isolation; this proves the same
// route on the real dispatcher, through the public close-guard path, down to the
// crash log file.
[Collection("desktop")]
[SupportedOSPlatform("linux")]
public sealed class DiagnosticsIntegrationTests
{
    [GuiFact]
    public async Task A_programming_fault_from_a_close_guard_reaches_the_crash_log()
    {
        var state = Path.Combine(Path.GetTempPath(), string.Concat("core-diag-", Guid.NewGuid().ToString("N")));

        try
        {
            var options = new CoreHostOptions(
                "core",
                "integration",
                SingleInstance: false,
                Platform: LinuxPlatform.Create(new XdgPathResolver(
                    new XdgEnvironment(state, state, state, state, state, state, null, null))));

            var created = CoreDiagnostics.Create(options);
            Assert.True(created.IsSuccess, created.IsSuccess ? string.Empty : created.Error.Code);

            using var diagnostics = created.Value;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var application = (CoreApplication)Avalonia.Application.Current!;
                var previous = application.Diagnostics;
                application.Diagnostics = diagnostics;

                try
                {
                    var window = new CoreWindow(new TrayService(icon: null, activated: _ => { }));
                    window.ConfirmClose = () => throw new NullReferenceException("close guard probe");
                    window.Show();
                    window.Close();
                    window.Hide();
                }
                finally
                {
                    application.Diagnostics = previous;
                }
            });

            // The close path is asynchronous; a dispatch lets its continuation run.
            await Dispatcher.UIThread.InvokeAsync(() => { });

            Assert.Contains(
                "NullReferenceException",
                File.ReadAllText(diagnostics.Paths.CrashLogFile),
                StringComparison.Ordinal);
            Assert.Contains(
                "unhandled exception",
                File.ReadAllText(diagnostics.Paths.ApplicationLogFile),
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(state))
            {
                Directory.Delete(state, recursive: true);
            }
        }
    }
}
