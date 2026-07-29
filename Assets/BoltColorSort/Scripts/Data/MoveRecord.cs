namespace NutBoltSort
{
    /// <summary>
    /// Immutable snapshot of one completed player transfer, stored by UndoManager.
    /// Only the topmost completed move can be reversed.
    /// </summary>
    public struct MoveRecord
    {
        /// <summary>Index into LevelManager.ActiveBolts for the bolt that was the move SOURCE.</summary>
        public int sourceBoltIndex;

        /// <summary>Index into LevelManager.ActiveBolts for the bolt that was the move DESTINATION.</summary>
        public int destinationBoltIndex;

        /// <summary>Color of all nuts that were transferred (all nuts in one group share the same color).</summary>
        public NutColor movedColor;

        /// <summary>Exact number of nuts that were moved. Undo returns exactly this many.</summary>
        public int movedNutCount;

        /// <summary>
        /// True if the source bolt became complete AFTER this move
        /// (i.e., removing the top group exposed a completed lower group that then got the cap).
        /// </summary>
        public bool sourceWasCompletedAfter;

        /// <summary>True if the destination bolt became complete as a direct result of this move.</summary>
        public bool destinationWasCompletedAfter;
    }
}
