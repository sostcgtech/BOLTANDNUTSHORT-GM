using System;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace NutBoltSort
{
    /// <summary>
    /// Custom mobile-game toggle.
    /// Replaces Unity's default checkmark-style toggle with a sliding handle design.
    ///
    /// ON  → bright green background, handle on the right, icon fully visible.
    /// OFF → dark slate background,   handle on the left,  icon slightly dimmed.
    ///
    /// Animate the handle with DOTween (0.15 s, OutCubic).
    /// Use SetWithoutNotify() during panel initialisation to avoid firing sounds/haptics.
    /// </summary>
    [AddComponentMenu("BoltShift/UI/Settings Toggle")]
    [DisallowMultipleComponent]
    public sealed class SettingsToggle : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        [Header("References")]
        [Tooltip("The pill-shaped background Image whose colour changes.")]
        [SerializeField] private Image toggleBackground;

        [Tooltip("The circular handle RectTransform that slides left/right.")]
        [SerializeField] private RectTransform handle;

        [Tooltip("Optional CanvasGroup on the icon inside the toggle. Alpha dims when OFF.")]
        [SerializeField] private CanvasGroup iconGroup;

        [Header("Colours")]
        [SerializeField] private Color onColour  = new Color(0.29f, 0.87f, 0.50f, 1f);  // #4ADE80
        [SerializeField] private Color offColour = new Color(0.20f, 0.25f, 0.34f, 1f);  // #334155

        [Header("Handle Positions (local X)")]
        [Tooltip("Local X position of the handle when the toggle is ON (right side).")]
        [SerializeField] private float handleOnX  = 28f;

        [Tooltip("Local X position of the handle when the toggle is OFF (left side).")]
        [SerializeField] private float handleOffX = -28f;

        [Header("Animation")]
        [SerializeField, Range(0.10f, 0.22f)] private float slideDuration = 0.15f;
        [SerializeField] private Ease slideEase = Ease.OutCubic;

        [Header("Icon Dimming")]
        [SerializeField, Range(0f, 1f)] private float iconOnAlpha  = 1.00f;
        [SerializeField, Range(0f, 1f)] private float iconOffAlpha = 0.45f;

        // ─────────────────────────────────────────────────────────────────────
        // State & Events
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Fired after any state change triggered by SetValue (not SetWithoutNotify).</summary>
        public event Action<bool> OnValueChanged;

        private bool isOn;
        private Tweener slideTween;
        private Tweener bgTween;
        private Tweener iconTween;

        /// <summary>Current state of the toggle.</summary>
        public bool IsOn => isOn;

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            // Ensure the background Image and handle are valid before the first render.
            EnsureReferences();
        }

        private void EnsureReferences()
        {
            if (toggleBackground == null)
                toggleBackground = GetComponentInChildren<Image>();

            if (handle == null)
            {
                // Fall back: assume the first child RectTransform is the handle.
                if (transform.childCount > 0)
                    handle = transform.GetChild(0).GetComponent<RectTransform>();
            }

            if (iconGroup == null)
                iconGroup = GetComponentInChildren<CanvasGroup>();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Changes the toggle state and fires OnValueChanged.
        /// Plays the slide animation.
        /// </summary>
        public void SetValue(bool value)
        {
            isOn = value;
            AnimateToState(isOn, animate: true);
            OnValueChanged?.Invoke(isOn);
        }

        /// <summary>
        /// Sets the visual state without firing OnValueChanged.
        /// Used during panel initialisation to avoid triggering audio or haptics.
        /// </summary>
        public void SetWithoutNotify(bool value)
        {
            isOn = value;
            AnimateToState(isOn, animate: false);
        }

        /// <summary>
        /// Convenience: flips the current state and fires OnValueChanged.
        /// Intended for use from a Button's onClick event in the Inspector.
        /// </summary>
        public void Toggle()
        {
            SetValue(!isOn);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Animation
        // ─────────────────────────────────────────────────────────────────────

        private void AnimateToState(bool state, bool animate)
        {
            float targetX    = state ? handleOnX  : handleOffX;
            Color targetBg   = state ? onColour   : offColour;
            float targetIcon = state ? iconOnAlpha : iconOffAlpha;

            // Kill any running tweens on the same targets.
            slideTween?.Kill();
            bgTween?.Kill();
            iconTween?.Kill();

            if (animate)
            {
                if (handle != null)
                    slideTween = handle.DOLocalMoveX(targetX, slideDuration)
                                       .SetEase(slideEase)
                                       .SetUpdate(true);   // UpdateMode.Normal is fine; no TimeScale freeze.

                if (toggleBackground != null)
                    bgTween = toggleBackground.DOColor(targetBg, slideDuration)
                                              .SetUpdate(true);

                if (iconGroup != null)
                    iconTween = iconGroup.DOFade(targetIcon, slideDuration)
                                         .SetUpdate(true);
            }
            else
            {
                // Instant snap — no tween.
                if (handle != null)
                {
                    Vector3 p = handle.localPosition;
                    p.x = targetX;
                    handle.localPosition = p;
                }

                if (toggleBackground != null)
                    toggleBackground.color = targetBg;

                if (iconGroup != null)
                    iconGroup.alpha = targetIcon;
            }
        }

        private void OnDestroy()
        {
            slideTween?.Kill();
            bgTween?.Kill();
            iconTween?.Kill();
        }
    }
}
