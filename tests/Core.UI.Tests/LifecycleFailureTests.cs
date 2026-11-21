// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Core.Platform.Linux;
using Core.UI.Chrome;
using Core.UI.Hosting;

namespace Core.UI.Tests;

// The shell has one classification for its catch blocks: every failure shows a
// banner, and a programming fault also reaches the crash log.
[Collection("headless")]
public sealed class LifecycleFailureTests : IDisposable
{
    private readonly string _state =
        Path.Combine(Path.GetTempPath(), string.Concat("core-lifecycle-", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_state))
        {
            Directory.Delete(_state, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task A_programming_fault_also_reaches_the_application_handler()
    {
        await Headless.Dispatch(() =>
        {
            var application = (CoreApplication)Application.Current!;
            var previous = application.UnhandledExceptionHandler;
            Exception? seen = null;
            application.UnhandledExceptionHandler = exception => seen = exception;

            var window = new CoreWindow(new FakeTrayService());
            window.Show();

            try
            {
                window.ReportLifecycleFailure("activate", new NullReferenceException("bug"));

                Assert.IsType<NullReferenceException>(seen);

                seen = null;
                window.ReportLifecycleFailure("activate", new IOException("disk"));

                Assert.Null(seen);
            }
            finally
            {
                application.UnhandledExceptionHandler = previous;
                window.Close();
            }
        });
    }

    [Fact]
    public async Task A_benign_failure_is_a_warning_in_the_application_log()
    {
        await Headless.Dispatch(() =>
        {
            var platform = LinuxPlatform.Create(new XdgPathResolver(
                new XdgEnvironment(_state, _state, _state, _state, _state, _state, null, null)));
            var created = CoreDiagnostics.Create(
                new CoreHostOptions("core", "lifecycle", SingleInstance: false, Platform: platform));

            Assert.True(created.IsSuccess, created.IsSuccess ? string.Empty : created.Error.Code);
            using var diagnostics = created.Value;

            var application = (CoreApplication)Application.Current!;
            var previous = application.Diagnostics;
            application.Diagnostics = diagnostics;

            var window = new CoreWindow(new FakeTrayService());
            window.Show();

            try
            {
                window.ReportLifecycleFailure("activate", new IOException("disk"));

                Assert.Contains(
                    "The view could not activate: disk",
                    File.ReadAllText(diagnostics.Paths.ApplicationLogFile),
                    StringComparison.Ordinal);
                Assert.False(
                    File.Exists(diagnostics.CrashLog.Path) &&
                    File.ReadAllText(diagnostics.CrashLog.Path).Contains("disk", StringComparison.Ordinal));
            }
            finally
            {
                application.Diagnostics = previous;
                window.Close();
            }
        });
    }
}
