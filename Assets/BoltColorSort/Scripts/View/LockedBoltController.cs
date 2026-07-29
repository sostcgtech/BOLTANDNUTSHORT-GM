using DG.Tweening;
using UnityEngine;

namespace NutBoltSort
{
    /// <summary>
    /// Attached by LevelManager to any bolt whose BoltNutStackData.boltType == Locked.
    /// The bolt's collider is disabled until TryUnlock() is called, after which it becomes
    /// a normal empty bolt at full capacity.
    /// </summary>
    [RequireComponent(typeof(BoltView))]
    public class LockedBoltController : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Animation")]
        [SerializeField, Min(0.01f)] private float unlockDuration = 0.25f;
        [SerializeField] private Color lockColor = new Color(0.62f, 0.52f, 0.12f); // dark gold

        // ── Runtime ────────────────────────────────────────────────────────────
        private BoltView   _bolt;
        private GameObject _lockRing;
        private Sequence   _unlockSeq;

        public bool IsUnlocked { get; private set; }

        /// <summary>Fired when the bolt is successfully unlocked.</summary>
        public event System.Action OnUnlocked;

        // ── Initialisation ─────────────────────────────────────────────────────

        /// <summary>Called by LevelManager immediately after this component is added to the bolt.</summary>
        public void Initialize()
        {
            _bolt = GetComponent<BoltView>();
            IsUnlocked = false;

            // Disable collider so the bolt cannot be tapped or receive nuts.
            if (_bolt.Collider != null) _bolt.Collider.enabled = false;

            BuildLockVisual();
        }

        private void BuildLockVisual()
        {
            if (_lockRing != null) return;

            // Create a visible lock band around the top of the bolt shaft.
            _lockRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _lockRing.name = "LockRing";
            _lockRing.transform.SetParent(transform, false);

            // Position at mid-shaft height
            float midY = _bolt != null ? (_bolt.GetStackPosition(0).y + _bolt.GetStackPosition(BoltView.Capacity - 1).y) * 0.5f : 1.0f;
            _lockRing.transform.localPosition = new Vector3(0f, midY, 0f);
            _lockRing.transform.localScale    = new Vector3(0.88f, 0.14f, 0.88f);

            var col = _lockRing.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var rend = _lockRing.GetComponent<Renderer>();
            if (rend != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var mat = new Material(shader) { color = lockColor };
                mat.SetFloat("_Metallic", 0.78f);
                mat.SetFloat("_Smoothness", 0.72f);
                rend.material = mat;
            }
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Removes the lock, enables the bolt, and fires OnUnlocked.
        /// Returns false if already unlocked or during animation.
        /// </summary>
        public bool TryUnlock()
        {
            if (IsUnlocked) return false;
            if (_unlockSeq != null && _unlockSeq.IsActive() && _unlockSeq.IsPlaying()) return false;

            IsUnlocked = true;

            // Animate lock ring shrinking away
            if (_lockRing != null)
            {
                _unlockSeq = DOTween.Sequence();
                _unlockSeq.Append(_lockRing.transform.DOScale(Vector3.zero, unlockDuration).SetEase(Ease.InBack));
                _unlockSeq.OnComplete(() =>
                {
                    if (_lockRing != null) Destroy(_lockRing);
                    EnableBolt();
                    OnUnlocked?.Invoke();
                });
            }
            else
            {
                EnableBolt();
                OnUnlocked?.Invoke();
            }

            return true;
        }

        private void EnableBolt()
        {
            if (_bolt != null && _bolt.Collider != null)
                _bolt.Collider.enabled = true;
        }

        /// <summary>Resets to locked state (used by Restart).</summary>
        public void ResetToLocked()
        {
            _unlockSeq?.Kill(false);
            IsUnlocked = false;
            if (_bolt != null && _bolt.Collider != null)
                _bolt.Collider.enabled = false;

            // Rebuild lock ring if it was destroyed
            if (_lockRing == null) BuildLockVisual();
            else
            {
                _lockRing.SetActive(true);
                _lockRing.transform.localScale = new Vector3(0.88f, 0.14f, 0.88f);
            }
        }

        private void OnDestroy()
        {
            _unlockSeq?.Kill(false);
        }
    }
}
