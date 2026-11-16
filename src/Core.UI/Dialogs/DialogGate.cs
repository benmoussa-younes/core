// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.UI.Dialogs;

/// <summary>
/// Serializes the shell's overlays: one confirmation or prompt is visible at a
/// time. The gate is shared by <see cref="DialogService"/> and
/// <see cref="PromptService"/>, so a confirm and a prompt cannot be stacked.
/// </summary>
internal sealed class DialogGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Waits for the overlay slot; dispose the lease to release it.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The lease that releases the slot.</returns>
    public async ValueTask<Lease> EnterAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(_gate);
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>Releases the overlay slot once.</summary>
    internal sealed class Lease : IDisposable
    {
        private SemaphoreSlim? _gate;

        public Lease(SemaphoreSlim gate)
        {
            _gate = gate;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _gate, null)?.Release();
        }
    }
}
