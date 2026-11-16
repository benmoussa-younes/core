// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Capabilities;
using Core.Results;

namespace Core.Tests;

public sealed class EchoCapability : ICapability<string, string>
{
    public ValueTask<Result<string>> InvokeAsync(
        string request,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Result<string>.Success(request));
    }
}
