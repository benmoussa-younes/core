// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Controls;
using Avalonia.Interactivity;
using Core.Platform;
using Core.Platform.Linux;
using Core.Results;
using Core.UI.Dialogs;
using Core.UI.Hosting;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class AboutDialogTests
{
    [Fact]
    public async Task Links_go_through_the_injected_launcher()
    {
        await Headless.Dispatch(() =>
        {
            var launcher = new RecordingLauncher();
            var info = new AboutInfo("Tool", "1.0", "notice", new Uri("https://example.com/source"), LicenseUrl: new Uri("file:///etc/passwd"));

            var dialog = new AboutDialog(info, launcher);

            // No platform bypass: the HyperlinkButton has nothing to navigate to.
            Assert.Null(dialog.SourceLink.NavigateUri);
            Assert.Null(dialog.LicenseLink.NavigateUri);

            Click(dialog.SourceLink);
            Click(dialog.LicenseLink);

            Assert.Equal(["https://example.com/source", "file:///etc/passwd"], launcher.Opened);
        });
    }

    [Fact]
    public async Task Without_a_launcher_the_platform_still_navigates()
    {
        await Headless.Dispatch(() =>
        {
            var info = new AboutInfo("Tool", "1.0", "notice", new Uri("https://example.com/source"));

            var dialog = new AboutDialog(info);

            Assert.Equal(new Uri("https://example.com/source"), dialog.SourceLink.NavigateUri);
            Assert.Equal(LegalNotice.LicenseUrl, dialog.LicenseLink.NavigateUri);
        });
    }

    [Fact]
    public async Task A_relative_link_is_refused_at_construction()
    {
        await Headless.Dispatch(() =>
        {
            var relativeSource = new AboutInfo("Tool", "1.0", "notice", new Uri("relative/source", UriKind.Relative));
            var relativeLicense = new AboutInfo(
                "Tool",
                "1.0",
                "notice",
                new Uri("https://example.com/source"),
                LicenseUrl: new Uri("licence", UriKind.Relative));

            Assert.Throws<ArgumentException>(() => new AboutDialog(relativeSource));
            Assert.Throws<ArgumentException>(() => new AboutDialog(relativeLicense));
        });
    }

    [Fact]
    public async Task A_failed_launch_leaves_a_warning_in_the_application_log()
    {
        var state = Path.Combine(Path.GetTempPath(), string.Concat("core-about-", Guid.NewGuid().ToString("N")));

        try
        {
            await Headless.Dispatch(async () =>
            {
                var platform = LinuxPlatform.Create(new XdgPathResolver(
                    new XdgEnvironment(state, state, state, state, state, state, null, null)));
                using var diagnostics = CoreDiagnostics.Create(
                    new CoreHostOptions("core", "about", SingleInstance: false, Platform: platform)).Value;

                var application = (CoreApplication)Avalonia.Application.Current!;
                var previous = application.Diagnostics;
                application.Diagnostics = diagnostics;

                try
                {
                    var dialog = new AboutDialog(
                        new AboutInfo("Tool", "1.0", "notice", new Uri("https://example.com/source")),
                        new RefusingLauncher());

                    Click(dialog.SourceLink);
                    await Task.Delay(50);

                    Assert.Contains(
                        "link.invalid",
                        File.ReadAllText(diagnostics.Paths.ApplicationLogFile),
                        StringComparison.Ordinal);
                }
                finally
                {
                    application.Diagnostics = previous;
                }
            });
        }
        finally
        {
            if (Directory.Exists(state))
            {
                Directory.Delete(state, recursive: true);
            }
        }
    }

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private sealed class RefusingLauncher : ISystemLinkLauncher
    {
        public ValueTask<Result<bool>> OpenAsync(string url, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<bool>.Failure(new CoreError("link.invalid", "refused")));
    }

    private sealed class RecordingLauncher : ISystemLinkLauncher
    {
        public List<string> Opened { get; } = [];

        public ValueTask<Result<bool>> OpenAsync(string url, CancellationToken cancellationToken = default)
        {
            Opened.Add(url);
            return ValueTask.FromResult(Result<bool>.Success(true));
        }
    }
}
