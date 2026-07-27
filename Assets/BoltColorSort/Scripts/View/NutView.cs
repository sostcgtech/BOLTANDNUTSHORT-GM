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

        public NutColor Color { get; private set; }
        public Quaternion RestingLocalRotation { get; set; }
        public Vector3 RestingLocalScale { get; set; }

        private void Awake()
        {
            RestingLocalRotation = transform.localRotation;
            RestingLocalScale = transform.localScale;

            if (optionalHighlight != null)
                optionalHighlight.SetActive(false);
        }

        /// <summary>
        /// Assigns logical color and material at runtime.
        /// </summary>
        public void Initialize(NutColor nutColor, Material material)
        {
            Color = nutColor;

            if (mainRenderer == null)
            {
                var renderers = GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0) mainRenderer = renderers[0];
                if (renderers.Length > 1) bevelRenderer = renderers[1];
            }

            if (mainRenderer != null && material != null)
            {
                mainRenderer.material = material;
            }
            if (bevelRenderer != null && material != null)
            {
                bevelRenderer.material = material;
            }
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
    }
}
