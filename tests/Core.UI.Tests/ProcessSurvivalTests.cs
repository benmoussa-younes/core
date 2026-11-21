// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Core.UI.Tests;

// The survival claim needs a process that survives: the unit case only asserts the
// Handled flag. Core.UI.CrashProbe posts a throwing job, then a marker job, and
// exits 0 only if the loop kept running.
[Collection("headless")]
public sealed class ProcessSurvivalTests : IDisposable
{
    private readonly string _state =
        Path.Combine(Path.GetTempPath(), string.Concat("core-survival-", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_state))
        {
            Directory.Delete(_state, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_dispatcher_exception_is_logged_once_and_the_process_survives()
    {
        var probe = Path.Combine(AppContext.BaseDirectory, "crash-probe", "Core.UI.CrashProbe.dll");

        Assert.True(File.Exists(probe), $"the probe is not copied to the test output: {probe}");

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        start.ArgumentList.Add(probe);
        start.ArgumentList.Add(_state);

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(TimeSpan.FromSeconds(60));

        Assert.True(process.HasExited, "the probe did not exit within 60 seconds");
        Assert.True(process.ExitCode == 0, $"exit={process.ExitCode} out={output} err={error}");

        var crash = File.ReadAllText(Path.Combine(_state, "core", "crash-probe", "crash.log"));
        var application = File.ReadAllText(Path.Combine(_state, "core", "crash-probe", "application.log"));

        Assert.Equal(1, Count(crash, "crash probe"));
        Assert.Equal(1, Count(application, "unhandled exception: crash probe"));
        Assert.Equal("survived", File.ReadAllText(Path.Combine(_state, "survived.txt")));
    }

    private static int Count(string text, string value) =>
        Regex.Matches(text, Regex.Escape(value), RegexOptions.None, TimeSpan.FromSeconds(2)).Count;
}
