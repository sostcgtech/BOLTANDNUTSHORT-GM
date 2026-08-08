using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace NutBoltSort
{
    /// <summary>
    /// Controls the Remove Ads popup in the Main Menu.
    ///
    /// The Purchase and Restore Purchase buttons are honest placeholders —
    /// they show a toast message and do not grant or simulate any purchase state.
    /// Wire IAP here when the store is ready.
    ///
    /// Wire-up points for future IAP integration:
    ///   - OnRemoveAdsPurchasePressed()  → replace body with IAP purchase call.
    ///   - OnRestorePurchasePressed()    → replace body with IAP restore call.
    ///   - Add a "removeAdsOwned" PlayerPrefs read in Open() to auto-close if
    ///     the player already owns the product.
    ///
    /// Do NOT call DOTween.KillAll() anywhere in this script.
    /// </summary>
    [AddComponentMenu("BoltShift/UI/Remove Ads Popup")]
    [DisallowMultipleComponent]
    public sealed class RemoveAdsPopup : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Overlay
        // ─────────────────────────────────────────────────────────────────────

        [Header("Overlay")]
        [Tooltip("Root GameObject toggled active/inactive.")]
        [SerializeField] private GameObject removeAdsOverlay;
        [SerializeField] private Image inputBlocker;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Panel
        // ─────────────────────────────────────────────────────────────────────

        [Header("Panel")]
        [SerializeField] private RectTransform removeAdsPanel;
        [SerializeField] private CanvasGroup   panelCanvasGroup;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Optional Icon Animation
        // ─────────────────────────────────────────────────────────────────────

        [Header("Icon Animation")]
        [Tooltip("RectTransform of the Remove Ads icon inside the popup — " +
                 "plays a one-shot pop when the popup opens.")]
        [SerializeField] private RectTransform removeAdsIconRect;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Buttons
        // ─────────────────────────────────────────────────────────────────────

        [Header("Buttons")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Button purchaseButton;
        [SerializeField] private Button restorePurchaseButton;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Text
        // ─────────────────────────────────────────────────────────────────────

        [Header("Text")]
        [SerializeField] private TMP_Text priceText;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Toast
        // ─────────────────────────────────────────────────────────────────────

        [Header("Toast")]
        [SerializeField] private TMP_Text toastLabel;
        [SerializeField, Range(0.8f, 3.0f)] private float toastDuration = 1.8f;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Animation Timing
        // ─────────────────────────────────────────────────────────────────────

        [Header("Animation Timing")]
        [SerializeField, Range(0.16f, 0.36f)] private float openDuration  = 0.27f;
        [SerializeField, Range(0.14f, 0.26f)] private float closeDuration = 0.22f;

        // ─────────────────────────────────────────────────────────────────────
        // State
        // ─────────────────────────────────────────────────────────────────────

        private Sequence   openSequence;
        private Sequence   closeSequence;
        private Sequence   closeBtnSequence;
        private Coroutine  toastCoroutine;
        private bool       initialized;

        public bool IsOpen { get; private set; }

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            openSequence?.Kill();
            closeSequence?.Kill();
            closeBtnSequence?.Kill();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Initialization
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Hides the popup instantly. Called by MainMenuManager.Start()
        /// so the popup starts hidden regardless of its editor state.
        /// </summary>
        public void HideImmediate()
        {
            EnsureInitialized();
            IsOpen = false;

            if (removeAdsOverlay != null)
                removeAdsOverlay.SetActive(false);
            else
                Debug.LogWarning("[RemoveAdsPopup] 'removeAdsOverlay' is not assigned in the Inspector!", this);

            if (toastLabel != null) toastLabel.gameObject.SetActive(false);
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

            if (purchaseButton != null)
            {
                purchaseButton.onClick.RemoveAllListeners();
                purchaseButton.onClick.AddListener(OnRemoveAdsPurchasePressed);
            }

            if (restorePurchaseButton != null)
            {
                restorePurchaseButton.onClick.RemoveAllListeners();
                restorePurchaseButton.onClick.AddListener(OnRestorePurchasePressed);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Open
        // ─────────────────────────────────────────────────────────────────────

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;

            EnsureInitialized();

            // Validate required references before doing anything visible.
            if (removeAdsOverlay == null)
            {
                Debug.LogError("[RemoveAdsPopup] Cannot open — 'removeAdsOverlay' is not assigned in the Inspector!", this);
                IsOpen = false;
                return;
            }
            if (removeAdsPanel == null)
                Debug.LogWarning("[RemoveAdsPopup] 'removeAdsPanel' is not assigned — panel animation will be skipped.", this);
            if (panelCanvasGroup == null)
                Debug.LogWarning("[RemoveAdsPopup] 'panelCanvasGroup' is not assigned — fade animation will be skipped.", this);

            if (removeAdsOverlay != null) removeAdsOverlay.SetActive(true);

            // ── Step 2: Kill any in-flight tweens from a previous open/close.
            openSequence?.Kill();
            closeSequence?.Kill();

            // ── Step 3: Reset panel to start state (invisible, scaled down).
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha          = 0f;
                panelCanvasGroup.interactable   = false;
                panelCanvasGroup.blocksRaycasts = false;
            }

            if (removeAdsPanel != null)
                removeAdsPanel.localScale = Vector3.one * 0.75f;

            // Reset icon for bounce.
            if (removeAdsIconRect != null)
                removeAdsIconRect.localScale = Vector3.one * 0.8f;

            // ── Step 4: Play sound (safe — AudioManager.Play is null-safe).
            AudioManager.Play(SfxType.PopupOpen);

            // ── Step 5: Build and start the open sequence.
            openSequence = DOTween.Sequence();

            // Fade in.
            if (panelCanvasGroup != null)
                openSequence.Join(panelCanvasGroup.DOFade(1f, openDuration));

            // Scale: 0.75 → 1.05 (OutBack overshoot) → 1.0 (OutCubic settle).
            if (removeAdsPanel != null)
            {
                float overshootDur = openDuration * 0.75f;
                float settleDur    = openDuration * 0.25f;

                openSequence.Join(
                    removeAdsPanel.DOScale(Vector3.one * 1.05f, overshootDur)
                                  .SetEase(Ease.OutBack)
                                  .OnComplete(() =>
                                      removeAdsPanel.DOScale(Vector3.one, settleDur)
                                                    .SetEase(Ease.OutCubic)));
            }

            openSequence.OnComplete(() =>
            {
                if (panelCanvasGroup != null)
                {
                    panelCanvasGroup.interactable   = true;
                    panelCanvasGroup.blocksRaycasts = true;
                }

                // Icon pop: 0.8 → 1.1 → 1.0 — fires once per open.
                PlayIconPopAnimation();
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Close
        // ─────────────────────────────────────────────────────────────────────

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;

            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.interactable   = false;
                panelCanvasGroup.blocksRaycasts = false;
            }

            openSequence?.Kill();
            closeSequence?.Kill();

            closeSequence = DOTween.Sequence();

            // Scale: 1.0 → 1.03 (slight anticipation) → 0.8 (shrink out).
            if (removeAdsPanel != null)
            {
                closeSequence.Append(
                    removeAdsPanel.DOScale(Vector3.one * 1.03f, closeDuration * 0.20f)
                                  .SetEase(Ease.OutQuad));

                closeSequence.Append(
                    removeAdsPanel.DOScale(Vector3.one * 0.80f, closeDuration * 0.80f)
                                  .SetEase(Ease.InCubic));
            }

            // Simultaneous fade.
            if (panelCanvasGroup != null)
                closeSequence.Join(panelCanvasGroup.DOFade(0f, closeDuration));

            closeSequence.OnComplete(() =>
            {
                if (removeAdsOverlay != null) removeAdsOverlay.SetActive(false);
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

            Transform btnTransform = closeButton.transform;

            // 1.0 → 0.88 → 1.0
            closeBtnSequence.Append(
                btnTransform.DOScale(Vector3.one * 0.88f, 0.08f)
                            .SetEase(Ease.OutQuad));

            closeBtnSequence.Append(
                btnTransform.DOScale(Vector3.one, 0.10f)
                            .SetEase(Ease.OutBack));

            closeBtnSequence.OnComplete(Close);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Icon Pop
        // ─────────────────────────────────────────────────────────────────────

        private void PlayIconPopAnimation()
        {
            if (removeAdsIconRect == null) return;

            // 0.8 → 1.1 → 1.0 — fires once when the popup finishes opening.
            removeAdsIconRect.DOScale(Vector3.one * 1.1f, 0.14f)
                             .SetEase(Ease.OutBack)
                             .OnComplete(() =>
                                 removeAdsIconRect.DOScale(Vector3.one, 0.10f)
                                                  .SetEase(Ease.OutCubic));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Purchase Placeholders
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Placeholder for future IAP integration.
        /// Replace the body of this method when an IAP SDK is connected.
        /// Do NOT grant Remove Ads ownership here yet.
        /// </summary>
        public void OnRemoveAdsPurchasePressed()
        {
            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

            // TODO: Replace with IAP purchase call when ready.
            Debug.Log("[RemoveAdsPopup] Remove Ads purchase will be connected later.");
            ShowToast("Purchases will be available soon.");
        }

        /// <summary>
        /// Placeholder for future IAP restore.
        /// Replace the body of this method when an IAP SDK is connected.
        /// </summary>
        public void OnRestorePurchasePressed()
        {
            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

            // TODO: Replace with IAP restore call when ready.
            Debug.Log("[RemoveAdsPopup] Restore purchases will be connected later.");
            ShowToast("Restore will be available soon.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Toast
        // ─────────────────────────────────────────────────────────────────────

        private void ShowToast(string message)
        {
            if (toastLabel == null)
            {
                Debug.Log($"[RemoveAdsPopup] {message}");
                return;
            }

            if (toastCoroutine != null)
                StopCoroutine(toastCoroutine);

            toastCoroutine = StartCoroutine(ToastRoutine(message));
        }

        private IEnumerator ToastRoutine(string message)
        {
            toastLabel.text = message;
            toastLabel.gameObject.SetActive(true);

            CanvasGroup cg = toastLabel.GetComponent<CanvasGroup>()
                          ?? toastLabel.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.DOFade(1f, 0.15f);

            yield return new WaitForSeconds(toastDuration);

            yield return cg.DOFade(0f, 0.20f).WaitForCompletion();
            toastLabel.gameObject.SetActive(false);
            toastCoroutine = null;
        }
    }
}
