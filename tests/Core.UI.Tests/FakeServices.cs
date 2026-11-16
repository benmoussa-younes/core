// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Presentation;
using Core.Mvp.Services;

namespace Core.UI.Tests;

internal sealed class FakeDialogService : IDialogService
{
    public ValueTask<ConfirmResult> ConfirmAsync(ConfirmRequest request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(ConfirmResult.Accept);
    }

    public ValueTask<SavePromptChoice> ConfirmSaveAsync(string documentName, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(SavePromptChoice.Save);
    }

    public ValueTask<QuitChoice> ConfirmQuitAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(QuitChoice.Quit);
    }
}

internal sealed class FakeTrayService : ITrayService
{
    public string? ToolTip { get; private set; }

    public CommandSet? Menu { get; private set; }

    public bool Visible { get; private set; }

    public void Show(string toolTip, CommandSet menu)
    {
        ToolTip = toolTip;
        Menu = menu;
        Visible = true;
    }

    public void Hide()
    {
        Visible = false;
    }
}
