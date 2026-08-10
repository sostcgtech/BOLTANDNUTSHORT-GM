using UnityEngine;

namespace NutBoltSort
{
    /// <summary>
    /// IAP-ready shop purchase manager.
    ///
    /// Currently a placeholder/mock — all purchase methods log a message and do
    /// NOT grant items or charge the player. This lets you build and wire up all
    /// UI and logic now, then connect real billing later in one place.
    ///
    /// HOW TO CONNECT REAL IAP LATER:
    ///   1. Add the Google Play Billing Unity plugin.
    ///   2. Replace the placeholder body of each method below with a real
    ///      IStoreController.InitiatePurchase(productId) call.
    ///   3. In your purchase-success callback, call the corresponding grant
    ///      method (GrantStarterPack, GrantRemoveAds, GrantCoinPack) here.
    ///   4. The Remove Ads button in both Main Menu and Shop already call
    ///      PurchaseManager.Instance.PurchaseRemoveAds() — no other changes needed.
    ///
    /// DEVELOPMENT SIMULATION:
    ///   In DEVELOPMENT_BUILD or UNITY_EDITOR, coin-pack purchases immediately grant
    ///   coins so you can test the economy without a real device. Disable this before
    ///   submitting to the store.
    /// </summary>
    [AddComponentMenu("BoltShift/Shop/Purchase Manager")]
    [DisallowMultipleComponent]
    public sealed class PurchaseManager : MonoBehaviour, IShopPurchaseProvider
    {
        // ─────────────────────────────────────────────────────────────────────
        // Singleton
        // ─────────────────────────────────────────────────────────────────────

        public static PurchaseManager Instance { get; private set; }

        public static PurchaseManager EnsureInstance(GameObject host)
        {
            if (Instance != null) return Instance;
            Instance = host.GetComponent<PurchaseManager>();
            if (Instance == null) Instance = host.AddComponent<PurchaseManager>();
            return Instance;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Product IDs — change ONLY here; call sites stay the same
        // ─────────────────────────────────────────────────────────────────────

        public static class ProductIds
        {
            public const string StarterPack  = "starter_pack";
            public const string RemoveAds    = "remove_ads";
            public const string CoinsSmall   = "coins_small";
            public const string CoinsMedium  = "coins_medium";
            public const string CoinsLarge   = "coins_large";
        }

        // ─────────────────────────────────────────────────────────────────────
        // Starter Pack Reward Configuration
        // Edit these values to match whatever you promise in your store listing.
        // ─────────────────────────────────────────────────────────────────────

        [Header("Starter Pack Rewards")]
        [SerializeField, Min(0)] private int starterPackCoins  = 5000;
        [SerializeField, Min(0)] private int starterPackUndo   = 10;
        [SerializeField, Min(0)] private int starterPackExpand = 3;

        // ─────────────────────────────────────────────────────────────────────
        // Coin Pack Reward Configuration
        // ─────────────────────────────────────────────────────────────────────

        [Header("Coin Pack Amounts")]
        [SerializeField, Min(0)] private int coinPackSmallAmount  = 2500;
        [SerializeField, Min(0)] private int coinPackMediumAmount = 6000;
        [SerializeField, Min(0)] private int coinPackLargeAmount  = 15000;

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ═════════════════════════════════════════════════════════════════════
        // IShopPurchaseProvider — Placeholder Implementations
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc/>
        public void PurchaseStarterPack()
        {
            Debug.Log("[Shop] IAP placeholder: Starter Pack — will connect to Google Play Billing.");

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // DEVELOPMENT ONLY — simulates a successful purchase for testing.
            Debug.LogWarning("[Shop] DEVELOPMENT ONLY — simulating Starter Pack purchase.");
            GrantStarterPack();
#endif
        }

        /// <inheritdoc/>
        public void PurchaseRemoveAds()
        {
            Debug.Log("[Shop] IAP placeholder: Remove Ads — will connect to Google Play Billing.");

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.LogWarning("[Shop] DEVELOPMENT ONLY — simulating Remove Ads purchase.");
            GrantRemoveAds();
#endif
        }

        /// <inheritdoc/>
        public void PurchaseCoinPack(string productId)
        {
            Debug.Log($"[Shop] IAP placeholder: {productId} — will connect to Google Play Billing.");

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.LogWarning($"[Shop] DEVELOPMENT ONLY — simulating coin pack purchase: {productId}");
            GrantCoinPack(productId);
#endif
        }

        /// <inheritdoc/>
        public void RestorePurchases()
        {
            Debug.Log("[Shop] IAP placeholder: Restore Purchases — will connect to Google Play Billing.");

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.LogWarning("[Shop] DEVELOPMENT ONLY — restore purchases not simulated.");
#endif
        }

        // ═════════════════════════════════════════════════════════════════════
        // Grant Methods — called by real IAP success callbacks when billing is live
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Grants starter pack rewards ONCE after a confirmed IAP success.
        /// Call this from your IAP purchase-success callback.
        /// </summary>
        public void GrantStarterPack()
        {
            var eco = PlayerEconomy.Instance;
            if (eco == null)
            {
                Debug.LogError("[Shop] GrantStarterPack: PlayerEconomy not ready.");
                return;
            }

            eco.AddCoins(starterPackCoins);
            eco.AddUndo(starterPackUndo);
            eco.AddExpand(starterPackExpand);

            Debug.Log($"[Shop] Starter Pack granted: +{starterPackCoins} coins, " +
                      $"+{starterPackUndo} undo, +{starterPackExpand} expand.");
        }

        /// <summary>
        /// Activates Remove Ads after a confirmed IAP success.
        /// Saves state, hides banner, disables interstitials.
        /// Rewarded ads remain available.
        /// </summary>
        public void GrantRemoveAds()
        {
            AdManager.Instance?.SetAdsRemoved(true);
            Debug.Log("[Shop] Remove Ads granted.");
        }

        /// <summary>
        /// Grants coins for the specified coin pack after a confirmed IAP success.
        /// </summary>
        public void GrantCoinPack(string productId)
        {
            int amount = productId switch
            {
                ProductIds.CoinsSmall  => coinPackSmallAmount,
                ProductIds.CoinsMedium => coinPackMediumAmount,
                ProductIds.CoinsLarge  => coinPackLargeAmount,
                _                      => 0,
            };

            if (amount <= 0)
            {
                Debug.LogWarning($"[Shop] Unknown coin pack product ID: {productId}");
                return;
            }

            PlayerEconomy.Instance?.AddCoins(amount);
            Debug.Log($"[Shop] Coin pack granted: +{amount} coins ({productId}).");
        }
    }
}
