using System.Collections.Generic;
using UnityEngine;

namespace NutBoltSort
{
    /// <summary>
    /// Maintains a stack of completed player moves and provides the top record for reversal.
    /// Only the most recent successful transfer can be undone.
    /// Records are added by GameManager after each successful transfer animation completes.
    /// </summary>
    public class UndoManager : MonoBehaviour
    {
        private readonly Stack<MoveRecord> _history = new Stack<MoveRecord>();

        /// <summary>True when at least one move can be reversed.</summary>
        public bool CanUndo => _history.Count > 0;

        /// <summary>Current depth of the undo history (informational).</summary>
        public int HistoryDepth => _history.Count;

        /// <summary>
        /// Records a completed move.  Called by GameManager after the full transfer animation finishes.
        /// </summary>
        public void RecordMove(MoveRecord record)
        {
            _history.Push(record);
        }

        /// <summary>
        /// Removes and returns the most recent move record so GameManager can reverse it.
        /// Returns false if history is empty.
        /// </summary>
        public bool TryPopLastMove(out MoveRecord record)
        {
            if (_history.Count == 0)
            {
                record = default;
                return false;
            }
            record = _history.Pop();
            return true;
        }

        /// <summary>Discards all history.  Called on Restart and Next Level.</summary>
        public void Clear() => _history.Clear();
    }
}
