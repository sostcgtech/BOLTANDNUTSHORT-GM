using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace NutBoltSort
{
    /// <summary>
    /// Pure UI driver for the 00_Boot splash screen.
    ///
    /// Does NOT contain any loading logic — it only reacts to values pushed in
    /// by GameBootstrap.
    ///
    /// Responsibilities:
    ///   - Receives actual progress (0–1) from GameBootstrap via SetActualProgress().
    ///   - Smoothly interpolates DisplayedProgress toward ActualProgress.
    ///   - Updates Slider.value (0–1) every frame.
    ///   - Updates PercentText only when the displayed integer value changes.
    ///   - Updates StatusText when SetStatus() is called.
    ///   - Plays a subtle logo idle pulse animation.
    ///   - FadeOutAndActivate(): fades the entire canvas out, then activates the
    ///     pre-loaded MainMenu scene.
    ///
    /// Add this component to the LoadingCanvas GameObject in the 00_Boot scene.
    /// </summary>
    [AddComponentMenu("BoltShift/Boot/Splash Screen Controller")]
    [DisallowMultipleComponent]
    public sealed class SplashScreenController : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        [Header("UI References")]
        [Tooltip("The CanvasGroup wrapping the entire loading screen. Used for the final fade-out.")]
        [SerializeField] private CanvasGroup loadingCanvasGroup;

        [Tooltip("The Slider component used as the loading bar. Set Min=0, Max=1, Interactable=OFF, " +
                 "and disable the Handle Slide Area child.")]
        [SerializeField] private Slider loadingSlider;

        [Tooltip("TextMeshPro label showing integer percentage, e.g. '63%'.")]
        [SerializeField] private TMP_Text percentText;

        [Tooltip("Optional label showing the current task status message.")]
        [SerializeField] private TMP_Text statusText;

        [Tooltip("Game logo RectTransform — driven by the idle pulse animation.")]
        [SerializeField] private RectTransform gameLogo;

        [Header("Animation")]
        [Tooltip("How fast the displayed progress bar catches up to actual progress (units/second).")]
        [SerializeField, Range(0.2f, 3f)] private float progressSmoothSpeed = 1.2f;

        [Tooltip("Maximum amount the displayed progress may be ahead of actual progress. " +
                 "Keeps the bar from lying to the player.")]
        [SerializeField, Range(0.01f, 0.1f)] private float maxProgressLead = 0.04f;

        [Tooltip("Duration of the logo idle breath cycle (half-cycle).")]
        [SerializeField, Range(1.4f, 3f)] private float logoPulseDuration = 2.0f;

        [Tooltip("Scale at the peak of the logo breath animation.")]
        [SerializeField, Range(1.005f, 1.04f)] private float logoPulseScale = 1.02f;

        [Header("Transition")]
        [Tooltip("Short pause (seconds) at 100% before the Main Menu is activated.")]
        [SerializeField, Range(0.05f, 0.4f)] private float completeHoldDuration = 0.15f;

        // ─────────────────────────────────────────────────────────────────────
        // State
        // ─────────────────────────────────────────────────────────────────────

        private float _actualProgress;
        private float _displayedProgress;
        private int   _lastDisplayedPercent = -1;   // -1 forces first update
        private Tween _logoPulseTween;

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Start()
        {
            // Initialise UI to zero.
            SetBarFillImmediate(0f);
            UpdatePercentText(0);
            if (statusText != null) statusText.text = string.Empty;

            // Ensure the canvas is fully visible.
            if (loadingCanvasGroup != null)
            {
                loadingCanvasGroup.alpha          = 1f;
                loadingCanvasGroup.interactable   = false;
                loadingCanvasGroup.blocksRaycasts = false;
            }

            StartLogoAnimation();
        }

        private void Update()
        {
            // Smooth display progress toward actual progress.
            float target = Mathf.Clamp(_actualProgress - maxProgressLead, 0f, _actualProgress);
            _displayedProgress = Mathf.MoveTowards(_displayedProgress, target,
                                                   progressSmoothSpeed * Time.unscaledDeltaTime);
            _displayedProgress = Mathf.Clamp01(_displayedProgress);

            SetBarFillImmediate(_displayedProgress);

            int pct = Mathf.RoundToInt(_displayedProgress * 100f);
            if (pct != _lastDisplayedPercent)
            {
                _lastDisplayedPercent = pct;
                UpdatePercentText(pct);
            }
        }

        private void OnDestroy()
        {
            _logoPulseTween?.Kill();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Public API — called by GameBootstrap
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Sets the actual loading progress (0–1).
        /// The displayed bar smoothly chases this value.
        /// </summary>
        public void SetActualProgress(float progress)
        {
            _actualProgress = Mathf.Clamp01(progress);
        }

        /// <summary>Sets the human-readable status text shown below the bar.</summary>
        public void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }

        /// <summary>
        /// Fills the bar to 100%, holds briefly, then immediately activates the
        /// pre-loaded Main Menu scene. The loading canvas stays fully visible until
        /// Unity replaces it, so the empty Boot scene is never exposed.
        /// </summary>
        public IEnumerator FadeOutAndActivate(UnityEngine.AsyncOperation menuOperation)
        {
            // Step 1 — snap actual to 100% and let the smooth bar catch up.
            _actualProgress = 1f;

            // Wait until displayed reaches ≥99%.
            float waitStart = Time.realtimeSinceStartup;
            const float maxWait = 1.5f;
            while (_displayedProgress < 0.99f && Time.realtimeSinceStartup - waitStart < maxWait)
                yield return null;

            // Force full — bar shows exactly 100%.
            _displayedProgress = 1f;
            SetBarFillImmediate(1f);
            UpdatePercentText(100);

            // Step 2 — brief hold so the player sees 100% for a moment.
            yield return new WaitForSecondsRealtime(completeHoldDuration);

            // Step 3 — activate the scene immediately (no fade).
            // The loading canvas is destroyed by Unity the moment the new scene loads,
            // so there is no blank frame between the splash and the Main Menu.
            if (menuOperation != null)
                menuOperation.allowSceneActivation = true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────────────────

        private void SetBarFillImmediate(float t)
        {
            if (loadingSlider != null) loadingSlider.value = t;
        }

        private void UpdatePercentText(int percent)
        {
            if (percentText != null) percentText.text = $"{percent}%";
        }

        private void StartLogoAnimation()
        {
            if (gameLogo == null) return;
            _logoPulseTween?.Kill();
            _logoPulseTween = gameLogo
                .DOScale(Vector3.one * logoPulseScale, logoPulseDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }
    }
}
