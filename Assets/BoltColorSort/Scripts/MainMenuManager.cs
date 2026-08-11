using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DG.Tweening;

namespace NutBoltSort
{
    /// <summary>
    /// Orchestrates the Main Menu scene.
    ///
    /// Responsibilities:
    ///   - Entry animation (logo, play button, UI buttons).
    ///   - Logo idle breath animation.
    ///   - Play button idle pulse animation.
    ///   - Remove Ads / Shop icon idle animations via MainMenuButtonAnimator.
    ///   - Remove Ads / Shop button press feedback → popup open.
    ///   - Play button reads current saved level from PlayerPrefs.
    ///   - Asynchronous gameplay scene load with color-iris transition.
    ///   - Settings button → MainMenuSettingsPanel.
    ///   - Remove Ads button → RemoveAdsPopup.
    ///   - Shop button → ShopPopup.
    ///   - Android Back button routing.
    ///
    /// Add this script to the MainMenuManager GameObject in the Main Menu scene.
    /// Do NOT call DOTween.KillAll() anywhere in this script.
    /// </summary>
    [AddComponentMenu("BoltShift/Main Menu Manager")]
    [DisallowMultipleComponent]
    public sealed class MainMenuManager : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Background & Logo
        // ─────────────────────────────────────────────────────────────────────

        [Header("Background & Logo")]
        [Tooltip("Full-screen background image. Assigned via Inspector.")]
        [SerializeField] private Image backgroundImage;

        [Tooltip("Game logo RectTransform for idle breath animation.")]
        [SerializeField] private RectTransform gameLogo;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Play Button
        // ─────────────────────────────────────────────────────────────────────

        [Header("Play Button")]
        [SerializeField] private Button        playButton;
        [SerializeField] private RectTransform playButtonRoot;
        [SerializeField] private TMP_Text      playText;
        [SerializeField] private TMP_Text      currentLevelText;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Top Bar Buttons
        // ─────────────────────────────────────────────────────────────────────

        [Header("Top Bar")]
        [SerializeField] private Button settingsButton;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Bottom Buttons
        // ─────────────────────────────────────────────────────────────────────

        [Header("Bottom Content")]
        [SerializeField] private Button removeAdsButton;
        [SerializeField] private Button shopButton;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Coin Section
        // ─────────────────────────────────────────────────────────────────────

        [Header("Coin Section")]
        [Tooltip("TMP_Text that shows the player's current coin balance in the Main Menu.")]
        [SerializeField] private TMP_Text coinDisplayText;

        [Tooltip("The + button next to the coin display. Clicking it opens the Shop popup.")]
        [SerializeField] private Button   coinPlusButton;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Extra Remove Ads Triggers
        // ─────────────────────────────────────────────────────────────────────

        [Header("Banner / Extra Buttons")]
        [Tooltip("Any additional buttons that should also open the Remove Ads popup. " +
                 "e.g. the X close button on an ad banner. Add as many as needed.")]
        [SerializeField] private Button[] extraRemoveAdsButtons;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Button Animators
        // ─────────────────────────────────────────────────────────────────────

        [Header("Button Animators")]
        [Tooltip("MainMenuButtonAnimator on the Remove Ads icon visual child.")]
        [SerializeField] private MainMenuButtonAnimator removeAdsAnimator;

        [Tooltip("MainMenuButtonAnimator on the Shop icon visual child.")]
        [SerializeField] private MainMenuButtonAnimator shopAnimator;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Panels
        // ─────────────────────────────────────────────────────────────────────

        [Header("Settings")]
        [SerializeField] private MainMenuSettingsPanel settingsPanel;

        [Header("Remove Ads")]
        [SerializeField] private RemoveAdsPopup removeAdsPopup;

        [Header("Shop")]
        [SerializeField] private ShopPopup shopPopup;

        [Header("Daily Reward")]
        [SerializeField] private Button dailyRewardButton;
        [SerializeField] private GameObject dailyRewardBadge;
        [SerializeField] private MainMenuButtonAnimator dailyRewardAnimator;
        [SerializeField] private DailyRewardPanel dailyRewardPanel;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Transition
        // ─────────────────────────────────────────────────────────────────────

        [Header("Scene Transition")]
        [SerializeField] private SceneTransitionController sceneTransition;

        [Tooltip("The exact name of the gameplay scene as it appears in Build Settings.")]
        [SerializeField] private string gameplaySceneName = SceneNames.Gameplay;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Animation Settings
        // ─────────────────────────────────────────────────────────────────────

        [Header("Entry Animation")]
        [SerializeField, Range(0.03f, 0.14f)] private float entryStagger       = 0.07f;
        [SerializeField, Range(0.14f, 0.30f)] private float entryItemDuration  = 0.22f;

        [Header("Logo Idle Animation")]
        [SerializeField, Range(1.6f, 2.8f)]  private float logoBreathDuration  = 2.2f;
        [SerializeField, Range(1.005f, 1.04f)] private float logoBreathScale   = 1.025f;

        [Header("Play Button Idle Animation")]
        [SerializeField, Range(0.7f, 1.4f)]  private float playPulseDuration   = 1.0f;
        [SerializeField, Range(1.01f, 1.08f)] private float playPulseScale     = 1.04f;

        // ─────────────────────────────────────────────────────────────────────
        // Constants
        // ─────────────────────────────────────────────────────────────────────

        private const string PrefsLevel = "CurrentLevelNumber";

        // ─────────────────────────────────────────────────────────────────────
        // State
        // ─────────────────────────────────────────────────────────────────────

        private bool     isLoading;
        private Tween    logoPulseTween;
        private Tween    playPulseTween;
        private Sequence entrySequence;

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            // Ensure AudioManager and HapticManager exist (they may persist from
            // gameplay or need to be created fresh if this is the first scene).
            AudioManager.EnsureInstance(gameObject);
            HapticManager.EnsureInstance(gameObject);

            BindButtons();
        }

        private void Start()
        {
            // Hide overlays cleanly regardless of their editor state.
            settingsPanel?.HideImmediate();
            removeAdsPopup?.HideImmediate();
            shopPopup?.HideImmediate();
            dailyRewardPanel?.HideImmediate();

            RefreshCoinDisplay(PlayerWallet.GetCoins());
            RefreshLevelText();
            AdManager.Instance?.ShowBanner();
            PlayEntryAnimation();

            // Keep the coin display in sync whenever the balance changes.
            PlayerWallet.OnCoinsChanged += RefreshCoinDisplay;

            if (DailyRewardManager.Instance != null)
                DailyRewardManager.Instance.OnStatusChanged += RefreshDailyRewardButtonState;

            RefreshDailyRewardButtonState();
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            // Android Back — close open panels first; ignore if loading.
            if (isLoading) return;

            if (settingsPanel != null && settingsPanel.IsOpen)
            {
                settingsPanel.CloseSettings();
                return;
            }

            if (removeAdsPopup != null && removeAdsPopup.IsOpen)
            {
                removeAdsPopup.Close();
                return;
            }

            if (shopPopup != null && shopPopup.IsOpen)
            {
                shopPopup.Close();
                return;
            }

            if (dailyRewardPanel != null && dailyRewardPanel.IsOpen)
            {
                dailyRewardPanel.Close();
                return;
            }

            // Nothing open — Back on Main Menu does nothing (exit confirmation out of scope).
        }

        private void OnDestroy()
        {
            logoPulseTween?.Kill();
            playPulseTween?.Kill();
            entrySequence?.Kill();

            // Always unsubscribe to avoid memory leaks after scene unload.
            PlayerWallet.OnCoinsChanged -= RefreshCoinDisplay;

            if (DailyRewardManager.Instance != null)
                DailyRewardManager.Instance.OnStatusChanged -= RefreshDailyRewardButtonState;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Button Binding
        // ─────────────────────────────────────────────────────────────────────

        private void BindButtons()
        {
            if (playButton != null)
            {
                playButton.onClick.RemoveAllListeners();
                playButton.onClick.AddListener(OnPlayPressed);
            }

            if (settingsButton != null)
            {
                settingsButton.onClick.RemoveAllListeners();
                settingsButton.onClick.AddListener(OnSettingsPressed);
            }

            if (removeAdsButton != null)
            {
                removeAdsButton.onClick.RemoveAllListeners();
                removeAdsButton.onClick.AddListener(OnRemoveAdsPressed);
            }

            if (shopButton != null)
            {
                shopButton.onClick.RemoveAllListeners();
                shopButton.onClick.AddListener(OnShopPressed);
            }

            if (dailyRewardButton != null)
            {
                dailyRewardButton.onClick.RemoveAllListeners();
                dailyRewardButton.onClick.AddListener(OnDailyRewardPressed);
            }

            // Coin section + button — opens the Shop popup.
            if (coinPlusButton != null)
            {
                coinPlusButton.onClick.RemoveAllListeners();
                coinPlusButton.onClick.AddListener(OnShopPressed);
            }

            // Wire every extra button (banner X, secondary buttons, etc.) to open
            // the Remove Ads popup — same handler, no animator press feedback.
            if (extraRemoveAdsButtons != null)
            {
                foreach (Button btn in extraRemoveAdsButtons)
                {
                    if (btn == null) continue;
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(OnExtraRemoveAdsPressed);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Level Text
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the saved level number and updates the Play button's level label.
        /// Safe to call again when returning from gameplay.
        /// </summary>
        public void RefreshLevelText()
        {
            int level = PlayerPrefs.GetInt(PrefsLevel, 1);
            if (level < 1) level = 1;

            if (currentLevelText != null)
                currentLevelText.text = $"LEVEL {level}";
        }

        /// <summary>
        /// Updates the coin display text with the given balance.
        /// Signature matches PlayerWallet.OnCoinsChanged so it can be subscribed directly.
        /// </summary>
        private void RefreshCoinDisplay(int newBalance)
        {
            if (coinDisplayText != null)
                coinDisplayText.text = newBalance.ToString("N0");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Entry Animation
        // ─────────────────────────────────────────────────────────────────────

        private void PlayEntryAnimation()
        {
            // Prepare items — start invisible/scaled-down.
            SetEntryStartState(gameLogo,        startAlpha: 0f, startScale: 0.88f);
            SetEntryStartState(playButtonRoot,  startAlpha: 0f, startScale: 0.88f);
            SetEntryStartState(settingsButton,  startAlpha: 0f);
            SetEntryStartState(removeAdsButton, startAlpha: 0f);
            SetEntryStartState(shopButton,      startAlpha: 0f);

            entrySequence?.Kill();
            entrySequence = DOTween.Sequence();

            float t = 0f;

            AppendEntryItem(entrySequence, gameLogo,        ref t);
            AppendEntryItem(entrySequence, playButtonRoot,  ref t);
            AppendEntryItem(entrySequence, settingsButton,  ref t);
            AppendEntryItem(entrySequence, removeAdsButton, ref t);
            AppendEntryItem(entrySequence, shopButton,      ref t);

            entrySequence.OnComplete(() =>
            {
                // Begin idle animations only after entry finishes.
                StartLogoIdleAnimation();
                StartPlayButtonIdleAnimation();
                StartButtonIdleAnimations();
            });
        }

        private static void SetEntryStartState(Component target, float startAlpha = 0f, float startScale = -1f)
        {
            if (target == null) return;
            var cg = target.GetComponent<CanvasGroup>();
            if (cg == null) cg = target.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = startAlpha;
            if (startScale > 0f)
                target.transform.localScale = Vector3.one * startScale;
        }

        private void AppendEntryItem(Sequence seq, Component target, ref float currentTime)
        {
            if (target == null) { currentTime += entryStagger; return; }

            var cg = target.GetComponent<CanvasGroup>();
            if (cg == null) cg = target.gameObject.AddComponent<CanvasGroup>();

            Sequence itemSeq = DOTween.Sequence();
            itemSeq.Join(cg.DOFade(1f, entryItemDuration).SetEase(Ease.OutCubic));
            itemSeq.Join(target.transform.DOScale(Vector3.one, entryItemDuration).SetEase(Ease.OutBack));

            seq.Insert(currentTime, itemSeq);
            currentTime += entryStagger;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Idle Animations
        // ─────────────────────────────────────────────────────────────────────

        private void StartLogoIdleAnimation()
        {
            if (gameLogo == null) return;
            logoPulseTween?.Kill();
            logoPulseTween = gameLogo
                .DOScale(Vector3.one * logoBreathScale, logoBreathDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        private void StartPlayButtonIdleAnimation()
        {
            if (playButtonRoot == null) return;
            playPulseTween?.Kill();
            playPulseTween = playButtonRoot
                .DOScale(Vector3.one * playPulseScale, playPulseDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        /// <summary>
        /// Starts the attention-animation loops on both icon animators.
        /// Remove Ads and Shop use different wait ranges so they rarely fire
        /// at the same time.
        /// </summary>
        private void StartButtonIdleAnimations()
        {
            removeAdsAnimator?.StartIdleAnimation();
            shopAnimator?.StartIdleAnimation();
            if (DailyRewardManager.Instance != null && DailyRewardManager.Instance.CanClaimReward)
                dailyRewardAnimator?.StartIdleAnimation();
        }

        private void StopIdleAnimations()
        {
            logoPulseTween?.Kill();
            playPulseTween?.Kill();

            removeAdsAnimator?.StopIdleAnimation();
            shopAnimator?.StopIdleAnimation();
            dailyRewardAnimator?.StopIdleAnimation();

            // Snap back to neutral scale so the transition starts cleanly.
            if (gameLogo       != null) gameLogo.localScale       = Vector3.one;
            if (playButtonRoot != null) playButtonRoot.localScale = Vector3.one;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Button Handlers
        // ─────────────────────────────────────────────────────────────────────

        private void OnPlayPressed()
        {
            // Guard: prevent double-tap during loading or while transition is active.
            if (isLoading || SceneTransitionController.TransitionInFlight) return;
            if (settingsPanel  != null && settingsPanel.IsOpen)  return;
            if (removeAdsPopup != null && removeAdsPopup.IsOpen) return;
            if (shopPopup      != null && shopPopup.IsOpen)      return;

            isLoading = true;

            // Disable the play button immediately to prevent any further clicks.
            if (playButton != null) playButton.interactable = false;

            StopIdleAnimations();

            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

            // Start the closing transition, then load asynchronously when covered.
            sceneTransition?.PlayClose(onCovered: () => StartCoroutine(LoadGameplayAsync()));

            // If no transition controller is assigned, load directly.
            if (sceneTransition == null)
                StartCoroutine(LoadGameplayAsync());
        }

        private IEnumerator LoadGameplayAsync()
        {
            AsyncOperation op = SceneManager.LoadSceneAsync(gameplaySceneName);

            if (op == null)
            {
                Debug.LogError($"[MainMenuManager] Scene '{gameplaySceneName}' not found. " +
                               "Check Build Settings and SceneNames.cs.");
                isLoading = false;
                if (playButton != null) playButton.interactable = true;
                sceneTransition?.PlayOpen(null);
                yield break;
            }

            // Prevent the scene from activating until we're ready.
            op.allowSceneActivation = false;

            // Wait until the scene is fully loaded in the background.
            while (op.progress < 0.9f)
                yield return null;

            // Scene is ready — activate it.
            op.allowSceneActivation = true;

            // Wait one frame for the scene to fully initialize.
            yield return null;

            // Open the transition to reveal the loaded gameplay scene.
            sceneTransition?.PlayOpen(null);

            // isLoading remains true; the Main Menu will be unloaded by the
            // new scene's SceneManager automatically.
        }

        private void OnSettingsPressed()
        {
            if (isLoading) return;
            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);
            settingsPanel?.OpenSettings();
        }

        private void OnRemoveAdsPressed()
        {
            if (isLoading) return;
            if (removeAdsPopup != null && removeAdsPopup.IsOpen) return;

            if (removeAdsPopup == null)
            {
                Debug.LogError("[MainMenuManager] 'removeAdsPopup' is not assigned in the Inspector! " +
                               "Drag the RemoveAdsPopup component into the Remove Ads field.", this);
                return;
            }

            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

            // Press feedback first — popup opens in the callback.
            if (removeAdsAnimator != null)
                removeAdsAnimator.PlayPressAnimation(() => removeAdsPopup?.Open());
            else
                removeAdsPopup?.Open();
        }

        /// <summary>
        /// Called by banner close buttons and any other secondary buttons
        /// that should open the Remove Ads popup.
        /// Opens the popup directly — no icon press animation.
        /// </summary>
        private void OnExtraRemoveAdsPressed()
        {
            if (isLoading) return;
            if (removeAdsPopup != null && removeAdsPopup.IsOpen) return;

            if (removeAdsPopup == null)
            {
                Debug.LogError("[MainMenuManager] 'removeAdsPopup' is not assigned — " +
                               "extra Remove Ads button has no popup to open.", this);
                return;
            }

            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);
            removeAdsPopup.Open();
        }

        private void OnShopPressed()
        {
            if (isLoading) return;
            if (shopPopup != null && shopPopup.IsOpen) return;

            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

            // Press feedback first — popup opens in the callback.
            if (shopAnimator != null)
                shopAnimator.PlayPressAnimation(() => shopPopup?.Open());
            else
                shopPopup?.Open();
        }

        private void OnDailyRewardPressed()
        {
            if (isLoading) return;
            if (dailyRewardPanel != null && dailyRewardPanel.IsOpen) return;

            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

            if (dailyRewardAnimator != null)
                dailyRewardAnimator.PlayPressAnimation(() => dailyRewardPanel?.Open());
            else
                dailyRewardPanel?.Open();
        }

        public void RefreshDailyRewardButtonState()
        {
            bool isAvailable = DailyRewardManager.Instance != null && DailyRewardManager.Instance.CanClaimReward;

            if (dailyRewardBadge != null)
                dailyRewardBadge.SetActive(isAvailable);

            if (isAvailable)
                dailyRewardAnimator?.StartIdleAnimation();
            else
                dailyRewardAnimator?.StopIdleAnimation();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Public — called when returning from gameplay (future use)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Call this when returning to the Main Menu from gameplay.
        /// Refreshes the level display and replays the entry animation.
        /// </summary>
        public void OnReturnFromGameplay()
        {
            isLoading = false;
            if (playButton != null) playButton.interactable = true;
            RefreshLevelText();
            PlayEntryAnimation();
        }
    }
}
