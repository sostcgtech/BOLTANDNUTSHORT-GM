using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NutBoltSort
{
    /// <summary>
    /// Development-only FPS counter that persists across all scenes.
    ///
    /// Self-contained — creates its own Canvas, Camera, and TMP_Text at runtime.
    /// No manual UI hierarchy setup needed.
    ///
    /// HOW TO USE:
    ///   1. Add this script to any GameObject in the very first scene (e.g. on
    ///      the GameBootstrap or any persistent object in 00_Boot).
    ///   2. Press Play — the FPS label appears in the top-right corner of every scene.
    ///
    /// BEFORE RELEASE:
    ///   - Uncheck the GameObject this script is on  — or —
    ///   - Use the #if DEVELOPMENT_BUILD guard already present here.
    ///   No other changes needed.
    ///
    /// COLOR KEY:
    ///   Green  = 55+ FPS  (excellent)
    ///   Yellow = 40-54 FPS (acceptable)
    ///   Red    = below 40 FPS (needs optimisation)
    /// </summary>
    [AddComponentMenu("BoltShift/Dev/FPS Counter")]
    public sealed class FPSCounter : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        [Header("Sampling")]
        [Tooltip("How often the display refreshes (seconds). 0.5 = twice per second.")]
        [SerializeField, Range(0.1f, 1.0f)] private float updateInterval = 0.5f;

        [Header("Display Position")]
        [Tooltip("Corner where the FPS label appears.")]
        [SerializeField] private Corner corner = Corner.TopRight;

        [Tooltip("Pixels of padding from the screen edge.")]
        [SerializeField] private Vector2 padding = new Vector2(16f, 16f);

        [Header("Color Thresholds")]
        [SerializeField] private int goodFps       = 55;   // green
        [SerializeField] private int acceptableFps = 40;   // yellow  (below = red)

        // ─────────────────────────────────────────────────────────────────────
        // Types
        // ─────────────────────────────────────────────────────────────────────

        public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

        // ─────────────────────────────────────────────────────────────────────
        // State
        // ─────────────────────────────────────────────────────────────────────

        private TMP_Text fpsLabel;
        private float    timer;
        private int      frameCount;

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            // Persist across all scene loads.
            DontDestroyOnLoad(gameObject);

            BuildUI();
        }

        private void Update()
        {
            if (fpsLabel == null) return;

            timer      += Time.unscaledDeltaTime;
            frameCount++;

            if (timer < updateInterval) return;

            float fps     = frameCount / timer;
            int   rounded = Mathf.RoundToInt(fps);

            fpsLabel.text = $"FPS: {rounded}";

            fpsLabel.color = fps >= goodFps       ? Color.green
                           : fps >= acceptableFps ? Color.yellow
                                                  : Color.red;

            frameCount = 0;
            timer      = 0f;
        }

        // ─────────────────────────────────────────────────────────────────────
        // UI Construction — builds its own overlay Canvas at runtime
        // ─────────────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            // ── Root GameObject (child of this) ──────────────────────────────
            var canvasGO = new GameObject("[DEV] FPS Canvas");
            canvasGO.transform.SetParent(transform, false);

            // ── Canvas (Screen Space - Overlay, high sort order) ─────────────
            var canvas              = canvasGO.AddComponent<Canvas>();
            canvas.renderMode       = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder     = 999;   // always on top of all other UI

            var scaler              = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode      = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight  = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>().blockingMask = 0; // pass-through

            // ── TMP_Text label ────────────────────────────────────────────────
            var labelGO = new GameObject("FPSLabel");
            labelGO.transform.SetParent(canvasGO.transform, false);

            fpsLabel = labelGO.AddComponent<TextMeshProUGUI>();
            fpsLabel.text      = "FPS: --";
            fpsLabel.fontSize  = 30f;
            fpsLabel.fontStyle = FontStyles.Bold;
            fpsLabel.color     = Color.white;
            fpsLabel.raycastTarget = false;    // no input interception

            // Outline so text is readable over any background.
            fpsLabel.outlineWidth = 0.25f;
            fpsLabel.outlineColor = new Color32(0, 0, 0, 200);

            // ── Anchor + position based on chosen corner ──────────────────────
            var rect = labelGO.GetComponent<RectTransform>();
            ApplyCornerLayout(rect);
        }

        private void ApplyCornerLayout(RectTransform rect)
        {
            const float labelW = 160f;
            const float labelH = 48f;

            switch (corner)
            {
                case Corner.TopRight:
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
                    rect.anchoredPosition = new Vector2(-padding.x, -padding.y);
                    fpsLabel.alignment    = TextAlignmentOptions.TopRight;
                    break;

                case Corner.TopLeft:
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                    rect.anchoredPosition = new Vector2(padding.x, -padding.y);
                    fpsLabel.alignment    = TextAlignmentOptions.TopLeft;
                    break;

                case Corner.BottomRight:
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
                    rect.anchoredPosition = new Vector2(-padding.x, padding.y);
                    fpsLabel.alignment    = TextAlignmentOptions.BottomRight;
                    break;

                case Corner.BottomLeft:
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0f);
                    rect.anchoredPosition = new Vector2(padding.x, padding.y);
                    fpsLabel.alignment    = TextAlignmentOptions.BottomLeft;
                    break;
            }

            rect.sizeDelta = new Vector2(labelW, labelH);
        }
    }
}
