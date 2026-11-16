// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>
/// A confirmation question and its button labels.
/// </summary>
/// <param name="Title">The question, shown as the heading.</param>
/// <param name="Body">The explanatory body, or the empty string.</param>
/// <param name="ConfirmLabel">The confirming button's label.</param>
/// <param name="AlternativeLabel">The alternative button's label, or the empty string to hide it.</param>
/// <param name="CancelLabel">The cancel button's label.</param>
/// <param name="Destructive">Whether the confirming action is destructive, so it can be toned as such.</param>
public sealed record ConfirmRequest(
    string Title,
    string Body = "",
    string ConfirmLabel = "OK",
    string AlternativeLabel = "",
    string CancelLabel = "Cancel",
    bool Destructive = false);
