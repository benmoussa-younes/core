// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Text;

/// <summary>
/// A bounded undo/redo history. It stores the state <em>before</em> each change,
/// so an undo restores exactly what the operator saw; a coalesced change keeps the
/// earlier snapshot, which is how a run of keystrokes becomes one undo step.
/// </summary>
/// <typeparam name="T">The immutable state type; it must be a reference type so an empty slot is <see langword="null"/>.</typeparam>
public sealed class UndoRedoHistory<T>
    where T : class
{
    private readonly int _capacity;
    private readonly List<T> _undo = [];
    private readonly List<T> _redo = [];

    /// <summary>Initializes a history.</summary>
    /// <param name="capacity">The maximum number of undo steps kept; must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than one.</exception>
    public UndoRedoHistory(int capacity = 200)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _capacity = capacity;
    }

    /// <summary>Gets whether an undo is possible.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Gets whether a redo is possible.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Gets the number of undo steps.</summary>
    public int UndoCount => _undo.Count;

    /// <summary>Gets the number of redo steps.</summary>
    public int RedoCount => _redo.Count;

    /// <summary>Records the state before a change.</summary>
    /// <param name="before">The state before the change.</param>
    /// <param name="coalesce">Whether the change continues the previous one; a coalesced change keeps the earlier snapshot.</param>
    /// <exception cref="ArgumentNullException"><paramref name="before"/> is <see langword="null"/>.</exception>
    public void Record(T before, bool coalesce = false)
    {
        ArgumentNullException.ThrowIfNull(before);

        if (!coalesce)
        {
            _undo.Add(before);

            if (_undo.Count > _capacity)
            {
                _undo.RemoveAt(0);
            }
        }

        _redo.Clear();
    }

    /// <summary>Undoes a change.</summary>
    /// <param name="current">The current state, which becomes redoable.</param>
    /// <returns>The state to restore, or <see langword="null"/> when there is nothing to undo.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="current"/> is <see langword="null"/>.</exception>
    public T? Undo(T current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (_undo.Count == 0)
        {
            return null;
        }

        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(current);
        return previous;
    }

    /// <summary>Redoes a change.</summary>
    /// <param name="current">The current state, which becomes undoable again.</param>
    /// <returns>The state to restore, or <see langword="null"/> when there is nothing to redo.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="current"/> is <see langword="null"/>.</exception>
    public T? Redo(T current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (_redo.Count == 0)
        {
            return null;
        }

        var next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(current);
        return next;
    }

    /// <summary>Forgets every step, as when a new document is opened.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
