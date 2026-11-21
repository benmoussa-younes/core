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
    public void A_uri_credential_past_the_line_cap_is_still_redacted()
    {
        using var diagnostics = CoreDiagnostics.Create(Options()).Value;
        var password = new string('x', 2_100);

        diagnostics.Report(new InvalidOperationException(
            string.Concat("open https://alice:", password, "@example.com/api")));

        Assert.DoesNotContain(
            "alice:xxx",
            File.ReadAllText(diagnostics.Paths.ApplicationLogFile),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_home_path_cut_by_the_line_cap_leaves_no_fragment()
    {
        // The home is redacted before the 2048-character cap, so a path that starts
        // just before the cut cannot leave its first characters in the log.
        using var diagnostics = CoreDiagnostics.Create(Options()).Value;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var padding = new string('a', 2_040);

        diagnostics.Report(new InvalidOperationException(string.Concat(padding, home, "/secret")));

        Assert.DoesNotContain(
            home[..8],
            File.ReadAllText(diagnostics.Paths.ApplicationLogFile),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_control_character_in_the_state_home_falls_back_to_the_default()
    {
        // XdgDirectories refuses a value it cannot use and the spec default applies;
        // the failure must not surface as an uncaught ArgumentException.
        using var diagnostics = CoreDiagnostics.Create(OptionsWithStateHome(string.Concat(_root, "/state\0evil"))).Value;

        Assert.Equal(
            Path.Combine(_root, ".local", "state", "acme", "tool"),
            diagnostics.Paths.StateDirectory);
    }

    [Fact]
    public void Every_created_state_component_is_owner_only()
    {
        using var diagnostics = CoreDiagnostics.Create(Options()).Value;

        var stateHome = Path.Combine(_root, "state");
        var vendor = Path.Combine(stateHome, "acme");

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(stateHome));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(vendor));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(diagnostics.Paths.StateDirectory));
    }

    [Fact]
    public void A_symlinked_state_directory_is_refused()
    {
        var real = Path.Combine(_root, "real");
        Directory.CreateDirectory(real);
        Directory.CreateDirectory(Path.Combine(_root, "state", "acme"));
        Directory.CreateSymbolicLink(Path.Combine(_root, "state", "acme", "tool"), real);

        var result = CoreDiagnostics.Create(Options());

        Assert.Equal("host.state_unwritable", result.Error.Code);
    }

    [Fact]
    public void An_unwritable_state_directory_is_a_typed_failure()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "state"), "not a directory");

        var result = CoreDiagnostics.Create(Options());

        Assert.Equal("host.state_unwritable", result.Error.Code);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var diagnostics = CoreDiagnostics.Create(Options()).Value;

        diagnostics.Dispose();
        diagnostics.Dispose();
    }

    [Fact]
    public async Task The_handler_wins_over_the_diagnostics()
    {
        await Headless.Dispatch(() =>
        {
            var diagnostics = CoreDiagnostics.Create(Options()).Value;
            var application = new DiagnosticsTestApplication { Diagnostics = diagnostics };
            var seen = false;
            application.UnhandledExceptionHandler = _ => seen = true;

            application.ReportUnhandledException(new InvalidOperationException("handler probe"));

            Assert.True(seen);
            Assert.False(
                File.Exists(diagnostics.CrashLog.Path) &&
                File.ReadAllText(diagnostics.CrashLog.Path).Contains("handler probe", StringComparison.Ordinal));

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
        OptionsWithStateHome(Path.Combine(_root, "state"));

    private CoreHostOptions OptionsWithStateHome(string stateHome) =>
        new(
            "acme",
            "tool",
            SingleInstance: false,
            Platform: LinuxPlatform.Create(new XdgPathResolver(
                new XdgEnvironment(
                    _root,
                    Path.Combine(_root, "config"),
                    stateHome,
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
