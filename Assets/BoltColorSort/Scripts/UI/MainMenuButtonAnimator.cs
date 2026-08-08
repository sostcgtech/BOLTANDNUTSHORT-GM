using System;
using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace NutBoltSort
{
    /// <summary>
    /// Drives idle attention animations and press-feedback animations for
    /// the Remove Ads and Shop icon buttons in the Main Menu.
    ///
    /// Attach this to the BUTTON root (or any stable parent).
    /// Drag the icon Image RectTransform into the Icon Transform field.
    /// Only the icon is animated — the button layout is never touched.
    ///
    /// Idle animations run on a coroutine timer — no Update() loop.
    /// Tweens are killed safely on disable/destroy.
    ///
    /// Usage:
    ///   1. Attach this component to the Button root GameObject.
    ///   2. Drag the icon Image/RectTransform child into Icon Transform.
    ///   3. Set Animation Type in the Inspector.
    ///   4. Call StartIdleAnimation() after the entry animation finishes.
    ///   5. Call PlayPressAnimation(Action) when the button is tapped.
    ///   6. Call StopIdleAnimation() when entering a loading state.
    ///
    /// Do NOT call DOTween.KillAll() anywhere in this script.
    /// </summary>
    [AddComponentMenu("BoltShift/UI/Main Menu Button Animator")]
    [DisallowMultipleComponent]
    public sealed class MainMenuButtonAnimator : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        public enum AnimationType { RemoveAds, Shop }

        [Header("Target Icon")]
        [Tooltip("Drag the icon Image RectTransform here. " +
                 "Only this object is animated — the button layout is unaffected.")]
        [SerializeField] private RectTransform iconTransform;

        [Header("Configuration")]
        [Tooltip("Selects which idle animation sequence to use.")]
        [SerializeField] private AnimationType animationType = AnimationType.RemoveAds;

        [Header("Remove Ads Idle Settings")]
        [SerializeField, Range(2.0f, 5.0f)] private float removeAdsMinWait = 3.0f;
        [SerializeField, Range(4.0f, 8.0f)] private float removeAdsMaxWait = 5.0f;

        [Header("Shop Idle Settings")]
        [SerializeField, Range(3.0f, 6.0f)] private float shopMinWait      = 4.0f;
        [SerializeField, Range(5.0f, 9.0f)] private float shopMaxWait      = 6.0f;
        [SerializeField, Range(4f, 14f)]    private float shopLiftPixels   = 8f;

        // ─────────────────────────────────────────────────────────────────────
        // State
        // ─────────────────────────────────────────────────────────────────────

        private Vector2  iconOriginalPos;
        private Sequence idleSequence;
        private Sequence pressSequence;
        private Coroutine idleCoroutine;
        private bool     idleRunning;
        private bool     isPressFeedbackPlaying;

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (iconTransform != null)
                iconOriginalPos = iconTransform.anchoredPosition;
        }

        private void OnDisable()
        {
            StopIdleAnimation();
            KillActiveSequences();
            ResetIcon();
        }

        private void OnDestroy()
        {
            StopIdleAnimation();
            KillActiveSequences();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Begin the idle attention loop. Call this after the Main Menu entry
        /// animation completes so the icon does not animate during the intro.
        /// </summary>
        public void StartIdleAnimation()
        {
            if (iconTransform == null)
            {
                Debug.LogWarning($"[MainMenuButtonAnimator] '{name}' has no Icon Transform assigned.", this);
                return;
            }
            if (idleRunning) return;
            idleRunning   = true;
            idleCoroutine = StartCoroutine(IdleLoop());
        }

        /// <summary>
        /// Stop the idle attention loop. Safe to call even if not running.
        /// </summary>
        public void StopIdleAnimation()
        {
            idleRunning = false;
            if (idleCoroutine != null)
            {
                StopCoroutine(idleCoroutine);
                idleCoroutine = null;
            }
            idleSequence?.Kill();
            idleSequence = null;
        }

        /// <summary>
        /// Plays scale-punch press feedback on the icon, then invokes onComplete.
        /// If feedback is already playing, onComplete is called immediately so
        /// the popup is never blocked.
        /// </summary>
        public void PlayPressAnimation(Action onComplete = null)
        {
            if (iconTransform == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (isPressFeedbackPlaying)
            {
                onComplete?.Invoke();
                return;
            }

            // Pause idle while press plays.
            StopIdleAnimation();
            KillActiveSequences();
            ResetIcon();

            isPressFeedbackPlaying = true;

            pressSequence = DOTween.Sequence();

            // 1.0 → 0.92 (fast compress)
            pressSequence.Append(
                iconTransform.DOScale(Vector3.one * 0.92f, 0.07f)
                             .SetEase(Ease.OutQuad));

            // 0.92 → 1.05 (spring up)
            pressSequence.Append(
                iconTransform.DOScale(Vector3.one * 1.05f, 0.12f)
                             .SetEase(Ease.OutQuad));

            // 1.05 → 1.0 (settle with soft overshoot)
            pressSequence.Append(
                iconTransform.DOScale(Vector3.one, 0.10f)
                             .SetEase(Ease.OutBack));

            pressSequence.OnComplete(() =>
            {
                isPressFeedbackPlaying = false;
                onComplete?.Invoke();
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Idle Loop
        // ─────────────────────────────────────────────────────────────────────

        private IEnumerator IdleLoop()
        {
            while (idleRunning)
            {
                float wait = animationType == AnimationType.RemoveAds
                    ? UnityEngine.Random.Range(removeAdsMinWait, removeAdsMaxWait)
                    : UnityEngine.Random.Range(shopMinWait,      shopMaxWait);

                yield return new WaitForSeconds(wait);

                if (!idleRunning)          yield break;
                if (isPressFeedbackPlaying) continue;

                yield return PlayIdleSequence();
            }
        }

        private IEnumerator PlayIdleSequence()
        {
            bool done = false;

            idleSequence?.Kill();
            idleSequence = animationType == AnimationType.RemoveAds
                ? BuildRemoveAdsSequence()
                : BuildShopSequence();

            idleSequence.OnComplete(() => done = true);

            yield return new WaitUntil(() => done || !idleRunning);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Remove Ads Idle Sequence — icon only
        // ─────────────────────────────────────────────────────────────────────

        private Sequence BuildRemoveAdsSequence()
        {
            Sequence seq = DOTween.Sequence();

            // Scale up: 1.0 → 1.08
            seq.Append(
                iconTransform.DOScale(Vector3.one * 1.08f, 0.10f)
                             .SetEase(Ease.OutCubic));

            // Wobble rotation: 0 → -5° → +5° → -3° → 0°
            seq.Append(
                iconTransform.DOLocalRotate(new Vector3(0f, 0f, -5f), 0.10f)
                             .SetEase(Ease.OutCubic));

            seq.Append(
                iconTransform.DOLocalRotate(new Vector3(0f, 0f,  5f), 0.12f)
                             .SetEase(Ease.InOutCubic));

            seq.Append(
                iconTransform.DOLocalRotate(new Vector3(0f, 0f, -3f), 0.10f)
                             .SetEase(Ease.InOutCubic));

            seq.Append(
                iconTransform.DOLocalRotate(Vector3.zero, 0.10f)
                             .SetEase(Ease.OutCubic));

            // Scale back with micro squash: 1.08 → 1.04 → 1.0 (OutBack pop)
            seq.Append(
                iconTransform.DOScale(Vector3.one * 1.04f, 0.06f)
                             .SetEase(Ease.InQuad));

            seq.Append(
                iconTransform.DOScale(Vector3.one, 0.10f)
                             .SetEase(Ease.OutBack));

            return seq;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Shop Idle Sequence — icon only
        // ─────────────────────────────────────────────────────────────────────

        private Sequence BuildShopSequence()
        {
            Sequence seq = DOTween.Sequence();

            // Scale up pulse
            seq.Append(
                iconTransform.DOScale(Vector3.one * 1.05f, 0.12f)
                             .SetEase(Ease.OutQuad));

            // Lift upward
            seq.Join(
                iconTransform.DOAnchorPosY(iconOriginalPos.y + shopLiftPixels, 0.14f)
                             .SetEase(Ease.OutQuad));

            // Rock left
            seq.Append(
                iconTransform.DOLocalRotate(new Vector3(0f, 0f, -4f), 0.13f)
                             .SetEase(Ease.OutCubic));

            // Rock right
            seq.Append(
                iconTransform.DOLocalRotate(new Vector3(0f, 0f,  4f), 0.14f)
                             .SetEase(Ease.InOutCubic));

            // Return to center rotation
            seq.Append(
                iconTransform.DOLocalRotate(Vector3.zero, 0.11f)
                             .SetEase(Ease.OutCubic));

            // Move back down
            seq.Join(
                iconTransform.DOAnchorPosY(iconOriginalPos.y, 0.14f)
                             .SetEase(Ease.InQuad));

            // Scale back
            seq.Append(
                iconTransform.DOScale(Vector3.one, 0.10f)
                             .SetEase(Ease.OutBack));

            return seq;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private void KillActiveSequences()
        {
            idleSequence?.Kill();
            pressSequence?.Kill();
            idleSequence  = null;
            pressSequence = null;
        }

        private void ResetIcon()
        {
            if (iconTransform == null) return;
            iconTransform.localScale        = Vector3.one;
            iconTransform.localEulerAngles  = Vector3.zero;
            iconTransform.anchoredPosition  = iconOriginalPos;
        }
    }
}
