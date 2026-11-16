// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text.Json.Serialization;
using Core.Mvp.Models;

namespace Core.UI.Chrome;

/// <summary>The source-generated JSON contract for <see cref="WindowPlacement"/>.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WindowPlacement))]
internal sealed partial class WindowPlacementJsonContext : JsonSerializerContext;
