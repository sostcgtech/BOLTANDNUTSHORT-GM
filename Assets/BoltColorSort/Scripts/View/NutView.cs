using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace NutBoltSort
{
    /// <summary>
    /// Component attached to NutPrefab managing visual rendering, material assignment, and local transform state.
    /// </summary>
    public class NutView : MonoBehaviour
    {
        [Header("Visual Components")]
        [SerializeField] private Renderer mainRenderer;
        [SerializeField] private Renderer bevelRenderer;
        [SerializeField] private GameObject optionalHighlight;
        [SerializeField] private Transform rotatingNutVisual;
        [Header("Hidden Nut Visual")]
        [SerializeField] private Material hiddenMaterial;
        [SerializeField] private Transform questionMarkAnchor;
        [SerializeField] private TextMeshPro questionMarkText;
        [SerializeField] private UnityEngine.Color questionMarkColor = UnityEngine.Color.white;
        [Tooltip("The TextMeshPro font size used by the hidden-nut question mark.")]
        [FormerlySerializedAs("questionMarkSize")]
        [SerializeField, Min(.01f)] private float questionMarkFontSize = 3f;
        [Tooltip("Centre of the visible front face in NutRoot local space. The negative Z side faces the gameplay camera.")]
        [FormerlySerializedAs("questionMarkLocalOffset")]
        [SerializeField] private Vector3 questionMarkFrontFaceLocalPosition = new Vector3(0f, 0f, -.04f);
        [Tooltip("World-space distance kept between the front face and the question-mark text.")]
        [SerializeField, Range(.005f, .02f)] private float questionMarkSurfaceOffset = .012f;
        [SerializeField, Min(.01f)] private float questionMarkScale = 1f;
        [SerializeField] private bool faceCamera = true;
        [SerializeField] private bool useContinuousBillboard = true;
        [SerializeField, Range(0f, 1f)] private float textOutlineWidth = .18f;
        [SerializeField] private Camera gameplayCamera;
        [SerializeField, Min(.01f)] private float revealDuration = .30f;
        [SerializeField] private float revealRotationDegrees = 180f;
        [SerializeField, Range(.7f, 1f)] private float revealAnticipationScale = .88f;

        public NutColor Color { get; private set; }
        public bool StartsHidden { get; private set; }
        public bool IsHidden => StartsHidden && !IsRevealed;
        public bool IsRevealed { get; private set; }
        public bool IsRevealing { get; private set; }
        public Quaternion RestingLocalRotation { get; set; }
        public Vector3 RestingLocalScale { get; set; }
        public Quaternion RestingVisualLocalRotation { get; private set; }
        public Transform RotatingNutVisual => rotatingNutVisual != null ? rotatingNutVisual : transform;
        private Material realColorMaterial;
        private Material runtimeHiddenMaterial;
        private Sequence revealSequence;
        private MaterialPropertyBlock completionGlowProperties;
        private bool cameraLookupAttempted;

        private void Awake()
        {
            CaptureRestingTransform();
            CacheGameplayCamera();

            if (optionalHighlight != null)
                optionalHighlight.SetActive(false);
        }

        private void OnValidate()
        {
            if (questionMarkText == null) return;
            ApplyQuestionMarkTextSettings();
        }

        /// <summary>
        /// Stores the transform that movement, reveal, and landing animations must return to.
        /// Call this after a spawner has applied its final local transform.
        /// </summary>
        public void CaptureRestingTransform()
        {
            RestingLocalRotation = transform.localRotation;
            RestingLocalScale = transform.localScale;
            RestingVisualLocalRotation = RotatingNutVisual.localRotation;
        }

        /// <summary>
        /// Assigns logical color and material at runtime.
        /// </summary>
        public void Initialize(NutColor nutColor, Material material, bool startsHidden = false)
        {
            Color = nutColor;
            StartsHidden = startsHidden;
            IsRevealed = !startsHidden;

            if (mainRenderer == null)
            {
                var renderers = GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0) mainRenderer = renderers[0];
                if (renderers.Length > 1) bevelRenderer = renderers[1];
            }

            realColorMaterial = material;
            EnsureQuestionMark();
            ApplyDisplayMaterial(IsHidden ? GetHiddenMaterial() : realColorMaterial);
            SetQuestionMarkVisible(IsHidden);
        }

        /// <summary>Silently exposes a starting top nut during board construction.</summary>
        public void RevealSilently()
        {
            if (!IsHidden) return;
            IsRevealed = true;
            ApplyDisplayMaterial(realColorMaterial);
            SetQuestionMarkVisible(false);
        }

        /// <summary>Reveals exactly once. The caller owns bolt reservation and waits for completion.</summary>
        public Sequence Reveal()
        {
            if (!IsHidden || IsRevealing) return null;
            IsRevealing = true;
            revealSequence?.Kill(false);
            Transform tr = transform;
            Transform visual = RotatingNutVisual;
            revealSequence = DOTween.Sequence().SetTarget(tr);
            revealSequence.Append(tr.DOScale(RestingLocalScale * revealAnticipationScale, revealDuration * .25f).SetEase(Ease.InQuad));
            revealSequence.Append(visual.DORotate(Vector3.up * revealRotationDegrees, revealDuration * .50f, RotateMode.LocalAxisAdd).SetEase(Ease.InOutCubic));
            revealSequence.InsertCallback(revealDuration * .48f, () =>
            {
                IsRevealed = true;
                ApplyDisplayMaterial(realColorMaterial);
                SetQuestionMarkVisible(false);
            });
            revealSequence.Append(tr.DOScale(RestingLocalScale, revealDuration * .25f).SetEase(Ease.OutBack));
            revealSequence.OnComplete(() =>
            {
                tr.localRotation = RestingLocalRotation;
                tr.localScale = RestingLocalScale;
                visual.localRotation = RestingVisualLocalRotation;
                IsRevealing = false;
                revealSequence = null;
            });
            return revealSequence;
        }

        public void CancelReveal()
        {
            if (revealSequence != null && revealSequence.IsActive()) revealSequence.Kill(false);
            revealSequence = null;
            IsRevealing = false;
        }

        public void ResetHiddenToStart()
        {
            CancelReveal();
            IsRevealed = !StartsHidden;
            ApplyDisplayMaterial(IsHidden ? GetHiddenMaterial() : realColorMaterial);
            SetQuestionMarkVisible(IsHidden);
        }

        /// <summary>Applies a temporary completion-wave emissive pulse without creating material instances.</summary>
        public void SetCompletionGlow(UnityEngine.Color color, float strength)
        {
            ApplyCompletionGlow(mainRenderer, color, strength);
            if (bevelRenderer != mainRenderer) ApplyCompletionGlow(bevelRenderer, color, strength);
        }

        private void ApplyCompletionGlow(Renderer renderer, UnityEngine.Color color, float strength)
        {
            if (renderer == null || renderer.sharedMaterial == null || !renderer.sharedMaterial.HasProperty("_EmissionColor")) return;
            if (completionGlowProperties == null) completionGlowProperties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(completionGlowProperties);
            completionGlowProperties.SetColor("_EmissionColor", color * Mathf.Max(0f, strength));
            renderer.SetPropertyBlock(completionGlowProperties);
        }

        private void EnsureQuestionMark()
        {
            if (rotatingNutVisual == null)
            {
                // Preserve the same separation for the procedural fallback and older
                // prefab variants: all current mesh children become the spinning visual.
                var visual = new GameObject("RotatingNutVisual").transform;
                visual.SetParent(transform, false);
                for (int i = transform.childCount - 2; i >= 0; i--)
                {
                    Transform child = transform.GetChild(i);
                    if (child != questionMarkAnchor && (questionMarkAnchor == null || !child.IsChildOf(questionMarkAnchor)))
                        child.SetParent(visual, true);
                }
                rotatingNutVisual = visual;
                RestingVisualLocalRotation = Quaternion.identity;
            }

            if (questionMarkAnchor == null)
            {
                var anchor = new GameObject("QuestionMarkAnchor");
                anchor.transform.SetParent(transform, false);
                questionMarkAnchor = anchor.transform;
            }

            if (questionMarkText == null)
            {
                var go = new GameObject("QuestionMarkText");
                go.transform.SetParent(questionMarkAnchor, false);
                questionMarkText = go.AddComponent<TextMeshPro>();
            }

            if (questionMarkText.transform.parent != questionMarkAnchor)
                questionMarkText.transform.SetParent(questionMarkAnchor, false);

            ResetQuestionMarkVisual();
            ApplyQuestionMarkTextSettings();
        }

        private void ApplyQuestionMarkTextSettings()
        {
            if (questionMarkText == null) return;
            questionMarkText.text = "?";
            questionMarkText.alignment = TextAlignmentOptions.Center;
            questionMarkText.fontSize = questionMarkFontSize;
            questionMarkText.color = questionMarkColor;
            questionMarkText.fontStyle = FontStyles.Bold;
            // Outline width is exposed for the authored TMP material. Do not assign
            // fontMaterial here: that would create a material instance per hidden nut.
        }

        private void ResetQuestionMarkVisual()
        {
            if (questionMarkAnchor == null) return;

            float zScale = Mathf.Max(.0001f, Mathf.Abs(transform.lossyScale.z));
            float localSurfaceOffset = questionMarkSurfaceOffset / zScale;
            questionMarkAnchor.localPosition = questionMarkFrontFaceLocalPosition + Vector3.back * localSurfaceOffset;
            questionMarkAnchor.localScale = Vector3.one * questionMarkScale;
            if (questionMarkText != null)
            {
                questionMarkText.transform.localPosition = Vector3.zero;
                // TMP's front is opposite this prefab's anchor forward direction.
                // Flip the glyph child only; the anchor still owns camera billboard rotation.
                questionMarkText.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                questionMarkText.transform.localScale = Vector3.one;
            }
            OrientQuestionMarkToCamera();
        }

        private void LateUpdate()
        {
            if (IsHidden && useContinuousBillboard && questionMarkAnchor != null && questionMarkAnchor.gameObject.activeSelf)
                OrientQuestionMarkToCamera();
        }

        /// <summary>Refreshes the cached-camera billboard after a move or camera change.</summary>
        public void OrientQuestionMarkToCamera()
        {
            if (!faceCamera || questionMarkAnchor == null) return;
            if (gameplayCamera == null) CacheGameplayCamera();
            if (gameplayCamera == null) return;

            // TextMeshPro's readable front points away from the viewing camera.
            // Using the inverse camera direction keeps the glyph upright instead of mirrored.
            Vector3 awayFromCamera = questionMarkAnchor.position - gameplayCamera.transform.position;
            if (awayFromCamera.sqrMagnitude > .000001f)
                questionMarkAnchor.rotation = Quaternion.LookRotation(awayFromCamera, gameplayCamera.transform.up);
        }

        private void CacheGameplayCamera()
        {
            if (gameplayCamera != null || cameraLookupAttempted) return;
            cameraLookupAttempted = true;
            gameplayCamera = Camera.main;
        }

        private Material GetHiddenMaterial()
        {
            if (hiddenMaterial != null) return hiddenMaterial;
            if (runtimeHiddenMaterial == null && realColorMaterial != null)
            {
                runtimeHiddenMaterial = new Material(realColorMaterial);
                UnityEngine.Color gray = new UnityEngine.Color(.52f, .55f, .59f, 1f);
                runtimeHiddenMaterial.color = gray;
                if (runtimeHiddenMaterial.HasProperty("_BaseColor")) runtimeHiddenMaterial.SetColor("_BaseColor", gray);
            }
            return runtimeHiddenMaterial;
        }

        private void ApplyDisplayMaterial(Material material)
        {
            if (mainRenderer != null && material != null) mainRenderer.material = material;
            if (bevelRenderer != null && material != null) bevelRenderer.material = material;
        }

        private void SetQuestionMarkVisible(bool visible)
        {
            if (questionMarkAnchor == null && visible) EnsureQuestionMark();
            if (questionMarkAnchor == null) return;
            if (visible)
            {
                ResetQuestionMarkVisual();
                questionMarkAnchor.gameObject.SetActive(true);
            }
            else questionMarkAnchor.gameObject.SetActive(false);
        }

        /// <summary>
        /// Enables or disables optional selection highlight effect.
        /// </summary>
        public void SetHighlight(bool active)
        {
            if (optionalHighlight != null)
            {
                optionalHighlight.SetActive(active);
            }
        }

        /// <summary>
        /// Returns the Unity Color associated with this nut's NutColor enum value.
        /// </summary>
        public UnityEngine.Color GetUnityColor() => NutColorToUnityColor(Color);

        /// <summary>
        /// Static lookup matching LevelManager's default palette. Used by cap coloring via MaterialPropertyBlock.
        /// </summary>
        public static UnityEngine.Color NutColorToUnityColor(NutColor color)
        {
            switch (color)
            {
                case NutColor.Red:    return new UnityEngine.Color(.95f, .19f, .20f);
                case NutColor.Blue:   return new UnityEngine.Color(.12f, .48f, .96f);
                case NutColor.Green:  return new UnityEngine.Color(.16f, .76f, .43f);
                case NutColor.Yellow: return new UnityEngine.Color(1f,   .68f, .08f);
                case NutColor.Purple: return new UnityEngine.Color(.58f, .12f, .90f);
                case NutColor.Orange: return new UnityEngine.Color(1f,   .50f, .10f);
                case NutColor.Pink:   return new UnityEngine.Color(.96f, .30f, .58f);
                case NutColor.Cyan:   return new UnityEngine.Color(.08f, .78f, .90f);
                case NutColor.Lime:   return new UnityEngine.Color(.55f, .86f, .14f);
                case NutColor.White:  return new UnityEngine.Color(.88f, .91f, .95f);
                case NutColor.DarkBlue:return new UnityEngine.Color(.15f, .27f, .66f);
                case NutColor.Magenta:return new UnityEngine.Color(.84f, .12f, .72f);
                default:              return UnityEngine.Color.white;
            }
        }
    }
}
