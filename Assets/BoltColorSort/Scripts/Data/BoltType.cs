namespace NutBoltSort
{
    /// <summary>
    /// Identifies the special role of a bolt in a level configuration.
    /// Normal bolts are standard 4-slot sort targets.
    /// Expandable bolts begin at a configurable capacity (0–4) and can be expanded one stage at a time.
    /// Locked bolts are initially unavailable and must be unlocked before they can receive nuts.
    /// </summary>
    public enum BoltType
    {
        Normal     = 0,
        Expandable = 1,
        Locked     = 2
    }
}
