// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.UI.IntegrationTests;

// The GUI suite needs a display and the real X11 libraries, so it is discovered by
// every `dotnet test` but skipped unless the operator asks for it:
//
//   CORE_GUI_TESTS=1 xvfb-run -a dotnet test tests/Core.UI.IntegrationTests
//
// The CI workflow sets the variable; the headless gate leaves it unset.
internal static class GuiTests
{
    public const string Variable = "CORE_GUI_TESTS";

    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(Variable), "1", StringComparison.Ordinal);
}

/// <summary>A fact that only runs when <c>CORE_GUI_TESTS=1</c>.</summary>
public sealed class GuiFactAttribute : FactAttribute
{
    public GuiFactAttribute(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!GuiTests.IsEnabled)
        {
            Skip = string.Concat("Set ", GuiTests.Variable, "=1 and run under a display (Xvfb is enough).");
        }
    }
}
