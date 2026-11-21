// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Core.Platform.Linux;
using Core.UI.Hosting;

// A throwaway process for the survival claim: it posts a throwing job on the
// dispatcher, then a marker job, and exits 0 only if the loop kept running and the
// failure was reported. Exit codes: 0 survived, 2 the loop stopped, 3 no
// application, 4 no diagnostics, 64 usage. The test reads crash.log and
// application.log from the state directory it passes.
if (args.Length != 1)
{
    Console.Error.WriteLine("usage: Core.UI.CrashProbe <state-directory>");
    return 64;
}

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("this probe exercises the Linux platform composition");
    return 65;
}

var state = args[0];
Directory.CreateDirectory(state);

AppBuilder.Configure<ProbeApp>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
    .SetupWithoutStarting();

if (Application.Current is not ProbeApp application)
{
    return 3;
}

var platform = LinuxPlatform.Create(new XdgPathResolver(new XdgEnvironment(
    state,
    state,
    state,
    state,
    state,
    state,
    null,
    null)));

var diagnostics = CoreDiagnostics.Create(new CoreHostOptions(
    "core",
    "crash-probe",
    SingleInstance: false,
    Platform: platform));

if (diagnostics.IsFailure)
{
    Console.Error.WriteLine(diagnostics.Error.Code);
    return 4;
}

application.Diagnostics = diagnostics.Value;

var marker = Path.Combine(state, "survived.txt");

Dispatcher.UIThread.Post(() => throw new InvalidOperationException("crash probe"));
Dispatcher.UIThread.Post(() => File.WriteAllText(marker, "survived"));

Dispatcher.UIThread.RunJobs();
Dispatcher.UIThread.RunJobs();

return File.Exists(marker) ? 0 : 2;

internal sealed class ProbeApp : CoreApplication
{
    protected override string ApplicationId => "crash-probe";
}
