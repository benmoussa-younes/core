// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Mvp.Services;

/// <summary>
/// The system clipboard: text and PNG images. The service is headless so a
/// presenter can copy and paste without naming an Avalonia type.
/// </summary>
public interface IClipboardService : IService
{
    /// <summary>Puts text on the clipboard.</summary>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">The token used to cancel the request.</param>
    ValueTask SetTextAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Reads the clipboard text.</summary>
    /// <param name="cancellationToken">The token used to cancel the request.</param>
    /// <returns>The text, or <see langword="null"/> when the clipboard holds none.</returns>
    ValueTask<string?> GetTextAsync(CancellationToken cancellationToken = default);

    /// <summary>Puts a PNG image on the clipboard.</summary>
    /// <param name="png">The encoded PNG bytes.</param>
    /// <param name="cancellationToken">The token used to cancel the request.</param>
    ValueTask SetImageAsync(byte[] png, CancellationToken cancellationToken = default);

    /// <summary>Reads the clipboard image as PNG.</summary>
    /// <param name="cancellationToken">The token used to cancel the request.</param>
    /// <returns>The encoded PNG bytes, or <see langword="null"/> when the clipboard holds no image.</returns>
    ValueTask<byte[]?> GetImageAsync(CancellationToken cancellationToken = default);
}
