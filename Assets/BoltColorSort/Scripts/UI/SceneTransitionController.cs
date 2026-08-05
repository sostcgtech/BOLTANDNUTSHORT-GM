using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace NutBoltSort
{
    /// <summary>
    /// Manages the full-screen color-iris scene transition.
    ///
    /// Usage:
    ///   1. Call PlayClose(onCovered) to animate the shape covering the screen.
    ///   2. Load the target scene asynchronously inside onCovered.
    ///   3. Once the scene is ready, call PlayOpen(onComplete) to reveal it.
    ///
    /// The TransitionShape must be a full-screen capable Image (circle/rounded sprite).
    /// Scale it from 0 to a value large enough to cover the screen in all orientations.
    ///
    /// Do NOT call DOTween.KillAll() anywhere in this script.
    /// </summary>
    [AddComponentMenu("BoltShift/UI/Scene Transition Controller")]
    [DisallowMultipleComponent]
    public sealed class SceneTransitionController : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        [Header("Transition Shape")]
        [Tooltip("The Image (circle/rounded sprite) that expands to cover the screen.")]
        [SerializeField] private Image transitionShape;

        [Tooltip("Scale multiplier applied when the shape must fully cover the screen. " +
                 "3.5 comfortably covers a 1080x1920 canvas at default scale.")]
        [SerializeField, Range(2f, 6f)] private float coverScale = 3.5f;

        [Header("Colors")]
        [Tooltip("One of these colors is chosen at random each time a transition plays.")]
        [SerializeField] private Color[] transitionColors = new Color[]
        {
            new Color(0.24f, 0.56f, 1.00f),   // Blue
            new Color(0.22f, 0.76f, 0.44f),   // Green
            new Color(1.00f, 0.80f, 0.10f),   // Yellow
            new Color(0.64f, 0.26f, 0.90f),   // Purple
            new Color(1.00f, 0.52f, 0.14f),   // Orange
        };

        [Header("Timing")]
        [SerializeField, Range(0.28f, 0.55f)] private float closeDuration = 0.40f;
        [SerializeField, Range(0.28f, 0.55f)] private float openDuration  = 0.40f;

        [Header("Optional Loading Text")]
        [SerializeField] private GameObject loadingIndicator;

        // ─────────────────────────────────────────────────────────────────────
        // Static Handshake
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Set by MainMenuManager just before activating the loaded scene.
        /// The gameplay scene's initialisation code should invoke this once
        /// the board is ready to be shown.
        /// Connect: FindAnyObjectByType&lt;SceneTransitionController&gt;?.PlayOpen()
        /// or use the static callback below.
        /// </summary>
        public static Action PendingOpenCallback;

        /// <summary>True while a transition is in progress.</summary>
        public static bool TransitionInFlight { get; private set; }

        // ─────────────────────────────────────────────────────────────────────
        // Private State
        // ─────────────────────────────────────────────────────────────────────

        private Tweener activeTween;

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            // Start hidden.
            if (transitionShape != null)
            {
                transitionShape.transform.localScale = Vector3.zero;
                transitionShape.gameObject.SetActive(false);
            }

            if (loadingIndicator != null)
                loadingIndicator.SetActive(false);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Expands the colored shape to cover the screen.
        /// <param name="onCovered">Invoked when the shape fully covers the screen.</param>
        /// </summary>
        public void PlayClose(Action onCovered = null)
        {
            if (TransitionInFlight) return;
            TransitionInFlight = true;

            SetRandomColor();

            if (transitionShape != null)
            {
                transitionShape.gameObject.SetActive(true);
                transitionShape.transform.localScale = Vector3.zero;
            }

            activeTween?.Kill();
            activeTween = transitionShape != null
                ? transitionShape.transform
                      .DOScale(Vector3.one * coverScale, closeDuration)
                      .SetEase(Ease.InCubic)
                      .SetUpdate(false)
                      .OnComplete(() =>
                      {
                          if (loadingIndicator != null)
                              loadingIndicator.SetActive(true);
                          onCovered?.Invoke();
                      })
                : null;

            if (activeTween == null)
                onCovered?.Invoke();
        }

        /// <summary>
        /// Shrinks the colored shape to reveal the scene beneath.
        /// <param name="onComplete">Invoked when the shape has fully disappeared.</param>
        /// </summary>
        public void PlayOpen(Action onComplete = null)
        {
            if (loadingIndicator != null)
                loadingIndicator.SetActive(false);

            activeTween?.Kill();
            activeTween = transitionShape != null
                ? transitionShape.transform
                      .DOScale(Vector3.zero, openDuration)
                      .SetEase(Ease.OutCubic)
                      .SetUpdate(false)
                      .OnComplete(() =>
                      {
                          if (transitionShape != null)
                              transitionShape.gameObject.SetActive(false);
                          TransitionInFlight = false;
                          onComplete?.Invoke();
                      })
                : null;

            if (activeTween == null)
            {
                TransitionInFlight = false;
                onComplete?.Invoke();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private void SetRandomColor()
        {
            if (transitionShape == null || transitionColors == null || transitionColors.Length == 0)
                return;
            transitionShape.color = transitionColors[UnityEngine.Random.Range(0, transitionColors.Length)];
        }

        private void OnDestroy()
        {
            activeTween?.Kill();
            TransitionInFlight = false;
        }
    }
}
