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

        public int BoltIndex { get; set; }
        public Vector3 HomePosition { get; set; }
        public bool IsLocked { get; private set; }
        public List<NutView> Nuts { get; } = new List<NutView>();

        public Transform NutContainer => nutContainer != null ? nutContainer : transform;
        public BoxCollider Collider => boxCollider != null ? boxCollider : GetComponent<BoxCollider>();

        private void Awake()
        {
            if (boxCollider == null)
                boxCollider = GetComponent<BoxCollider>();

            if (selectionEffect != null) selectionEffect.SetActive(false);
            if (completionEffect != null) completionEffect.SetActive(false);
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
            return new Vector3(0f, 0.43f + index * NutStep, 0f);
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
    }
}
