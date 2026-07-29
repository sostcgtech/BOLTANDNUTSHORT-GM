using DG.Tweening;
using UnityEngine;

namespace NutBoltSort
{
    /// <summary>
    /// Attached by LevelManager to any bolt whose BoltNutStackData.boltType == Expandable.
    /// Manages a 0–4 capacity stage, a procedural height-cover visual, and integration with
    /// GameManager's GetMoveCount override via GetEffectiveCapacity().
    /// </summary>
    [RequireComponent(typeof(BoltView))]
    public class ExpandableBoltController : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Animation")]
        [SerializeField, Min(0.01f)] private float extensionDuration  = 0.28f;
        [SerializeField, Min(0.01f)] private float bounceDuration     = 0.10f;
        [SerializeField, Range(1f, 1.15f)] private float bounceScale  = 1.06f;
        [SerializeField] private Color coverColor = new Color(0.22f, 0.26f, 0.34f);

        // ── Runtime ────────────────────────────────────────────────────────────
        private BoltView    _bolt;
        private int         _currentCapacity;  // 0..4
        private int         _maxCapacity = 4;
        private GameObject  _cover;
        private Sequence    _coverSeq;

        // Cover local-Y positions per stage (derived from BoltView slot positions)
        // Stage N cover sits just above slot N-1 (blocking slots N..3)
        private float[] _coverYPerStage;

        public int CurrentCapacity => _currentCapacity;
        public int MaxCapacity     => _maxCapacity;
        public bool IsAtMax        => _currentCapacity >= _maxCapacity;

        /// <summary>Fired when the capacity changes. Argument is the new capacity.</summary>
        public event System.Action<int> OnCapacityChanged;

        // ── Initialisation ─────────────────────────────────────────────────────

        /// <summary>Called by LevelManager immediately after this component is added to the bolt.</summary>
        public void Initialize(int startCapacity, int maxCapacity = 4)
        {
            _bolt        = GetComponent<BoltView>();
            _maxCapacity = Mathf.Clamp(maxCapacity, 1, BoltView.Capacity);
            _currentCapacity = Mathf.Clamp(startCapacity, 0, _maxCapacity);

            BuildCoverYStages();
            BuildCoverVisual();
            ApplyCoverForCapacity(_currentCapacity, animate: false);

            // Disable collider at stage 0 so the bolt can't be tapped or receive nuts.
            UpdateCollider();
        }

        private void BuildCoverYStages()
        {
            // _coverYPerStage[n] = local Y the cover rests at when capacity == n.
            // At capacity 0: cover sits at slot-0 level (blocks everything).
            // At capacity n: cover sits at slot-n level (slots 0..n-1 are accessible).
            // At max capacity: cover is hidden.
            _coverYPerStage = new float[_maxCapacity + 1];
            if (_bolt == null) return;
            for (int i = 0; i <= _maxCapacity; i++)
            {
                // The cover's bottom edge aligns with slot i; slot i is the first BLOCKED slot.
                _coverYPerStage[i] = _bolt.GetStackPosition(Mathf.Clamp(i, 0, BoltView.Capacity - 1)).y
                                     - 0.10f; // small offset so cover visually caps the slot above
                if (i == 0)
                    _coverYPerStage[i] = _bolt.GetStackPosition(0).y - 0.18f;
            }
        }

        private void BuildCoverVisual()
        {
            if (_cover != null) return;

            _cover = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _cover.name = "ExpandableCover";
            _cover.transform.SetParent(transform, false);
            _cover.transform.localScale = new Vector3(0.74f, 0.06f, 0.74f);

            // Remove the physics collider — cover is purely visual.
            var col = _cover.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Tint it
            var rend = _cover.GetComponent<Renderer>();
            if (rend != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var mat = new Material(shader) { color = coverColor };
                mat.SetFloat("_Metallic", 0.65f);
                mat.SetFloat("_Smoothness", 0.55f);
                rend.material = mat;
            }
        }

        private void ApplyCoverForCapacity(int capacity, bool animate)
        {
            if (_cover == null) return;

            if (capacity >= _maxCapacity)
            {
                // Fully expanded: hide cover
                _cover.SetActive(false);
                return;
            }

            _cover.SetActive(true);
            float targetY = _coverYPerStage != null && capacity < _coverYPerStage.Length
                ? _coverYPerStage[capacity]
                : 0f;

            if (!animate)
            {
                Vector3 lp = _cover.transform.localPosition;
                lp.y = targetY;
                _cover.transform.localPosition = lp;
                return;
            }

            // Animate: slide cover upward + bounce
            _coverSeq?.Kill(false);
            _coverSeq = DOTween.Sequence();
            _coverSeq.Append(_cover.transform.DOLocalMoveY(targetY, extensionDuration).SetEase(Ease.OutCubic));
            _coverSeq.Append(_cover.transform.DOScaleY(_cover.transform.localScale.y * bounceScale, bounceDuration * 0.5f).SetEase(Ease.OutBack));
            _coverSeq.Append(_cover.transform.DOScaleY(0.06f, bounceDuration * 0.5f).SetEase(Ease.InOutSine));
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Increases the bolt capacity by one stage (0→1→2→3→4).
        /// Returns false if already at max or during animation.
        /// </summary>
        public bool IncreaseCapacity()
        {
            if (IsAtMax) return false;
            if (_coverSeq != null && _coverSeq.IsActive() && _coverSeq.IsPlaying()) return false;

            _currentCapacity = Mathf.Min(_currentCapacity + 1, _maxCapacity);
            ApplyCoverForCapacity(_currentCapacity, animate: true);
            UpdateCollider();
            OnCapacityChanged?.Invoke(_currentCapacity);
            return true;
        }

        /// <summary>Resets to the initial starting capacity (used by Restart).</summary>
        public void ResetToStartCapacity(int startCapacity)
        {
            _coverSeq?.Kill(false);
            _currentCapacity = Mathf.Clamp(startCapacity, 0, _maxCapacity);
            ApplyCoverForCapacity(_currentCapacity, animate: false);
            UpdateCollider();
        }

        private void UpdateCollider()
        {
            if (_bolt == null) return;
            // At capacity 0, bolt can't be selected or receive nuts → disable collider.
            bool shouldBeActive = _currentCapacity > 0;
            if (_bolt.Collider != null)
                _bolt.Collider.enabled = shouldBeActive;
        }

        private void OnDestroy()
        {
            _coverSeq?.Kill(false);
        }
    }
}
