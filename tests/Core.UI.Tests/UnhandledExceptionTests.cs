// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia;
using Core.UI.Hosting;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class UnhandledExceptionTests
{
    [Fact]
    public async Task An_appdomain_exception_reaches_the_application_handler()
    {
        var captured = await Headless.Dispatch(() =>
        {
            var application = (CoreApplication)Application.Current!;
            Exception? seen = null;
            application.UnhandledExceptionHandler = exception => seen = exception;

            CoreApplication.OnAppDomainUnhandledException(
                this,
                new UnhandledExceptionEventArgs(new InvalidOperationException("boom"), isTerminating: false));

            application.UnhandledExceptionHandler = null;
            return seen;
        });

        Assert.IsType<InvalidOperationException>(captured);
        Assert.Equal("boom", captured!.Message);
    }

    [Fact]
    public async Task An_unobserved_task_exception_reaches_the_application_handler_and_is_observed()
    {
        var (captured, observed) = await Headless.Dispatch(() =>
        {
            var application = (CoreApplication)Application.Current!;
            Exception? seen = null;
            application.UnhandledExceptionHandler = exception => seen = exception;

            var args = new UnobservedTaskExceptionEventArgs(
                new AggregateException(new InvalidOperationException("boom")));
            CoreApplication.OnUnobservedTaskException(this, args);

            application.UnhandledExceptionHandler = null;
            return (seen, args.Observed);
        });

        Assert.IsType<AggregateException>(captured);
        Assert.True(observed);
    }

    [Fact]
    public async Task The_default_handler_writes_to_stderr()
    {
        var text = await Headless.Dispatch(() =>
        {
            var application = new TestApplication();
            var original = Console.Error;
            var writer = new StringWriter();
            Console.SetError(writer);

            try
            {
                application.ReportUnhandledException(new InvalidOperationException("boom"));
            }
            finally
            {
                Console.SetError(original);
            }

            return writer.ToString();
        });

        Assert.Contains("boom", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_handler_receives_an_exception_directly()
    {
        var captured = await Headless.Dispatch(() =>
        {
            var application = new TestApplication();
            Exception? seen = null;
            application.UnhandledExceptionHandler = exception => seen = exception;
            application.ReportUnhandledException(new InvalidOperationException("boom"));
            return seen;
        });

        Assert.IsType<InvalidOperationException>(captured);
        Assert.Throws<ArgumentNullException>(() => new TestApplication().ReportUnhandledException(null!));
    }

    private sealed class TestApplication : CoreApplication
    {
        protected override string ApplicationId => "core-unhandled-test";
    }
}
