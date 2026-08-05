using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace NutBoltSort
{
    /// <summary>
    /// Controls the Main Menu Settings panel.
    ///
    /// Differences from SettingsPanelController (gameplay):
    ///   - No Home button.
    ///   - Has a Privacy Policy button that opens a URL in the browser.
    ///   - No Win-popup deferral logic.
    ///   - Android Back handled externally by MainMenuManager.
    ///
    /// Reuses SettingsToggle for Sound and Vibration.
    /// Reuses the same PlayerPrefs keys as AudioManager / HapticManager.
    ///
    /// Do NOT call DOTween.KillAll() anywhere in this script.
    /// </summary>
    [AddComponentMenu("BoltShift/UI/Main Menu Settings Panel")]
    [DisallowMultipleComponent]
    public sealed class MainMenuSettingsPanel : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Overlay
        // ─────────────────────────────────────────────────────────────────────

        [Header("Overlay")]
        [SerializeField] private GameObject settingsOverlay;
        [SerializeField] private Image      inputBlocker;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Panel Animation
        // ─────────────────────────────────────────────────────────────────────

        [Header("Panel Animation")]
        [SerializeField] private RectTransform settingsPanel;
        [SerializeField] private CanvasGroup   panelCanvasGroup;

        [Header("Animation Timing")]
        [SerializeField, Range(0.18f, 0.32f)] private float openDuration  = 0.26f;
        [SerializeField, Range(0.14f, 0.24f)] private float closeDuration = 0.18f;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Buttons
        // ─────────────────────────────────────────────────────────────────────

        [Header("Buttons")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Button contactUsButton;
        [SerializeField] private Button privacyPolicyButton;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Toggles
        // ─────────────────────────────────────────────────────────────────────

        [Header("Sound Toggle")]
        [SerializeField] private SettingsToggle soundToggle;

        [Header("Vibration Toggle")]
        [SerializeField] private SettingsToggle vibrationToggle;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Version
        // ─────────────────────────────────────────────────────────────────────

        [Header("Version")]
        [SerializeField] private TMP_Text versionText;

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Support / Privacy
        // ─────────────────────────────────────────────────────────────────────

        [Header("Support & Privacy")]
        [Tooltip("Support email address. Set once in the Inspector.")]
        [SerializeField] private string supportEmail = "support@yourgame.com";

        [Tooltip("Privacy policy URL. Set once in the Inspector.")]
        [SerializeField] private string privacyPolicyUrl = "https://yourgame.com/privacy";

        // ─────────────────────────────────────────────────────────────────────
        // Inspector — Toast
        // ─────────────────────────────────────────────────────────────────────

        [Header("Toast Message")]
        [SerializeField] private TMP_Text toastLabel;
        [SerializeField, Range(0.8f, 3.0f)] private float toastDuration = 1.8f;

        // ─────────────────────────────────────────────────────────────────────
        // PlayerPrefs Keys  (must match AudioManager / HapticManager)
        // ─────────────────────────────────────────────────────────────────────

        private const string SfxKey     = "BoltShift.SfxEnabled";
        private const string HapticsKey = "BoltShift.HapticsEnabled";

        // ─────────────────────────────────────────────────────────────────────
        // State
        // ─────────────────────────────────────────────────────────────────────

        private Sequence  openSequence;
        private Sequence  closeSequence;
        private Coroutine toastCoroutine;
        private bool      initialized;

        public bool IsOpen { get; private set; }

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            EnsureInitialized();
        }

        /// <summary>
        /// Hides the overlay instantly. Called by MainMenuManager.Start()
        /// so the panel starts hidden regardless of its editor state.
        /// </summary>
        public void HideImmediate()
        {
            EnsureInitialized();
            IsOpen = false;
            if (settingsOverlay != null) settingsOverlay.SetActive(false);
            if (toastLabel      != null) toastLabel.gameObject.SetActive(false);
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
                closeButton.onClick.AddListener(CloseSettings);
            }

            if (contactUsButton != null)
            {
                contactUsButton.onClick.RemoveAllListeners();
                contactUsButton.onClick.AddListener(OnContactUsPressed);
            }

            if (privacyPolicyButton != null)
            {
                privacyPolicyButton.onClick.RemoveAllListeners();
                privacyPolicyButton.onClick.AddListener(OnPrivacyPolicyPressed);
            }

            if (soundToggle != null)
                soundToggle.OnValueChanged += OnSoundToggleChanged;

            if (vibrationToggle != null)
                vibrationToggle.OnValueChanged += OnVibrationToggleChanged;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Open / Close
        // ─────────────────────────────────────────────────────────────────────

        public void OpenSettings()
        {
            if (IsOpen) return;
            IsOpen = true;

            InitialiseToggles();
            UpdateVersionText();

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

            openSequence?.Kill();
            closeSequence?.Kill();

            AudioManager.Play(SfxType.PopupOpen);

            openSequence = DOTween.Sequence();

            if (panelCanvasGroup != null)
                openSequence.Join(panelCanvasGroup.DOFade(1f, openDuration));

            if (settingsPanel != null)
            {
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

        public void CloseSettings()
        {
            if (!IsOpen) return;
            IsOpen = false;

            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

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

            closeSequence.OnComplete(() =>
            {
                if (settingsOverlay != null) settingsOverlay.SetActive(false);
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Initialisation Helpers
        // ─────────────────────────────────────────────────────────────────────

        private void InitialiseToggles()
        {
            bool sfxOn     = PlayerPrefs.GetInt(SfxKey,     1) == 1;
            bool hapticsOn = PlayerPrefs.GetInt(HapticsKey, 1) == 1;

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
            AudioManager.EnableSfx(isOn);
            if (isOn) AudioManager.Play(SfxType.ButtonClick);
        }

        private void OnVibrationToggleChanged(bool isOn)
        {
            HapticManager.EnableHaptics(isOn);
            if (isOn) HapticManager.Play(HapticType.Light);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Button Handlers
        // ─────────────────────────────────────────────────────────────────────

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
                Debug.LogWarning($"[MainMenuSettings] Email open failed: {ex.Message}");
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
                Debug.LogWarning($"[MainMenuSettings] Clipboard copy failed: {ex.Message}");
                ShowToast($"Contact: {supportEmail}");
            }
        }

        private void OnPrivacyPolicyPressed()
        {
            AudioManager.Play(SfxType.ButtonClick);
            HapticManager.Play(HapticType.Light);

            if (string.IsNullOrWhiteSpace(privacyPolicyUrl))
            {
                Debug.LogWarning("[MainMenuSettings] Privacy Policy URL is not configured.");
                ShowToast("Privacy policy URL not set.");
                return;
            }

            try
            {
                Application.OpenURL(privacyPolicyUrl);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MainMenuSettings] Could not open privacy policy URL: {ex.Message}");
                ShowToast("Could not open privacy policy.");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Toast
        // ─────────────────────────────────────────────────────────────────────

        private void ShowToast(string message)
        {
            if (toastLabel == null)
            {
                Debug.Log($"[MainMenuSettings] {message}");
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

            if (soundToggle     != null) soundToggle.OnValueChanged     -= OnSoundToggleChanged;
            if (vibrationToggle != null) vibrationToggle.OnValueChanged -= OnVibrationToggleChanged;
        }
    }
}
