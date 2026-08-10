using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace NutBoltSort
{
    /// <summary>
    /// Controls the Shop full-screen UI in the Main Menu.
    ///
    /// Content area is intentionally empty — shop categories and items will
    /// be added in a future update. The coin section reads from PlayerWallet.
    ///
    /// Wire-up points for future shop content:
    ///   - Add item/tab child GameObjects inside EmptyContentArea.
    ///   - Call PlayerWallet.AddCoins / PlayerWallet.SpendCoins from purchase
    ///     callbacks — the coin display will refresh automatically via event.
    ///
    /// Animation: full-screen fade + slide, no popup scale/bounce.
    /// Do NOT call DOTween.KillAll() anywhere in this script.
    /// </summary>
    [AddComponentMenu("BoltShift/UI/Shop Popup")]
    [DisallowMultipleComponent]
    public sealed class ShopPopup : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        [Header("Overlay")]
        [Tooltip("Root GameObject toggled active/inactive.")]
        [SerializeField] private GameObject shopOverlay;
        [Tooltip("Full-overlay image that blocks Main Menu interaction.")]
        [SerializeField] private Image inputBlocker;

        [Header("Panel")]
        [Tooltip("The content RectTransform that slides in/out. Must have a CanvasGroup on the same object or its parent.")]
        [SerializeField] private RectTransform shopPanel;
        [SerializeField] private CanvasGroup   panelCanvasGroup;

        [Header("Buttons")]
        [SerializeField] private Button closeButton;

        [Header("Coin Section")]
        [Tooltip("Text that displays the player's current coin balance.")]
        [SerializeField] private TMP_Text coinAmountText;

        [Header("Booster Cards")]
        [SerializeField] private Button undoBoosterButton;
        [SerializeField] private int undoBoosterCost = 200;
        [SerializeField] private int undoBoosterAmount = 5;

        [SerializeField] private Button expandBoosterButton;
        [SerializeField] private int expandBoosterCost = 250;
        [SerializeField] private int expandBoosterAmount = 1;

        [SerializeField] private Button watchAdBoosterButton;
        [SerializeField] private int adBoosterUndoReward = 2;
        [SerializeField] private int adBoosterExpandReward = 1;

        [Header("IAP Products")]
        [SerializeField] private Button starterPackButton;
        [SerializeField] private Button removeAdsButton;
        [SerializeField] private Button coinPackSmallButton;
        [SerializeField] private Button coinPackMediumButton;
        [SerializeField] private Button coinPackLargeButton;

        [Header("Optional Icon")]
        [Tooltip("RectTransform of the shop icon inside the screen (no animation — kept for layout reference).")]
        [SerializeField] private RectTransform shopIconRect;

        [Header("Animation Timing")]
        [Tooltip("Duration of the full-screen fade-in and content slide-up.")]
        [SerializeField, Range(0.10f, 0.30f)] private float openDuration  = 0.20f;
        [Tooltip("Duration of the fade-out and content slide-down.")]
        [SerializeField, Range(0.10f, 0.25f)] private float closeDuration = 0.17f;
        [Tooltip("Pixels the content travels upward on open / downward on close.")]
        [SerializeField, Range(20f, 60f)]     private float slidePixels   = 40f;

        // ─────────────────────────────────────────────────────────────────────
        // State
        // ─────────────────────────────────────────────────────────────────────

        private Sequence  openSequence;
        private Sequence  closeSequence;
        private Sequence  closeBtnSequence;
        private Sequence  headerSequence;
        private bool      initialized;

        /// <summary>Resting anchoredPosition of shopPanel — cached once in Awake.</summary>
        private Vector2   contentRestPos;

        public bool IsOpen { get; private set; }

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            // Cache the designer-set rest position so slide math is always relative.
            if (shopPanel != null)
                contentRestPos = shopPanel.anchoredPosition;

            EnsureInitialized();
        }

        private void OnDestroy()
        {
            openSequence?.Kill();
            closeSequence?.Kill();
            closeBtnSequence?.Kill();
            headerSequence?.Kill();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Initialization
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Hides the screen instantly. Called by MainMenuManager.Start()
        /// so the screen starts hidden regardless of editor state.
        /// </summary>
        public void HideImmediate()
        {
            EnsureInitialized();
            IsOpen = false;
            if (shopOverlay != null) shopOverlay.SetActive(false);

            // Ensure the panel is at its rest position when hidden.
            if (shopPanel != null)
                shopPanel.anchoredPosition = contentRestPos;
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            BindButtons();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Button Binding
        // ─────────────────────────────────────────────────────────────────────

        private void BindButtons()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(OnCloseButtonPressed);
            }

            if (undoBoosterButton != null)
            {
                undoBoosterButton.onClick.RemoveAllListeners();
                undoBoosterButton.onClick.AddListener(OnUndoBoosterPressed);
            }

            if (expandBoosterButton != null)
            {
                expandBoosterButton.onClick.RemoveAllListeners();
                expandBoosterButton.onClick.AddListener(OnExpandBoosterPressed);
            }

            if (watchAdBoosterButton != null)
            {
                watchAdBoosterButton.onClick.RemoveAllListeners();
                watchAdBoosterButton.onClick.AddListener(OnWatchAdBoosterPressed);
            }

            if (starterPackButton != null)
            {
                starterPackButton.onClick.RemoveAllListeners();
                starterPackButton.onClick.AddListener(OnStarterPackPressed);
            }

            if (removeAdsButton != null)
            {
                removeAdsButton.onClick.RemoveAllListeners();
                removeAdsButton.onClick.AddListener(OnRemoveAdsPressed);
            }

            if (coinPackSmallButton != null)
            {
                coinPackSmallButton.onClick.RemoveAllListeners();
                coinPackSmallButton.onClick.AddListener(() => OnCoinPackPressed(PurchaseManager.ProductIds.CoinsSmall));
            }

            if (coinPackMediumButton != null)
            {
                coinPackMediumButton.onClick.RemoveAllListeners();
                coinPackMediumButton.onClick.AddListener(() => OnCoinPackPressed(PurchaseManager.ProductIds.CoinsMedium));
            }

            if (coinPackLargeButton != null)
            {
                coinPackLargeButton.onClick.RemoveAllListeners();
                coinPackLargeButton.onClick.AddListener(() => OnCoinPackPressed(PurchaseManager.ProductIds.CoinsLarge));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Shop Purchase Handlers
        // ─────────────────────────────────────────────────────────────────────

        private void OnUndoBoosterPressed()
        {
            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

            if (PlayerEconomy.Instance != null)
            {
                if (PlayerEconomy.Instance.SpendCoins(undoBoosterCost))
                {
                    PlayerEconomy.Instance.AddUndo(undoBoosterAmount);
                    RefreshCoinDisplay();
                }
            }
            else if (PlayerWallet.SpendCoins(undoBoosterCost))
            {
                RefreshCoinDisplay();
            }
        }

        private void OnExpandBoosterPressed()
        {
            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

            if (PlayerEconomy.Instance != null)
            {
                if (PlayerEconomy.Instance.SpendCoins(expandBoosterCost))
                {
                    PlayerEconomy.Instance.AddExpand(expandBoosterAmount);
                    RefreshCoinDisplay();
                }
            }
            else if (PlayerWallet.SpendCoins(expandBoosterCost))
            {
                RefreshCoinDisplay();
            }
        }

        private void OnWatchAdBoosterPressed()
        {
            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

            if (AdManager.Instance != null)
            {
                AdManager.Instance.ShowRewardedAd(
                    RewardType.ShopBooster,
                    onRewardGranted: () =>
                    {
                        PlayerEconomy.Instance?.AddUndo(adBoosterUndoReward);
                        PlayerEconomy.Instance?.AddExpand(adBoosterExpandReward);
                        RefreshCoinDisplay();
                    });
            }
            else
            {
                PlayerEconomy.Instance?.AddUndo(adBoosterUndoReward);
                PlayerEconomy.Instance?.AddExpand(adBoosterExpandReward);
                RefreshCoinDisplay();
            }
        }

        private void OnStarterPackPressed()
        {
            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

            PurchaseManager.Instance?.PurchaseStarterPack();
            RefreshCoinDisplay();
        }

        private void OnRemoveAdsPressed()
        {
            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

            PurchaseManager.Instance?.PurchaseRemoveAds();
        }

        private void OnCoinPackPressed(string productId)
        {
            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

            PurchaseManager.Instance?.PurchaseCoinPack(productId);
            RefreshCoinDisplay();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Open — full-screen fade + upward slide
        // ─────────────────────────────────────────────────────────────────────

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;

            EnsureInitialized();

            // Refresh coin balance every time the shop opens.
            RefreshCoinDisplay();

            // ── Step 1: Activate overlay (must come before any DOTween calls).
            if (shopOverlay != null) shopOverlay.SetActive(true);

            // ── Step 2: Kill any in-flight sequences.
            openSequence?.Kill();
            closeSequence?.Kill();
            headerSequence?.Kill();

            // ── Step 3: Set initial state — NO scale changes.
            //    Panel starts slightly below its rest position and fully transparent.
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha          = 0f;
                panelCanvasGroup.interactable   = false;
                panelCanvasGroup.blocksRaycasts = false;
            }

            if (shopPanel != null)
                shopPanel.anchoredPosition = new Vector2(
                    contentRestPos.x,
                    contentRestPos.y - slidePixels);   // start below → rises up

            // Prepare header elements for their delayed pop.
            PrepareHeaderElements();

            // ── Step 4: Sound.
            AudioManager.Play(SfxType.PopupOpen);

            // ── Step 5: Build open sequence.
            openSequence = DOTween.Sequence();

            // Fade the whole overlay from 0 → 1.
            if (panelCanvasGroup != null)
                openSequence.Join(
                    panelCanvasGroup.DOFade(1f, openDuration)
                                    .SetEase(Ease.OutCubic));

            // Slide content upward from (restY - slidePixels) → restY.
            if (shopPanel != null)
                openSequence.Join(
                    shopPanel.DOAnchorPos(contentRestPos, openDuration)
                             .SetEase(Ease.OutCubic));

            openSequence.OnComplete(() =>
            {
                if (panelCanvasGroup != null)
                {
                    panelCanvasGroup.interactable   = true;
                    panelCanvasGroup.blocksRaycasts = true;
                }

                // Small delayed pop for the close button and coin counter.
                PlayHeaderElementsAnimation();
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Close — fade + downward slide
        // ─────────────────────────────────────────────────────────────────────

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;

            // Block input immediately.
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.interactable   = false;
                panelCanvasGroup.blocksRaycasts = false;
            }

            openSequence?.Kill();
            closeSequence?.Kill();
            headerSequence?.Kill();

            closeSequence = DOTween.Sequence();

            // Fade from 1 → 0.
            if (panelCanvasGroup != null)
                closeSequence.Join(
                    panelCanvasGroup.DOFade(0f, closeDuration)
                                    .SetEase(Ease.InCubic));

            // Slide content downward by ~35px while fading.
            if (shopPanel != null)
                closeSequence.Join(
                    shopPanel.DOAnchorPos(
                                 new Vector2(contentRestPos.x, contentRestPos.y - 35f),
                                 closeDuration)
                             .SetEase(Ease.InCubic));

            closeSequence.OnComplete(() =>
            {
                // Reset to rest position ready for next open.
                if (shopPanel != null)
                    shopPanel.anchoredPosition = contentRestPos;

                if (shopOverlay != null) shopOverlay.SetActive(false);
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Close Button Press Feedback
        // ─────────────────────────────────────────────────────────────────────

        private void OnCloseButtonPressed()
        {
            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

            if (closeButton == null)
            {
                Close();
                return;
            }

            closeBtnSequence?.Kill();
            closeBtnSequence = DOTween.Sequence();

            Transform btn = closeButton.transform;

            // Quick press-down → release feedback (scale only on the button itself, not the panel).
            closeBtnSequence.Append(btn.DOScale(Vector3.one * 0.88f, 0.07f).SetEase(Ease.OutQuad));
            closeBtnSequence.Append(btn.DOScale(Vector3.one,          0.09f).SetEase(Ease.OutQuad));
            closeBtnSequence.OnComplete(Close);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Header Elements Animation
        // Small delayed fade + pop for Close button and Coin counter after open.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Sets Close button and Coin counter to alpha 0 before the open animation.</summary>
        private void PrepareHeaderElements()
        {
            SetElementAlpha(closeButton?.gameObject,    0f);
            SetElementAlpha(coinAmountText?.gameObject, 0f);
        }

        /// <summary>Fades in + lightly pops the close button and coin counter after the screen opens.</summary>
        private void PlayHeaderElementsAnimation()
        {
            headerSequence?.Kill();
            headerSequence = DOTween.Sequence();

            // Close button — fade in with micro scale-up pop.
            if (closeButton != null)
            {
                CanvasGroup btnCg = GetOrAddCanvasGroup(closeButton.gameObject);
                closeButton.transform.localScale = Vector3.one * 0.85f;

                headerSequence.Insert(0.04f, btnCg.DOFade(1f, 0.12f).SetEase(Ease.OutCubic));
                headerSequence.Insert(0.04f,
                    closeButton.transform.DOScale(Vector3.one, 0.14f).SetEase(Ease.OutBack));
            }

            // Coin counter — fade in slightly after the button.
            if (coinAmountText != null)
            {
                CanvasGroup coinCg = GetOrAddCanvasGroup(coinAmountText.gameObject);

                headerSequence.Insert(0.08f, coinCg.DOFade(1f, 0.12f).SetEase(Ease.OutCubic));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static void SetElementAlpha(GameObject go, float alpha)
        {
            if (go == null) return;
            CanvasGroup cg = GetOrAddCanvasGroup(go);
            cg.alpha = alpha;
        }

        private static CanvasGroup GetOrAddCanvasGroup(GameObject go)
        {
            CanvasGroup cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            return cg;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Coin Display
        // ─────────────────────────────────────────────────────────────────────

        private void RefreshCoinDisplay()
        {
            if (coinAmountText == null) return;
            coinAmountText.text = PlayerWallet.GetCoins().ToString("N0");
        }
    }
}
