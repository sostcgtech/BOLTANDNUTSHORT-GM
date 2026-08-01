using System.Collections.Generic;
using UnityEngine;

namespace NutBoltSort
{
    /// <summary>
    /// Component attached to BoltPrefab managing bolt stack state, visual slots, interaction collider,
    /// fixed selection hover point, and completion cap.
    /// </summary>
    public class BoltView : MonoBehaviour
    {
        public const int Capacity = 4;
        public const float NutStep = 0.48f; // kept for backwards compatibility; runtime uses slotSpacing.

        // ── Hierarchy References ───────────────────────────────────────────────
        [Header("Hierarchy References")]
        [SerializeField] private Transform nutContainer;
        [SerializeField] private Transform[] stackSlots; // Slot0 (bottom) to Slot3 (top)
        [SerializeField] private GameObject selectionEffect;
        [SerializeField] private GameObject completionEffect;
        [SerializeField] private BoxCollider boxCollider;

        [Header("Expandable Bolt Stage Models")]
        [Tooltip("Drag the 1-nut-capacity bolt model here.")]
        [SerializeField] private GameObject boltStage1;
        [Tooltip("Drag the 2-nut-capacity bolt model here.")]
        [SerializeField] private GameObject boltStage2;
        [Tooltip("Drag the 3-nut-capacity bolt model here.")]
        [SerializeField] private GameObject boltStage3;
        [Tooltip("Drag the full 4-nut-capacity bolt model here.")]
        [SerializeField] private GameObject boltStage4;

        // ── Slot Spacing ───────────────────────────────────────────────────────
        [Header("Slot Spacing Settings")]
        [Tooltip("Vertical spacing distance between consecutive slots.")]
        [SerializeField] private float slotSpacing = 0.48f;

        [Tooltip("Y offset position of the first bottom slot (Slot 0).")]
        [SerializeField] private float bottomOffset = 0.43f;

        [Tooltip("Automatically keep stackSlots child transforms evenly spaced.")]
        [SerializeField] private bool autoAlignStackSlots = true;

        // ── Special Points ─────────────────────────────────────────────────────
        [Header("Selection & Cap Points (auto-created if null)")]
        [Tooltip("Fixed world position the selected nut always lifts to. Auto-created if null.")]
        [SerializeField] private Transform selectionHoverPoint;

        [Tooltip("World position the completion cap rests at. Auto-created if null.")]
        [SerializeField] private Transform completionCapPoint;

        [Tooltip("Optional point used by the tutorial hand. If omitted, the first (bottom) nut slot is used.")]
        [SerializeField] private Transform tutorialPointerPoint;

        [Tooltip("Fine adjustment for the fallback tutorial target below Slot 0.")]
        [SerializeField] private Vector3 tutorialPointerFallbackOffset = new Vector3(0f, -0.22f, 0f);

        [Tooltip("A child cap instance, or a cap prefab asset to instantiate for every bolt.")]
        [SerializeField] private GameObject completionCap;

        // ── Gizmos ─────────────────────────────────────────────────────────────
        [Header("Gizmos / Scene Preview")]
        [SerializeField] private bool showSlotPreview = true;
        [SerializeField] private bool previewOnlyWhenSelected = false;
        [SerializeField] private float nutPreviewRadius = 0.32f;
        [SerializeField] private float nutPreviewHeight = 0.2f;

        // ── Runtime State ──────────────────────────────────────────────────────
        private MaterialPropertyBlock _capMpb;
        private Vector3 completionCapRestingLocalScale = Vector3.one;

        public int BoltIndex { get; set; }
        public Vector3 HomePosition { get; set; }

        /// <summary>Scale captured after prefab setup. Gameplay feedback always returns to this exact value.</summary>
        public Vector3 RestingLocalScale { get; private set; }
        public bool IsLocked { get; private set; }
        public List<NutView> Nuts { get; } = new List<NutView>();

        public Transform NutContainer => nutContainer != null ? nutContainer : transform;
        public BoxCollider Collider => boxCollider != null ? boxCollider : GetComponent<BoxCollider>();
        public Transform CompletionCapTransform => completionCap != null ? completionCap.transform : null;
        public Vector3 CompletionCapRestingLocalScale => completionCapRestingLocalScale;
        public GameObject BoltStage1 => boltStage1;
        public GameObject BoltStage2 => boltStage2;
        public GameObject BoltStage3 => boltStage3;
        public GameObject BoltStage4 => boltStage4;

        // ── Slot Spacing Public API ────────────────────────────────────────────
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

        // ── Special Point World Positions ──────────────────────────────────────
        /// <summary>Fixed world position the selected nut must always lift to (never depends on stack height).</summary>
        public Vector3 GetHoverWorldPosition()
        {
            EnsureSpecialPoints();
            return selectionHoverPoint != null ? selectionHoverPoint.position : transform.position + Vector3.up * (bottomOffset + (Capacity - 1) * slotSpacing + 0.85f);
        }

        /// <summary>World position the completion cap rests at when the bolt is complete.</summary>
        public Vector3 GetCapWorldPosition()
        {
            EnsureSpecialPoints();
            return completionCapPoint != null ? completionCapPoint.position : transform.position + Vector3.up * (bottomOffset + (Capacity - 1) * slotSpacing + 0.25f);
        }

        /// <summary>World target for tutorial guidance; this never affects gameplay placement.</summary>
        public Vector3 GetTutorialPointerWorldPosition()
        {
            return tutorialPointerPoint != null
                ? tutorialPointerPoint.position
                // Level 1 teaches the player where a nut begins on the bolt, so
                // the fallback deliberately uses Slot 0, not the high hover point.
                : NutContainer.TransformPoint(GetStackPosition(0) + tutorialPointerFallbackOffset);
        }

        // ── Cap Control ────────────────────────────────────────────────────────
        /// <summary>Activates the completion cap and tints it to the given color via MaterialPropertyBlock (no new material created).</summary>
        public void ActivateCap(Color color)
        {
            EnsureSpecialPoints();
            if (completionCap == null) return;
            completionCap.SetActive(true);

            if (_capMpb == null) _capMpb = new MaterialPropertyBlock();
            var rend = completionCap.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                _capMpb.SetColor("_BaseColor", color); // URP
                _capMpb.SetColor("_Color", color);     // Standard
                _capMpb.SetColor("_EmissionColor", color * 0.15f);
                rend.SetPropertyBlock(_capMpb);
            }
        }

        /// <summary>Hides the completion cap and resets it to CompletionCapPoint.</summary>
        public void DeactivateCap()
        {
            if (completionCap == null) return;
            completionCap.SetActive(false);
            completionCap.transform.localScale = completionCapRestingLocalScale;
            if (completionCapPoint != null)
                completionCap.transform.position = completionCapPoint.position;
        }

        // ── Unity Lifecycle ────────────────────────────────────────────────────
        private void Awake()
        {
            RestingLocalScale = transform.localScale;

            if (boxCollider == null)
                boxCollider = GetComponent<BoxCollider>();

            if (selectionEffect != null) selectionEffect.SetActive(false);
            if (completionEffect != null) completionEffect.SetActive(false);

            if (autoAlignStackSlots)
                AlignStackSlots();

            EnsureSpecialPoints();
            if (completionCap != null)
                completionCapRestingLocalScale = completionCap.transform.localScale;
        }

        private void OnValidate()
        {
            if (autoAlignStackSlots)
                AlignStackSlots();
        }

        // ── Special Points Auto-Creation ───────────────────────────────────────
        private void EnsureSpecialPoints()
        {
            float topY = bottomOffset + (Capacity - 1) * slotSpacing;
            bool capWasCreated = false;

            // SelectionHoverPoint
            if (selectionHoverPoint == null)
            {
                var existing = transform.Find("SelectionHoverPoint");
                if (existing != null)
                {
                    selectionHoverPoint = existing;
                }
                else
                {
                    var go = new GameObject("SelectionHoverPoint");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = new Vector3(0f, topY + 0.85f, 0f);
                    selectionHoverPoint = go.transform;
                }
            }

            // CompletionCapPoint
            if (completionCapPoint == null)
            {
                var existing = transform.Find("CompletionCapPoint");
                if (existing != null)
                {
                    completionCapPoint = existing;
                }
                else
                {
                    var go = new GameObject("CompletionCapPoint");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = new Vector3(0f, topY + 0.22f, 0f);
                    completionCapPoint = go.transform;
                }
            }

            // Completion Cap GameObject. BoltPrefab currently stores a reference to
            // Cap.prefab, which is an asset rather than a scene object. Assets cannot
            // be activated, moved, or tweened at runtime, so make a bolt-local
            // instance before any cap operation.
            if (completionCap != null && !completionCap.scene.IsValid())
            {
                completionCap = Instantiate(completionCap, transform);
                completionCap.name = "CompletionCap";
                capWasCreated = true;
            }

            if (completionCap == null)
            {
                var existing = transform.Find("CompletionCap");
                if (existing != null)
                {
                    completionCap = existing.gameObject;
                }
                else
                {
                    completionCap = CreateProceduralCap();
                }
                capWasCreated = true;
            }

            if (capWasCreated)
            {
                completionCap.SetActive(false);
                if (completionCapPoint != null)
                    completionCap.transform.position = completionCapPoint.position;
                completionCapRestingLocalScale = completionCap.transform.localScale;
            }
        }

        private GameObject CreateProceduralCap()
        {
            var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cap.name = "CompletionCap";
            cap.transform.SetParent(transform, false);
            cap.transform.localScale = new Vector3(0.72f, 0.055f, 0.72f);

            // Remove physics collider — cap is purely visual.
            var col = cap.GetComponent<Collider>();
            if (col != null)
            {
#if UNITY_EDITOR
                DestroyImmediate(col);
#else
                Destroy(col);
#endif
            }
            return cap;
        }

        // ── Stack Slot Alignment ───────────────────────────────────────────────
        /// <summary>Automatically aligns stack slot transforms so they are perfectly and evenly spaced.</summary>
        [ContextMenu("Auto-Align Stack Slots Evenly")]
        public void AlignStackSlots()
        {
            EnsureStackSlotsArray();
            for (int i = 0; i < Capacity; i++)
            {
                Vector3 targetLocalPos = new Vector3(0f, bottomOffset + i * slotSpacing, 0f);
                if (stackSlots != null && i < stackSlots.Length && stackSlots[i] != null)
                    stackSlots[i].localPosition = targetLocalPos;
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
                    stackSlots = foundSlots.ToArray();
            }
        }

        /// <summary>Gets local position for stack index 0..3 (Slot0 to Slot3).</summary>
        public Vector3 GetStackPosition(int index)
        {
            if (stackSlots != null && index >= 0 && index < stackSlots.Length && stackSlots[index] != null)
                return stackSlots[index].localPosition;
            return new Vector3(0f, bottomOffset + index * slotSpacing, 0f);
        }

        // ── Effects ────────────────────────────────────────────────────────────
        public void SetSelectionEffect(bool active)
        {
            if (selectionEffect != null) selectionEffect.SetActive(active);
        }

        public void SetCompletionEffect(bool active)
        {
            if (completionEffect != null) completionEffect.SetActive(active);
        }

        /// <summary>Locks bolt interactions without playing animations (used during silent load validation).</summary>
        public void LockBoltSilently()
        {
            IsLocked = true;
            if (Collider != null) Collider.enabled = false;
        }

        /// <summary>Reverses LockBoltSilently — re-enables the bolt for interaction without clearing Nuts or effects.</summary>
        public void UnlockSilently()
        {
            IsLocked = false;
            if (Collider != null) Collider.enabled = true;
        }

        /// <summary>Resets runtime state for reuse or initialization.</summary>
        public void ResetState()
        {
            IsLocked = false;
            if (Collider != null) Collider.enabled = true;
            Nuts.Clear();
            SetSelectionEffect(false);
            SetCompletionEffect(false);
            DeactivateCap();
        }

        /// <summary>Checks if bolt is full of one revealed color. Hidden nuts are unresolved blockers.</summary>
        public bool IsComplete()
        {
            if (Nuts.Count != Capacity) return false;

            NutView firstNut = Nuts[0];
            if (firstNut == null || firstNut.IsHidden) return false;

            NutColor firstColor = firstNut.Color;
            for (int i = 1; i < Nuts.Count; i++)
            {
                NutView nut = Nuts[i];
                if (nut == null || nut.IsHidden || nut.Color != firstColor) return false;
            }
            return true;
        }

        // ── Gizmos ─────────────────────────────────────────────────────────────
        private void OnDrawGizmos()
        {
            if (showSlotPreview && !previewOnlyWhenSelected)
                DrawSlotPreviewGizmos();
        }

        private void OnDrawGizmosSelected()
        {
            if (showSlotPreview && previewOnlyWhenSelected)
                DrawSlotPreviewGizmos();
        }

        private void DrawSlotPreviewGizmos()
        {
            Transform container = NutContainer;
            Vector3 previousPos = Vector3.zero;

            Color[] slotColors =
            {
                new Color(0.2f, 0.8f, 1.0f, 0.85f),  // Slot 0: Cyan
                new Color(0.2f, 1.0f, 0.4f, 0.85f),  // Slot 1: Green
                new Color(1.0f, 0.85f, 0.2f, 0.85f), // Slot 2: Yellow
                new Color(1.0f, 0.35f, 0.65f, 0.85f) // Slot 3: Magenta
            };

            for (int i = 0; i < Capacity; i++)
            {
                Vector3 localPos  = GetStackPosition(i);
                Vector3 worldPos  = container.TransformPoint(localPos);
                Gizmos.color      = slotColors[i % slotColors.Length];
                Gizmos.DrawWireCube(worldPos, new Vector3(nutPreviewRadius * 2f, nutPreviewHeight, nutPreviewRadius * 2f));
                Gizmos.DrawSphere(worldPos, 0.04f);
                if (i > 0) { Gizmos.color = new Color(1f, 1f, 1f, 0.5f); Gizmos.DrawLine(previousPos, worldPos); }
                previousPos = worldPos;

#if UNITY_EDITOR
                UnityEditor.Handles.color = slotColors[i % slotColors.Length];
                var style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold
                };
                style.normal.textColor = slotColors[i % slotColors.Length];
                string tName     = (stackSlots != null && i < stackSlots.Length && stackSlots[i] != null) ? $" [{stackSlots[i].name}]" : "";
                UnityEditor.Handles.Label(worldPos + Vector3.right * (nutPreviewRadius + 0.08f), $"Slot {i}{tName}", style);
                UnityEditor.Handles.DrawWireDisc(worldPos, transform.up, nutPreviewRadius);
#endif
            }

            // Draw hover & cap points
#if UNITY_EDITOR
            if (selectionHoverPoint != null)
            {
                Gizmos.color = new Color(1f, 1f, 0f, 0.9f);
                Gizmos.DrawWireSphere(selectionHoverPoint.position, 0.12f);
                UnityEditor.Handles.Label(selectionHoverPoint.position + Vector3.right * 0.15f, "Hover", new GUIStyle(GUI.skin.label) { normal = { textColor = Color.yellow }, fontStyle = FontStyle.Bold, fontSize = 11 });
            }
            if (completionCapPoint != null)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
                Gizmos.DrawWireSphere(completionCapPoint.position, 0.10f);
                UnityEditor.Handles.Label(completionCapPoint.position + Vector3.right * 0.15f, "Cap", new GUIStyle(GUI.skin.label) { normal = { textColor = new Color(1f, 0.5f, 0f) }, fontStyle = FontStyle.Bold, fontSize = 11 });
            }
#endif
        }
    }
}
