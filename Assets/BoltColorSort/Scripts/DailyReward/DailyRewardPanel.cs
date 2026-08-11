using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace NutBoltSort
{
    /// <summary>
    /// Popup controller for the 7-Day Daily Reward Panel in the Main Menu.
    ///
    /// Animation: scale pop + fade in/out matching standard popup style.
    /// Supports procedural UI construction fallback if Inspector references are unassigned.
    /// </summary>
    [AddComponentMenu("BoltShift/DailyReward/Daily Reward Panel")]
    [DisallowMultipleComponent]
    public sealed class DailyRewardPanel : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        [Header("Overlay & Panel")]
        [SerializeField] private GameObject overlayRoot;
        [SerializeField] private RectTransform panelRect;
        [SerializeField] private CanvasGroup panelCanvasGroup;

        [Header("Buttons")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Button claimButton;
        [SerializeField] private TMP_Text claimButtonText;
        [SerializeField] private Button claim2xButton;
        [SerializeField] private TMP_Text claim2xButtonText;

        [Header("Status & Timer")]
        [SerializeField] private TMP_Text statusTimerText;
        [SerializeField] private GameObject timerPillGroup;

        [Header("7 Day Cards (Day 1 - Day 7)")]
        [SerializeField] private DailyRewardDayCard[] dayCards = new DailyRewardDayCard[7];

        [Header("Animation Timing")]
        [SerializeField, Range(0.20f, 0.40f)] private float openDuration  = 0.28f;
        [SerializeField, Range(0.15f, 0.30f)] private float closeDuration = 0.20f;

        // ─────────────────────────────────────────────────────────────────────
        // State
        // ─────────────────────────────────────────────────────────────────────

        private Sequence openSequence;
        private Sequence closeSequence;
        private Sequence closeBtnSequence;
        private bool     initialized;

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

            if (DailyRewardManager.Instance != null)
                DailyRewardManager.Instance.OnStatusChanged -= RefreshUIState;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Initialization
        // ─────────────────────────────────────────────────────────────────────

        public void HideImmediate()
        {
            EnsureInitialized();
            IsOpen = false;
            if (overlayRoot != null) overlayRoot.SetActive(false);
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;

            EnsureUIHierarchy();
            BindButtons();

            if (DailyRewardManager.Instance != null)
                DailyRewardManager.Instance.OnStatusChanged += RefreshUIState;
        }

        private void BindButtons()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(OnCloseButtonPressed);
            }

            if (claimButton != null)
            {
                claimButton.onClick.RemoveAllListeners();
                claimButton.onClick.AddListener(OnClaimButtonPressed);
            }

            if (claim2xButton != null)
            {
                claim2xButton.onClick.RemoveAllListeners();
                claim2xButton.onClick.AddListener(OnClaim2xButtonPressed);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Open / Close Animation
        // ─────────────────────────────────────────────────────────────────────

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;

            EnsureInitialized();

            // Enable overlay first so StartCoroutine in RefreshUIState can run safely
            if (overlayRoot != null) overlayRoot.SetActive(true);

            DailyRewardManager.Instance?.RefreshRewardStatus();
            RefreshUIState();

            openSequence?.Kill();
            closeSequence?.Kill();

            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha          = 0f;
                panelCanvasGroup.interactable   = false;
                panelCanvasGroup.blocksRaycasts = false;
            }

            if (panelRect != null)
                panelRect.localScale = Vector3.one * 0.85f;

            AudioManager.Play(SfxType.PopupOpen);

            openSequence = DOTween.Sequence();

            if (panelCanvasGroup != null)
                openSequence.Join(panelCanvasGroup.DOFade(1f, openDuration));

            if (panelRect != null)
            {
                openSequence.Join(
                    panelRect.DOScale(Vector3.one * 1.04f, openDuration * 0.75f)
                             .SetEase(Ease.OutBack)
                             .OnComplete(() =>
                                 panelRect.DOScale(Vector3.one, openDuration * 0.25f)
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

            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.interactable   = false;
                panelCanvasGroup.blocksRaycasts = false;
            }

            openSequence?.Kill();
            closeSequence?.Kill();

            closeSequence = DOTween.Sequence();

            if (panelRect != null)
            {
                closeSequence.Append(
                    panelRect.DOScale(Vector3.one * 0.90f, closeDuration)
                             .SetEase(Ease.InCubic));
            }

            if (panelCanvasGroup != null)
                closeSequence.Join(panelCanvasGroup.DOFade(0f, closeDuration));

            closeSequence.OnComplete(() =>
            {
                if (overlayRoot != null) overlayRoot.SetActive(false);
            });
        }

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
            closeBtnSequence.Append(closeButton.transform.DOScale(Vector3.one * 0.88f, 0.08f).SetEase(Ease.OutQuad));
            closeBtnSequence.Append(closeButton.transform.DOScale(Vector3.one, 0.10f).SetEase(Ease.OutBack));
            closeBtnSequence.OnComplete(Close);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Claim Handler & UI Refresh
        // ─────────────────────────────────────────────────────────────────────

        private void OnClaimButtonPressed()
        {
            var mgr = DailyRewardManager.Instance;
            if (mgr == null || !mgr.CanClaimReward) return;

            ExecuteClaim(multiplier: 1);
        }

        private void OnClaim2xButtonPressed()
        {
            var mgr = DailyRewardManager.Instance;
            if (mgr == null || !mgr.CanClaimReward) return;

            if (AdManager.Instance != null)
            {
                AdManager.Instance.ShowRewardedAd(
                    RewardType.WinDoubleCoins,
                    onRewardGranted: () => ExecuteClaim(multiplier: 2),
                    onFailed:        () => ExecuteClaim(multiplier: 1));
            }
            else
            {
                ExecuteClaim(multiplier: 2);
            }
        }

        private void ExecuteClaim(int multiplier)
        {
            var mgr = DailyRewardManager.Instance;
            if (mgr == null || !mgr.CanClaimReward) return;

            if (claimButton != null)   claimButton.interactable = false;
            if (claim2xButton != null) claim2xButton.interactable = false;

            int dayToClaim = mgr.CurrentDay;
            bool claimed = mgr.ClaimTodayReward(multiplier);
            if (!claimed)
            {
                RefreshUIState();
                return;
            }

            AudioManager.Play(SfxType.Reward);
            HapticManager.Play(HapticType.Medium);

            // Card pop animation
            int cardIndex = dayToClaim - 1;
            if (dayCards != null && cardIndex >= 0 && cardIndex < dayCards.Length && dayCards[cardIndex] != null)
            {
                dayCards[cardIndex].PlayClaimAnimation(() => RefreshUIState());
            }
            else
            {
                RefreshUIState();
            }
        }

        private void Update()
        {
            if (overlayRoot != null && !overlayRoot.activeInHierarchy) return;

            var mgr = DailyRewardManager.Instance;
            if (mgr == null || !mgr.IsClockTampered)
            {
                UpdateTimerDisplay();
            }
        }

        public void RefreshUIState()
        {
            var mgr = DailyRewardManager.Instance;
            int currentDay = mgr != null ? mgr.CurrentDay : 1;
            bool isAvailable = mgr != null && mgr.CanClaimReward;
            bool isTampered = mgr != null && mgr.IsClockTampered;

            // 1. Update 7 Day Cards
            if (dayCards != null)
            {
                for (int i = 0; i < dayCards.Length; i++)
                {
                    if (dayCards[i] == null) continue;
                    int dayNum = i + 1;
                    DailyRewardItem item = mgr != null ? mgr.GetRewardForDay(dayNum) : DailyRewardConfigSO.GetDefaultReward(dayNum);

                    DailyRewardCardState cardState;
                    if (dayNum < currentDay)
                    {
                        cardState = DailyRewardCardState.Claimed;
                    }
                    else if (dayNum == currentDay)
                    {
                        cardState = isAvailable ? DailyRewardCardState.Available : DailyRewardCardState.Claimed;
                    }
                    else
                    {
                        cardState = DailyRewardCardState.Future;
                    }

                    dayCards[i].Setup(dayNum, item, cardState);
                }
            }

            // 2. Update Claim Buttons (Normal & 2X Rewarded)
            if (claimButton != null)
            {
                claimButton.interactable = isAvailable;
                if (claimButtonText != null) claimButtonText.text = isAvailable ? "Claim" : "Claimed";
            }

            if (claim2xButton != null)
            {
                claim2xButton.gameObject.SetActive(isAvailable);
                claim2xButton.interactable = isAvailable;
                if (claim2xButtonText != null) claim2xButtonText.text = "2x";
            }

            // 3. Update Countdown / Status Text (Always Visible)
            if (timerPillGroup != null) timerPillGroup.SetActive(true);
            if (statusTimerText != null) statusTimerText.gameObject.SetActive(true);

            if (isTampered)
            {
                if (statusTimerText != null) statusTimerText.text = "Check device date settings";
            }
            else
            {
                UpdateTimerDisplay();
            }
        }

        private void UpdateTimerDisplay()
        {
            DateTime nextMidnight = DateTime.Today.AddDays(1);
            TimeSpan remaining = nextMidnight - DateTime.Now;
            if (remaining.TotalSeconds <= 0)
            {
                DailyRewardManager.Instance?.RefreshRewardStatus();
                return;
            }

            if (statusTimerText != null)
            {
                if (!statusTimerText.gameObject.activeSelf)
                    statusTimerText.gameObject.SetActive(true);

                statusTimerText.text = $"{remaining.Hours:D2}h {remaining.Minutes:D2}m {remaining.Seconds:D2}s";
                statusTimerText.SetAllDirty();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Auto-Discovery (safely references existing child UI components)
        // ─────────────────────────────────────────────────────────────────────

        private void EnsureUIHierarchy()
        {
            if (overlayRoot == null) overlayRoot = gameObject;

            if (panelRect == null)
            {
                var p = transform.Find("PopupPanel") ?? transform.Find("Panel");
                if (p != null) panelRect = p.GetComponent<RectTransform>();
            }

            if (panelRect != null && panelCanvasGroup == null)
            {
                panelCanvasGroup = panelRect.GetComponent<CanvasGroup>() ?? panelRect.gameObject.AddComponent<CanvasGroup>();
            }

            // Auto-discover cards if unassigned in Inspector
            if (dayCards == null || dayCards.Length < 7 || dayCards[0] == null)
            {
                var cardsInChild = GetComponentsInChildren<DailyRewardDayCard>(true);
                if (cardsInChild != null && cardsInChild.Length >= 7)
                {
                    dayCards = new DailyRewardDayCard[7];
                    for (int i = 0; i < 7; i++) dayCards[i] = cardsInChild[i];
                }
            }

            // Auto-discover timer text & pill group if unassigned
            if (statusTimerText == null)
            {
                var txts = GetComponentsInChildren<TMP_Text>(true);
                foreach (var t in txts)
                {
                    string n = t.gameObject.name.ToLower();
                    if (n.Contains("timer") || n.Contains("countdown") || n.Contains("status"))
                    {
                        statusTimerText = t;
                        break;
                    }
                }
            }

            if (timerPillGroup == null && statusTimerText != null)
            {
                timerPillGroup = statusTimerText.gameObject.transform.parent != null
                    ? statusTimerText.gameObject.transform.parent.gameObject
                    : statusTimerText.gameObject;
            }

            // Auto-discover buttons if unassigned
            if (claimButton == null || claim2xButton == null || closeButton == null)
            {
                var btns = GetComponentsInChildren<Button>(true);
                foreach (var b in btns)
                {
                    string n = b.gameObject.name.ToLower();
                    if (closeButton == null && (n.Contains("close") || n.Contains("x"))) closeButton = b;
                    else if (claim2xButton == null && (n.Contains("2x") || n.Contains("double") || n.Contains("ad"))) claim2xButton = b;
                    else if (claimButton == null && n.Contains("claim")) claimButton = b;
                }
            }
        }
    }
}
