// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>
/// One text prompt: a title, a field label, a starting value and the button
/// labels. The presenter validates the returned text; the prompt itself never
/// blocks on a rule.
/// </summary>
/// <param name="Title">The dialog title.</param>
/// <param name="Label">The field label.</param>
/// <param name="Value">The starting value.</param>
/// <param name="ConfirmLabel">The confirm button label.</param>
/// <param name="CancelLabel">The cancel button label.</param>
/// <param name="IsPassword">Whether the field masks its value.</param>
public sealed record PromptRequest(
    string Title,
    string Label,
    string Value = "",
    string ConfirmLabel = "OK",
    string CancelLabel = "Cancel",
    bool IsPassword = false)
{
    /// <summary>Formats the request without leaking a password value.</summary>
    /// <returns>A diagnostic string.</returns>
    public override string ToString() =>
        $"PromptRequest {{ Title = {Title}, Label = {Label}, Value = {(IsPassword ? "[redacted]" : Value)}, " +
        $"ConfirmLabel = {ConfirmLabel}, CancelLabel = {CancelLabel}, IsPassword = {IsPassword} }}";
}
