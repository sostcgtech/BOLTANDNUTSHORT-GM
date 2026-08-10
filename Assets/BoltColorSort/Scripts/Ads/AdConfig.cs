namespace NutBoltSort
{
    /// <summary>
    /// CENTRAL AD CONFIGURATION — ALL AD UNIT IDs LIVE HERE AND NOWHERE ELSE
    ///
    /// HOW TO SWITCH TEST → PRODUCTION:
    ///   1. Comment out the TEST block below (or remove it).
    ///   2. Fill in the PRODUCTION fields with your real AdMob ad unit IDs.
    ///   3. Replace the App ID in AndroidManifest.xml with your real App ID.
    ///   4. Done — no other scripts need changing.
    /// </summary>
    public static class AdConfig
    {
        // ──────────────────────────────────────────────────────────────────────
        // USE TEST ADS FLAG
        // Set to false when you enter your real production IDs below.
        // ──────────────────────────────────────────────────────────────────────
        public const bool UseTestAds = true;

        // Official Google AdMob Test Ad Unit IDs (Android)
        private const string TestBannerId       = "ca-app-pub-3940256099942544/6300978111";
        private const string TestInterstitialId = "ca-app-pub-3940256099942544/1033173712";
        private const string TestRewardedId     = "ca-app-pub-3940256099942544/5224354917";

        // Production Ad Unit IDs (replace with your real IDs before store release)
        private const string ProductionBannerId       = "";
        private const string ProductionInterstitialId = "";
        private const string ProductionRewardedId     = "";

        // Resolved IDs (uses Test IDs if UseTestAds is true or production IDs are empty)
        public static string BannerId       => (UseTestAds || string.IsNullOrEmpty(ProductionBannerId))       ? TestBannerId       : ProductionBannerId;
        public static string InterstitialId => (UseTestAds || string.IsNullOrEmpty(ProductionInterstitialId)) ? TestInterstitialId : ProductionInterstitialId;
        public static string RewardedId     => (UseTestAds || string.IsNullOrEmpty(ProductionRewardedId))     ? TestRewardedId     : ProductionRewardedId;

        // ──────────────────────────────────────────────────────────────────────
        // Interstitial Frequency Configuration
        // Change these values to tune how often interstitials appear.
        // interstitialStartLevel   — first level number that can show an interstitial.
        // interstitialEveryXLevels — show an interstitial every N levels after start.
        //   1 = every level, 2 = every other level, etc.
        // ──────────────────────────────────────────────────────────────────────
        public const int InterstitialStartLevel   = 4;
        public const int InterstitialEveryXLevels = 1;

        // ──────────────────────────────────────────────────────────────────────
        // PlayerPrefs Keys (internal — do not change without migrating saved data)
        // ──────────────────────────────────────────────────────────────────────
        public const string AdsRemovedKey = "BoltShift.AdsRemoved";
    }
}
