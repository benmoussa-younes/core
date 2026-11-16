// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text.Json.Serialization;

namespace Core.Mvp.Services;

/// <summary>The source-generated JSON contract for <see cref="AppearanceSettings"/>.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppearanceSettings))]
internal sealed partial class AppearanceJsonContext : JsonSerializerContext;
