namespace NutBoltSort
{
    /// <summary>
    /// Contract for the shop purchase provider.
    ///
    /// Currently implemented by PurchaseManager as a placeholder/mock.
    /// When Google Play Billing is ready, swap PurchaseManager's body with
    /// real IAP calls — all call sites remain identical.
    /// </summary>
    public interface IShopPurchaseProvider
    {
        /// <summary>Initiates the Starter Pack IAP flow.</summary>
        void PurchaseStarterPack();

        /// <summary>Initiates the Remove Ads IAP flow.</summary>
        void PurchaseRemoveAds();

        /// <summary>
        /// Initiates an IAP flow for the specified coin pack product ID.
        /// Use the product ID constants from <see cref="PurchaseManager.ProductIds"/>.
        /// </summary>
        void PurchaseCoinPack(string productId);

        /// <summary>Initiates the platform restore-purchases flow.</summary>
        void RestorePurchases();
    }
}
