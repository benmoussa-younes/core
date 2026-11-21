// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Paths;
using Core.Platform;
using Core.Platform.Linux;
using Core.UI.Hosting;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class CoreDiagnosticsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), string.Concat("core-diagnostics-", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void It_creates_the_three_files_below_the_xdg_state_directory()
    {
        using var diagnostics = CoreDiagnostics.Create(Options()).Value;

        var state = Path.Combine(_root, "state", "acme", "tool");

        Assert.Equal(state, diagnostics.Paths.StateDirectory);
        Assert.Equal(Path.Combine(state, "application.log"), diagnostics.Paths.ApplicationLogFile);
        Assert.Equal(Path.Combine(state, "operations.log"), diagnostics.Paths.OperationJournalFile);
        Assert.Equal(Path.Combine(state, "crash.log"), diagnostics.Paths.CrashLogFile);
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(state));
    }

    [Fact]
    public void Report_writes_the_crash_log_and_the_application_log()
    {
        using var diagnostics = CoreDiagnostics.Create(Options()).Value;

        diagnostics.Report(new InvalidOperationException("diagnostics probe"));

        var crash = File.ReadAllText(diagnostics.CrashLog.Path);
        var application = File.ReadAllText(diagnostics.Paths.ApplicationLogFile);

        Assert.Contains("diagnostics probe", crash, StringComparison.Ordinal);
        Assert.Contains("[Error]", application, StringComparison.Ordinal);
        Assert.Contains("diagnostics probe", application, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_redacts_the_home_directory()
    {
        using var diagnostics = CoreDiagnostics.Create(Options()).Value;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        diagnostics.Report(new InvalidOperationException(
            string.Concat("probe at ", home, "/secret/file.txt")));

        Assert.DoesNotContain(home, File.ReadAllText(diagnostics.Paths.ApplicationLogFile), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_application_uses_the_diagnostics_when_no_handler_is_set()
    {
        await Headless.Dispatch(() =>
        {
            var diagnostics = CoreDiagnostics.Create(Options()).Value;
            var application = new DiagnosticsTestApplication { Diagnostics = diagnostics };
            var original = Console.Error;
            var writer = new StringWriter();
            Console.SetError(writer);

            try
            {
                application.ReportUnhandledException(new InvalidOperationException("application probe"));
            }
            finally
            {
                Console.SetError(original);
            }

            Assert.Contains("application probe", File.ReadAllText(diagnostics.CrashLog.Path), StringComparison.Ordinal);
            Assert.Contains("application probe", File.ReadAllText(diagnostics.Paths.ApplicationLogFile), StringComparison.Ordinal);
            Assert.Equal(string.Empty, writer.ToString());

            diagnostics.Dispose();
        });
    }

    [Fact]
    public void A_missing_platform_is_a_typed_failure()
    {
        var result = CoreDiagnostics.Create(new CoreHostOptions("acme", "tool"));

        Assert.Equal("host.platform_required", result.Error.Code);
    }

    [Fact]
    public void An_invalid_size_is_a_typed_failure()
    {
        var result = CoreDiagnostics.Create(Options() with { LogMaxBytes = 0 });

        Assert.Equal("host.invalid_options", result.Error.Code);
    }

    private CoreHostOptions Options() =>
        new(
            "acme",
            "tool",
            SingleInstance: false,
            Platform: LinuxPlatform.Create(new XdgPathResolver(
                new XdgEnvironment(
                    _root,
                    Path.Combine(_root, "config"),
                    Path.Combine(_root, "state"),
                    Path.Combine(_root, "data"),
                    Path.Combine(_root, "cache"),
                    Path.Combine(_root, "run"),
                    null,
                    null))));

    private sealed class DiagnosticsTestApplication : CoreApplication
    {
        protected override string ApplicationId => "core-diagnostics-test";
    }
}
