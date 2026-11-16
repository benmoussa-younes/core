// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Tests;

/// <summary>
/// A controllable clock whose timers fire only when the test advances it, so a
/// debounce is deterministic and needs no sleeping.
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];

    /// <summary>Gets the current instant.</summary>
    public DateTimeOffset Now { get; private set; } = start;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Now;

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var timer = new ManualTimer(this, callback, state);
        _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock forward and fires every timer that is now due.</summary>
    /// <param name="by">The interval.</param>
    public void Advance(TimeSpan by)
    {
        Now += by;

        foreach (var timer in _timers.ToArray())
        {
            timer.FireIfDue();
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _time;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private DateTimeOffset? _due;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;
        private bool _disposed;

        internal ManualTimer(ManualTimeProvider time, TimerCallback callback, object? state)
        {
            _time = time;
            _callback = callback;
            _state = state;
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            _due = dueTime == Timeout.InfiniteTimeSpan ? null : _time.Now + dueTime;
            return true;
        }

        public void Dispose() => _disposed = true;

        public ValueTask DisposeAsync()
        {
            _disposed = true;
            return ValueTask.CompletedTask;
        }

        internal void FireIfDue()
        {
            if (_disposed || _due is not { } due || due > _time.Now)
            {
                return;
            }

            _due = _period == Timeout.InfiniteTimeSpan ? null : _time.Now + _period;
            _callback(_state);
        }
    }
}
