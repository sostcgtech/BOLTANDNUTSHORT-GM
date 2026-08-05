using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace NutBoltSort
{
    /// <summary>
    /// Controls the gameplay Settings panel.
    ///
    /// Responsibilities:
    ///   - Open / close animations (DOTween scale + fade).
    ///   - Sound toggle  → AudioManager.EnableSfx()
    ///   - Vibration toggle → HapticManager.EnableHaptics()
    ///   - Contact Us → device email application (clipboard fallback).
    ///   - Home button → safe placeholder for future Main Menu navigation.
    ///   - Version text from Application.version.
    ///   - Android Back button support.
    ///   - Win-popup deferral while panel is open.
    ///
    /// Wire the Settings button via UIManager.OnSettingsButtonPressed().
    /// Do NOT call DOTween.KillAll() anywhere in this script.
    /// </summary>
    [AddComponentMenu("BoltShift/UI/Settings Panel Controller")]
    [DisallowMultipleComponent]
    public sealed class SettingsPanelController : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Overlay
        // ─────────────────────────────────────────────────────────────────────

        [Header("Overlay")]
        [Tooltip("Root GameObject that contains both the InputBlocker and the SettingsPanel. Set inactive when closed.")]
        [SerializeField] private GameObject settingsOverlay;

        [Tooltip("Full-screen image that blocks gameplay taps behind the panel.")]
        [SerializeField] private Image inputBlocker;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Panel Animation
        // ─────────────────────────────────────────────────────────────────────

        [Header("Panel Animation")]
        [Tooltip("The panel RectTransform that scales in/out.")]
        [SerializeField] private RectTransform settingsPanel;

        [Tooltip("CanvasGroup on the panel root for alpha fading.")]
        [SerializeField] private CanvasGroup panelCanvasGroup;

        [Header("Animation Timing")]
        [SerializeField, Range(0.18f, 0.32f)] private float openDuration  = 0.26f;
        [SerializeField, Range(0.14f, 0.24f)] private float closeDuration = 0.18f;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Buttons
        // ─────────────────────────────────────────────────────────────────────

        [Header("Buttons")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Button contactUsButton;
        [SerializeField] private Button homeButton;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Sound Toggle
        // ─────────────────────────────────────────────────────────────────────

        [Header("Sound Toggle")]
        [SerializeField] private SettingsToggle soundToggle;
        [Tooltip("Optional extra icon Image next to the toggle label.")]
        [SerializeField] private Image soundIcon;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Vibration Toggle
        // ─────────────────────────────────────────────────────────────────────

        [Header("Vibration Toggle")]
        [SerializeField] private SettingsToggle vibrationToggle;
        [Tooltip("Optional extra icon Image next to the toggle label.")]
        [SerializeField] private Image vibrationIcon;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Version
        // ─────────────────────────────────────────────────────────────────────

        [Header("Version")]
        [SerializeField] private TMP_Text versionText;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Support
        // ─────────────────────────────────────────────────────────────────────

        [Header("Support")]
        [Tooltip("Support email address. Never hardcode; set this in the Inspector.")]
        [SerializeField] private string supportEmail = "support@yourgame.com";

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Toast
        // ─────────────────────────────────────────────────────────────────────

        [Header("Toast Message")]
        [Tooltip("A TMP_Text label used to show brief feedback messages (e.g. 'Support email copied').")]
        [SerializeField] private TMP_Text toastLabel;
        [SerializeField, Range(0.8f, 3.0f)] private float toastDuration = 1.8f;

        // ─────────────────────────────────────────────────────────────────────
        // PlayerPrefs Keys  (match AudioManager / HapticManager internal keys)
        // ─────────────────────────────────────────────────────────────────────

        private const string SfxKey     = "BoltShift.SfxEnabled";
        private const string HapticsKey = "BoltShift.HapticsEnabled";

        // ─────────────────────────────────────────────────────────────────────
        // State
        // ─────────────────────────────────────────────────────────────────────

        private Sequence   openSequence;
        private Sequence   closeSequence;
        private Coroutine  toastCoroutine;
        private bool       pendingWinPopup;
        private bool       initialized;

        /// <summary>True while the settings panel is visible and interactive.</summary>
        public bool IsOpen { get; private set; }

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            // Bind buttons now if this object started active in the hierarchy.
            // If SettingsOverlay was disabled in the editor, Awake still runs
            // as soon as the overlay is first enabled — so binding always
            // happens before the first interaction.
            EnsureInitialized();
        }

        /// <summary>
        /// Hides the overlay instantly without animation.
        /// Called by UIManager.Start() so the overlay is hidden at game start
        /// regardless of whether it was enabled or disabled in the editor.
        /// </summary>
        public void HideImmediate()
        {
            EnsureInitialized();
            IsOpen = false;
            if (settingsOverlay != null)
                settingsOverlay.SetActive(false);
            if (toastLabel != null)
                toastLabel.gameObject.SetActive(false);
        }

        /// <summary>Idempotent: runs button binding exactly once.</summary>
        private void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            BindButtons();
        }

        private void Update()
        {
            // Android Back button closes the panel.
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
                CloseSettings();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Button Binding
        // ─────────────────────────────────────────────────────────────────────

        private void BindButtons()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(OnClosePressed);
            }

            if (contactUsButton != null)
            {
                contactUsButton.onClick.RemoveAllListeners();
                contactUsButton.onClick.AddListener(OnContactUsPressed);
            }

            if (homeButton != null)
            {
                homeButton.onClick.RemoveAllListeners();
                homeButton.onClick.AddListener(OnHomePressed);
            }

            // Toggle buttons are driven by SettingsToggle.Toggle() via Inspector onClick.
            // We subscribe here so we can react to value changes at runtime.
            if (soundToggle != null)
                soundToggle.OnValueChanged += OnSoundToggleChanged;

            if (vibrationToggle != null)
                vibrationToggle.OnValueChanged += OnVibrationToggleChanged;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Open / Close
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Opens the Settings panel. Called by UIManager.OnSettingsButtonPressed().</summary>
        public void OpenSettings()
        {
            if (IsOpen) return;
            IsOpen = true;

            // Initialise state before showing — no sounds/haptics during init.
            InitialiseToggles();
            UpdateVersionText();

            // Prepare the overlay for animation.
            if (settingsOverlay != null)
                settingsOverlay.SetActive(true);

            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha          = 0f;
                panelCanvasGroup.interactable   = false;
                panelCanvasGroup.blocksRaycasts = false;
            }

            if (settingsPanel != null)
                settingsPanel.localScale = Vector3.one * 0.85f;

            // Kill any previous sequence.
            openSequence?.Kill();
            closeSequence?.Kill();

            AudioManager.Play(SfxType.PopupOpen);

            openSequence = DOTween.Sequence();

            if (panelCanvasGroup != null)
                openSequence.Join(panelCanvasGroup.DOFade(1f, openDuration));

            if (settingsPanel != null)
            {
                // 0.85 → 1.05 → 1.0  using OutBack for the overshoot feel.
                openSequence.Join(
                    settingsPanel.DOScale(Vector3.one * 1.05f, openDuration * 0.75f)
                                 .SetEase(Ease.OutBack)
                                 .OnComplete(() =>
                                     settingsPanel.DOScale(Vector3.one, openDuration * 0.25f)
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

        /// <summary>Closes the Settings panel. Called by the Close button and Android Back.</summary>
        public void CloseSettings()
        {
            if (!IsOpen) return;
            IsOpen = false;

            AudioManager.Play(SfxType.ButtonClick);

            // Disable interaction immediately so the player can't tap while animating out.
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

            if (settingsPanel != null)
                closeSequence.Join(
                    settingsPanel.DOScale(Vector3.one * 0.85f, closeDuration)
                                 .SetEase(Ease.InCubic));

            closeSequence.OnComplete(OnSettingsClosed);
        }

        private void OnSettingsClosed()
        {
            if (settingsOverlay != null)
                settingsOverlay.SetActive(false);

            // Flush deferred Win popup if the level finished while settings was open.
            if (pendingWinPopup)
            {
                pendingWinPopup = false;
                var uiManager = FindAnyObjectByType<UIManager>();
                uiManager?.ShowWinPopup();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Initialisation helpers
        // ─────────────────────────────────────────────────────────────────────

        private void InitialiseToggles()
        {
            bool sfxOn     = PlayerPrefs.GetInt(SfxKey,     1) == 1;
            bool hapticsOn = PlayerPrefs.GetInt(HapticsKey, 1) == 1;

            // SetWithoutNotify avoids triggering sounds/haptics during init.
            if (soundToggle     != null) soundToggle.SetWithoutNotify(sfxOn);
            if (vibrationToggle != null) vibrationToggle.SetWithoutNotify(hapticsOn);
        }

        private void UpdateVersionText()
        {
            if (versionText != null)
                versionText.text = $"Version {Application.version}";
        }

        // ─────────────────────────────────────────────────────────────────────
        // Toggle Callbacks
        // ─────────────────────────────────────────────────────────────────────

        private void OnSoundToggleChanged(bool isOn)
        {
            // Update AudioManager immediately.
            AudioManager.EnableSfx(isOn);

            // Play a test click only when turning sound ON.
            if (isOn)
                AudioManager.Play(SfxType.ButtonClick);
        }

        private void OnVibrationToggleChanged(bool isOn)
        {
            // Update HapticManager immediately.
            HapticManager.EnableHaptics(isOn);

            // Play one light test vibration only when turning vibration ON.
            if (isOn)
                HapticManager.Play(HapticType.Light);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Button Handlers
        // ─────────────────────────────────────────────────────────────────────

        private void OnClosePressed()
        {
            CloseSettings();
        }

        private void OnContactUsPressed()
        {
            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

            if (string.IsNullOrWhiteSpace(supportEmail))
            {
                ShowToast("No support email configured.");
                return;
            }

            string subject = "BoltShift Feedback";
            string body =
                "Hello,\n\n" +
                "I would like to contact the BoltShift team.\n\n" +
                $"Game Version: {Application.version}\n" +
                $"Device: {SystemInfo.deviceModel}\n" +
                $"Android Version: {SystemInfo.operatingSystem}\n\n" +
                "Message:\n";

            string uri =
                "mailto:" + supportEmail +
                "?subject=" + Uri.EscapeDataString(subject) +
                "&body="    + Uri.EscapeDataString(body);

            try
            {
                Application.OpenURL(uri);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SettingsPanelController] Could not open email client: {ex.Message}");
                FallbackCopyEmail();
            }
        }

        private void FallbackCopyEmail()
        {
            try
            {
                GUIUtility.systemCopyBuffer = supportEmail;
                ShowToast("Support email copied");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SettingsPanelController] Clipboard copy failed: {ex.Message}");
                ShowToast($"Contact: {supportEmail}");
            }
        }

        /// <summary>
        /// Placeholder for future Main Menu navigation.
        /// Structure this so wiring in a real scene load later requires only
        /// replacing the single TODO comment below.
        /// </summary>
        public void OnHomePressed()
        {
            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

            // TODO: Replace with SceneManager.LoadScene("MainMenu") when ready.
            Debug.Log("[SettingsPanelController] Home pressed — main menu not yet implemented.");
            ShowToast("Home screen will be added later.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Win Deferral
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called by UIManager when a win occurs while the panel is open.
        /// The popup will be shown automatically once the panel closes.
        /// </summary>
        public void SetPendingWin(bool pending)
        {
            pendingWinPopup = pending;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Toast
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Shows a brief feedback message inside the panel.</summary>
        private void ShowToast(string message)
        {
            if (toastLabel == null)
            {
                Debug.Log($"[Settings] {message}");
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

            // Fade in.
            CanvasGroup cg = toastLabel.GetComponent<CanvasGroup>();
            if (cg == null) cg = toastLabel.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.DOFade(1f, 0.15f);

            yield return new WaitForSeconds(toastDuration);

            // Fade out.
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

            if (soundToggle     != null) soundToggle.OnValueChanged     -= OnSoundToggleChanged;
            if (vibrationToggle != null) vibrationToggle.OnValueChanged -= OnVibrationToggleChanged;
        }
    }
}
