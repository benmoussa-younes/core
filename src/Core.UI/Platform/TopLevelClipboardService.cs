// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Core.Mvp.Services;

namespace Core.UI.Platform;

/// <summary>
/// The <see cref="IClipboardService"/> over Avalonia's <see cref="IClipboard"/>:
/// text through the clipboard text helpers and images as PNG bytes. A
/// <see langword="null"/> clipboard (a top level without one) is a no-op.
/// </summary>
public sealed class TopLevelClipboardService : IClipboardService
{
    private readonly IClipboard? _clipboard;

    /// <summary>Initializes a new instance of the <see cref="TopLevelClipboardService"/> class.</summary>
    /// <param name="clipboard">The top level's clipboard, or <see langword="null"/>.</param>
    public TopLevelClipboardService(IClipboard? clipboard)
    {
        _clipboard = clipboard;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public async ValueTask SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        Dispatcher.UIThread.VerifyAccess();

        if (_clipboard is null)
        {
            return;
        }

        await _clipboard.SetTextAsync(text).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dispatcher.UIThread.VerifyAccess();

        return _clipboard is null ? null : await _clipboard.TryGetTextAsync().ConfigureAwait(true);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="png"/> is <see langword="null"/>.</exception>
    public async ValueTask SetImageAsync(byte[] png, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(png);
        cancellationToken.ThrowIfCancellationRequested();
        Dispatcher.UIThread.VerifyAccess();

        if (_clipboard is null)
        {
            return;
        }

        using var stream = new MemoryStream(png, writable: false);

        // Ownership of the decoded bitmap transfers to the clipboard, which may
        // hold it after the call returns; disposing it here would hand over a
        // disposed image (the headless clipboard does exactly that).
        var bitmap = new Bitmap(stream);
        await _clipboard.SetBitmapAsync(bitmap).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]?> GetImageAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dispatcher.UIThread.VerifyAccess();

        if (_clipboard is null)
        {
            return null;
        }

        var bitmap = await _clipboard.TryGetBitmapAsync().ConfigureAwait(true);

        if (bitmap is null)
        {
            return null;
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }
}
