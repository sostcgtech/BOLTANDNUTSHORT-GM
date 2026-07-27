using System.Collections.Generic;
using UnityEngine;

namespace NutBoltSort
{
    /// <summary>
    /// Component attached to BoltPrefab managing bolt stack state, visual slots, and interaction collider.
    /// </summary>
    public class BoltView : MonoBehaviour
    {
        public const int Capacity = 4;
        public const float NutStep = 0.48f;

        [Header("Hierarchy References")]
        [SerializeField] private Transform nutContainer;
        [SerializeField] private Transform[] stackSlots; // Slot0 (bottom) to Slot3 (top)
        [SerializeField] private GameObject selectionEffect;
        [SerializeField] private GameObject completionEffect;
        [SerializeField] private BoxCollider boxCollider;

        [Header("Slot Spacing Settings")]
        [Tooltip("Vertical spacing distance between consecutive slots.")]
        [SerializeField] private float slotSpacing = 0.48f;

        [Tooltip("Y offset position of the first bottom slot (Slot 0).")]
        [SerializeField] private float bottomOffset = 0.43f;

        [Tooltip("Automatically keep stackSlots child transforms evenly spaced.")]
        [SerializeField] private bool autoAlignStackSlots = true;

        [Header("Gizmos / Scene Preview")]
        [SerializeField] private bool showSlotPreview = true;
        [SerializeField] private bool previewOnlyWhenSelected = false;
        [SerializeField] private float nutPreviewRadius = 0.32f;
        [SerializeField] private float nutPreviewHeight = 0.2f;

        public int BoltIndex { get; set; }
        public Vector3 HomePosition { get; set; }
        /// <summary>Scale captured after prefab setup. Gameplay feedback always returns to this exact value.</summary>
        public Vector3 RestingLocalScale { get; private set; }
        public bool IsLocked { get; private set; }
        public List<NutView> Nuts { get; } = new List<NutView>();

        public Transform NutContainer => nutContainer != null ? nutContainer : transform;
        public BoxCollider Collider => boxCollider != null ? boxCollider : GetComponent<BoxCollider>();

        public float SlotSpacing
        {
            get => slotSpacing;
            set { slotSpacing = value; AlignStackSlots(); }
        }

        public float BottomOffset
        {
            get => bottomOffset;
            set { bottomOffset = value; AlignStackSlots(); }
        }

        private void Awake()
        {
            RestingLocalScale = transform.localScale;
            if (boxCollider == null)
                boxCollider = GetComponent<BoxCollider>();

            if (selectionEffect != null) selectionEffect.SetActive(false);
            if (completionEffect != null) completionEffect.SetActive(false);

            if (autoAlignStackSlots)
            {
                AlignStackSlots();
            }
        }

        private void OnValidate()
        {
            if (autoAlignStackSlots)
            {
                AlignStackSlots();
            }
        }

        /// <summary>
        /// Automatically aligns stack slot transforms so they are perfectly and evenly spaced.
        /// </summary>
        [ContextMenu("Auto-Align Stack Slots Evenly")]
        public void AlignStackSlots()
        {
            EnsureStackSlotsArray();

            for (int i = 0; i < Capacity; i++)
            {
                Vector3 targetLocalPos = new Vector3(0f, bottomOffset + i * slotSpacing, 0f);
                if (stackSlots != null && i < stackSlots.Length && stackSlots[i] != null)
                {
                    stackSlots[i].localPosition = targetLocalPos;
                }
            }
        }

        private void EnsureStackSlotsArray()
        {
            if (stackSlots == null || stackSlots.Length < Capacity)
            {
                Transform parent = NutContainer;
                var foundSlots = new List<Transform>();

                for (int i = 0; i < Capacity; i++)
                {
                    Transform t = parent.Find($"Slot{i}") ?? parent.Find($"Slot_{i}") ?? parent.Find($"Slot {i}");
                    if (t != null) foundSlots.Add(t);
                }

                if (foundSlots.Count == Capacity)
                {
                    stackSlots = foundSlots.ToArray();
                }
            }
        }

        /// <summary>
        /// Gets local position for stack index 0..3 (Slot0 to Slot3).
        /// </summary>
        public Vector3 GetStackPosition(int index)
        {
            if (stackSlots != null && index >= 0 && index < stackSlots.Length && stackSlots[index] != null)
            {
                return stackSlots[index].localPosition;
            }
            return new Vector3(0f, bottomOffset + index * slotSpacing, 0f);
        }

        public void SetSelectionEffect(bool active)
        {
            if (selectionEffect != null) selectionEffect.SetActive(active);
        }

        public void SetCompletionEffect(bool active)
        {
            if (completionEffect != null) completionEffect.SetActive(active);
        }

        /// <summary>
        /// Locks bolt interactions without playing animations (used during silent load validation).
        /// </summary>
        public void LockBoltSilently()
        {
            IsLocked = true;
            if (Collider != null) Collider.enabled = false;
        }

        /// <summary>
        /// Resets runtime state for reuse or initialization.
        /// </summary>
        public void ResetState()
        {
            IsLocked = false;
            if (Collider != null) Collider.enabled = true;
            Nuts.Clear();
            SetSelectionEffect(false);
            SetCompletionEffect(false);
        }

        /// <summary>
        /// Checks if bolt contains Capacity nuts of identical color.
        /// </summary>
        public bool IsComplete()
        {
            if (Nuts.Count != Capacity) return false;
            NutColor firstColor = Nuts[0].Color;
            for (int i = 1; i < Nuts.Count; i++)
            {
                if (Nuts[i].Color != firstColor) return false;
            }
            return true;
        }

        private void OnDrawGizmos()
        {
            if (showSlotPreview && !previewOnlyWhenSelected)
            {
                DrawSlotPreviewGizmos();
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (showSlotPreview && previewOnlyWhenSelected)
            {
                DrawSlotPreviewGizmos();
            }
        }

        private void DrawSlotPreviewGizmos()
        {
            Transform container = NutContainer;
            Vector3 previousPos = Vector3.zero;

            Color[] slotColors = new Color[]
            {
                new Color(0.2f, 0.8f, 1.0f, 0.85f), // Slot 0: Cyan
                new Color(0.2f, 1.0f, 0.4f, 0.85f), // Slot 1: Green
                new Color(1.0f, 0.85f, 0.2f, 0.85f), // Slot 2: Yellow
                new Color(1.0f, 0.35f, 0.65f, 0.85f)  // Slot 3: Magenta
            };

            for (int i = 0; i < Capacity; i++)
            {
                Vector3 localPos = GetStackPosition(i);
                Vector3 worldPos = container.TransformPoint(localPos);

                Gizmos.color = slotColors[i % slotColors.Length];

                // Draw nut preview wireframe bounding shape
                Gizmos.DrawWireCube(worldPos, new Vector3(nutPreviewRadius * 2f, nutPreviewHeight, nutPreviewRadius * 2f));
                Gizmos.DrawSphere(worldPos, 0.04f);

                // Draw alignment path line connecting stack slots
                if (i > 0)
                {
                    Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
                    Gizmos.DrawLine(previousPos, worldPos);
                }
                previousPos = worldPos;

#if UNITY_EDITOR
                UnityEditor.Handles.color = slotColors[i % slotColors.Length];
                GUIStyle style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold
                };
                style.normal.textColor = slotColors[i % slotColors.Length];

                string transformName = (stackSlots != null && i < stackSlots.Length && stackSlots[i] != null) ? $" [{stackSlots[i].name}]" : "";
                string labelText = $"Slot {i}{transformName}";

                UnityEditor.Handles.Label(worldPos + Vector3.right * (nutPreviewRadius + 0.08f), labelText, style);
                UnityEditor.Handles.DrawWireDisc(worldPos, transform.up, nutPreviewRadius);
#endif
            }
        }
    }
}
