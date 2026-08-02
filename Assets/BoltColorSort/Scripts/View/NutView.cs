using DG.Tweening;
using TMPro;
using UnityEngine;

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
        [Header("Hidden Nut Visual")]
        [SerializeField] private Material hiddenMaterial;
        [SerializeField] private TextMeshPro questionMarkText;
        [SerializeField] private UnityEngine.Color questionMarkColor = UnityEngine.Color.white;
        [SerializeField, Min(.01f)] private float questionMarkSize = .75f;
        [SerializeField] private Vector3 questionMarkLocalOffset = new Vector3(0f, .12f, -.34f);
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
        private Material realColorMaterial;
        private Material runtimeHiddenMaterial;
        private Sequence revealSequence;

        private void Awake()
        {
            CaptureRestingTransform();

            if (optionalHighlight != null)
                optionalHighlight.SetActive(false);
        }

        /// <summary>
        /// Stores the transform that movement, reveal, and landing animations must return to.
        /// Call this after a spawner has applied its final local transform.
        /// </summary>
        public void CaptureRestingTransform()
        {
            RestingLocalRotation = transform.localRotation;
            RestingLocalScale = transform.localScale;
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
            revealSequence = DOTween.Sequence().SetTarget(tr);
            revealSequence.Append(tr.DOScale(RestingLocalScale * revealAnticipationScale, revealDuration * .25f).SetEase(Ease.InQuad));
            revealSequence.Append(tr.DORotate(Vector3.up * revealRotationDegrees, revealDuration * .50f, RotateMode.LocalAxisAdd).SetEase(Ease.InOutCubic));
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

        private void EnsureQuestionMark()
        {
            if (questionMarkText != null) return;
            var go = new GameObject("QuestionMarkText");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = questionMarkLocalOffset;
            go.transform.localRotation = Quaternion.identity;
            questionMarkText = go.AddComponent<TextMeshPro>();
            questionMarkText.text = "?";
            questionMarkText.alignment = TextAlignmentOptions.Center;
            questionMarkText.fontSize = questionMarkSize;
            questionMarkText.color = questionMarkColor;
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
            if (questionMarkText != null) questionMarkText.gameObject.SetActive(visible);
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
