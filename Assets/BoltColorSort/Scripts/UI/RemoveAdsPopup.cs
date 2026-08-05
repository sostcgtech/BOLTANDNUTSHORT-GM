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
    /// The Purchase button is an honest placeholder — it shows a toast message
    /// and does not grant or simulate any purchase state.
    /// Wire IAP here when the store is ready.
    ///
    /// Do NOT call DOTween.KillAll() anywhere in this script.
    /// </summary>
    [AddComponentMenu("BoltShift/UI/Remove Ads Popup")]
    [DisallowMultipleComponent]
    public sealed class RemoveAdsPopup : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        [Header("Overlay")]
        [Tooltip("Root GameObject toggled active/inactive.")]
        [SerializeField] private GameObject removeAdsOverlay;
        [SerializeField] private Image inputBlocker;

        [Header("Panel")]
        [SerializeField] private RectTransform removeAdsPanel;
        [SerializeField] private CanvasGroup   panelCanvasGroup;

        [Header("Buttons")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Button purchaseButton;

        [Header("Text")]
        [SerializeField] private TMP_Text priceText;

        [Header("Toast")]
        [SerializeField] private TMP_Text toastLabel;
        [SerializeField, Range(0.8f, 3.0f)] private float toastDuration = 1.8f;

        [Header("Animation Timing")]
        [SerializeField, Range(0.16f, 0.32f)] private float openDuration  = 0.24f;
        [SerializeField, Range(0.14f, 0.24f)] private float closeDuration = 0.20f;

        // ─────────────────────────────────────────────────────────────────────
        // State
        // ─────────────────────────────────────────────────────────────────────

        private Sequence   openSequence;
        private Sequence   closeSequence;
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

        public void HideImmediate()
        {
            EnsureInitialized();
            IsOpen = false;
            if (removeAdsOverlay != null) removeAdsOverlay.SetActive(false);
            if (toastLabel       != null) toastLabel.gameObject.SetActive(false);
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            BindButtons();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Binding
        // ─────────────────────────────────────────────────────────────────────

        private void BindButtons()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Close);
            }

            if (purchaseButton != null)
            {
                purchaseButton.onClick.RemoveAllListeners();
                purchaseButton.onClick.AddListener(OnRemoveAdsPurchasePressed);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Open / Close
        // ─────────────────────────────────────────────────────────────────────

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;

            EnsureInitialized();

            if (removeAdsOverlay != null) removeAdsOverlay.SetActive(true);

            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha          = 0f;
                panelCanvasGroup.interactable   = false;
                panelCanvasGroup.blocksRaycasts = false;
            }

            if (removeAdsPanel != null)
                removeAdsPanel.localScale = Vector3.one * 0.82f;

            AudioManager.Play(SfxType.PopupOpen);

            openSequence?.Kill();
            closeSequence?.Kill();

            openSequence = DOTween.Sequence();

            if (panelCanvasGroup != null)
                openSequence.Join(panelCanvasGroup.DOFade(1f, openDuration));

            if (removeAdsPanel != null)
            {
                // 0.82 → 1.05 → 1.0
                openSequence.Join(
                    removeAdsPanel.DOScale(Vector3.one * 1.05f, openDuration * 0.75f)
                                  .SetEase(Ease.OutBack)
                                  .OnComplete(() =>
                                      removeAdsPanel.DOScale(Vector3.one, openDuration * 0.25f)
                                                    .SetEase(Ease.OutCubic)));
            }

            openSequence.OnComplete(() =>
            {
                if (panelCanvasGroup != null)
                {
                    panelCanvasGroup.interactable   = true;
                    panelCanvasGroup.blocksRaycasts = true;
                }
            });
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;

            AudioManager.Play(SfxType.ButtonClick);

            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.interactable   = false;
                panelCanvasGroup.blocksRaycasts = false;
            }

            openSequence?.Kill();
            closeSequence?.Kill();

            closeSequence = DOTween.Sequence();

            if (panelCanvasGroup != null)
                closeSequence.Join(panelCanvasGroup.DOFade(0f, closeDuration));

            if (removeAdsPanel != null)
                closeSequence.Join(
                    removeAdsPanel.DOScale(Vector3.one * 0.85f, closeDuration)
                                  .SetEase(Ease.InCubic));

            closeSequence.OnComplete(() =>
            {
                if (removeAdsOverlay != null) removeAdsOverlay.SetActive(false);
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Purchase Placeholder
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
            Debug.Log("[RemoveAdsPopup] Purchase pressed — IAP not yet implemented.");
            ShowToast("Purchases will be available soon.");
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

        // ─────────────────────────────────────────────────────────────────────
        // Cleanup
        // ─────────────────────────────────────────────────────────────────────

        private void OnDestroy()
        {
            openSequence?.Kill();
            closeSequence?.Kill();
        }
    }
}
