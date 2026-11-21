// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Core.UI.Chrome;
using Core.UI.Hosting;

namespace Core.UI.Tests;

// The shell has one classification for its catch blocks: every failure shows a
// banner, and a programming fault also reaches the crash log.
[Collection("headless")]
public sealed class LifecycleFailureTests
{
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
}
