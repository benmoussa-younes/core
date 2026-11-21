// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Controls;
using Avalonia.Interactivity;
using Core.Platform;
using Core.Results;
using Core.UI.Dialogs;

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
        });
    }

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

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
