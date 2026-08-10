namespace NutBoltSort
{
    /// <summary>
    /// Identifies the context in which a rewarded ad is shown.
    /// Pass this to AdManager.ShowRewardedAd() so the reward callback
    /// knows exactly what to grant without needing separate ad objects.
    /// </summary>
    public enum RewardType
    {
        /// <summary>Undo Watch-Ad button — refills Undo uses.</summary>
        Undo,

        /// <summary>Expand Watch-Ad button — refills Expand uses.</summary>
        Expand,

        /// <summary>Win Panel "Claim X2" button — doubles the coin reward.</summary>
        WinDoubleCoins,

        /// <summary>Shop "Watch Ad" booster card — gives +2 Undo +1 Expand.</summary>
        ShopBooster,
    }
}
