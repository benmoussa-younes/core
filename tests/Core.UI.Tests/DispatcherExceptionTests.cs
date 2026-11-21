// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Threading;
using Core.UI.Hosting;

namespace Core.UI.Tests;

[Collection("headless")]
public sealed class DispatcherExceptionTests
{
    [Fact]
    public async Task A_dispatcher_exception_is_reported_and_marked_handled()
    {
        var (reports, handled) = await Headless.Dispatch(() =>
        {
            var application = (CoreApplication)Application.Current!;
            var count = 0;
            application.UnhandledExceptionHandler = _ => count++;

            var args = ExceptionArgs(new InvalidOperationException("boom"));
            CoreApplication.OnDispatcherUnhandledException(this, args);

            application.UnhandledExceptionHandler = null;
            return (count, args.Handled);
        });

        Assert.True(handled, "an escaped dispatcher exception must not reach the AppDomain hook");
        Assert.Equal(1, reports);
    }

    // WHY: Avalonia 12 raises these args from its dispatcher and offers no public
    // way to build them; the exception is a private field of an object whose only
    // constructor takes the dispatcher.
    private static DispatcherUnhandledExceptionEventArgs ExceptionArgs(Exception exception)
    {
        var args = (DispatcherUnhandledExceptionEventArgs)RuntimeHelpers.GetUninitializedObject(
            typeof(DispatcherUnhandledExceptionEventArgs));

        var field = typeof(DispatcherUnhandledExceptionEventArgs)
            .GetField("_exception", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The event args no longer carry a private _exception field.");

        field.SetValue(args, exception);
        return args;
    }
}
