using DG.Tweening;
using UnityEngine;

namespace NutBoltSort
{
    /// <summary>
    /// Controls the visual and capacity stages of an expandable bolt using the four stage
    /// models assigned on BoltView. Capacity is committed only after a stage-growth tween ends.
    /// </summary>
    [RequireComponent(typeof(BoltView))]
    public class ExpandableBoltController : MonoBehaviour
    {
        [Header("Stage Animation")]
        [SerializeField, Min(.01f)] private float unlockDuration = .30f;
        [SerializeField, Min(.01f)] private float extensionDuration = .40f;
        [SerializeField, Min(.01f)] private float settleDuration = .15f;
        [SerializeField, Range(1f, 1.15f)] private float unlockPulseScale = 1.06f;
        [SerializeField, Range(.4f, .8f)] private float lockedBrightness = .56f;

        private BoltView _bolt;
        private GameObject[] _stages;
        private Vector3[] _stageRestScales;
        private int[] _verticalScaleAxes;
        private float _baseBottomWorldY;
        private int _currentCapacity;
        private int _maxCapacity = BoltView.Capacity;
        private bool _stageOneLocked;
        private Sequence _stageSequence;
        private MaterialPropertyBlock _propertyBlock;

        public int CurrentCapacity => _currentCapacity;
        public int MaxCapacity => _maxCapacity;
        public bool IsAtMax => !_stageOneLocked && _currentCapacity >= _maxCapacity;
        public bool IsExpanding => _stageSequence != null && _stageSequence.IsActive() && _stageSequence.IsPlaying();

        /// <summary>Fired only after a visually completed stage becomes available for stacking.</summary>
        public event System.Action<int> OnCapacityChanged;

        /// <summary>Called by LevelManager after the controller is added to a runtime bolt.</summary>
        public void Initialize(int startCapacity, int maxCapacity = BoltView.Capacity)
        {
            _bolt = GetComponent<BoltView>();
            _maxCapacity = Mathf.Clamp(maxCapacity, 1, BoltView.Capacity);
            _stages = new[] { _bolt.BoltStage1, _bolt.BoltStage2, _bolt.BoltStage3, _bolt.BoltStage4 };
            _stageRestScales = new Vector3[_stages.Length];
            _verticalScaleAxes = new int[_stages.Length];

            // Imported stage meshes can have different pivot positions. Aligning their visual
            // bottoms once gives every later transition the same fixed base to grow from.
            AlignStagesToSharedBase();
            for (int i = 0; i < _stages.Length; i++)
            {
                if (_stages[i] == null) continue;
                _stageRestScales[i] = _stages[i].transform.localScale;
                _verticalScaleAxes[i] = GetVerticalScaleAxis(_stages[i].transform);
            }

            DisableLegacyCylinder();

            // The staged design always begins as a visible, locked one-slot bolt.  The supplied
            // startCapacity is intentionally not used to pre-open later visual stages.
            _currentCapacity = 1;
            _stageOneLocked = true;
            ApplyImmediateStage(1, stageOneLocked: true);
        }

        /// <summary>
        /// Accepts one Expand use. First use unlocks Stage 1; later uses reveal one new stage.
        /// </summary>
        public bool IncreaseCapacity()
        {
            if (IsExpanding || IsAtMax || _stages == null) return false;

            if (_stageOneLocked)
            {
                return PlayStageOneUnlock();
            }

            int nextCapacity = Mathf.Min(_currentCapacity + 1, _maxCapacity);
            if (nextCapacity == _currentCapacity) return false;
            return PlayStageGrowth(_currentCapacity, nextCapacity);
        }

        /// <summary>Restores the staged bolt to its original locked one-slot presentation.</summary>
        public void ResetToStartCapacity(int startCapacity)
        {
            _stageSequence?.Kill(false);
            _stageSequence = null;
            _currentCapacity = 1;
            _stageOneLocked = true;
            ApplyImmediateStage(1, stageOneLocked: true);
        }

        private bool PlayStageOneUnlock()
        {
            GameObject stageOne = GetStage(1);
            if (stageOne == null) return false;

            SetColliderEnabled(false);
            stageOne.SetActive(true);
            SetStageBrightness(stageOne, lockedBrightness);
            Vector3 restScale = GetRestScale(1);

            _stageSequence?.Kill(false);
            _stageSequence = DOTween.Sequence().SetTarget(this);
            _stageSequence.Append(DOTween.To(
                () => lockedBrightness,
                value => SetStageBrightness(stageOne, value),
                1f, unlockDuration).SetEase(Ease.OutCubic));
            _stageSequence.Join(DOTween.To(() => 1f, value =>
            {
                stageOne.transform.localScale = restScale * Mathf.Lerp(1f, unlockPulseScale, value);
                AnchorStageBottom(stageOne);
            }, 1f, unlockDuration * .55f).SetEase(Ease.OutCubic));
            _stageSequence.Append(DOTween.To(() => unlockPulseScale, value =>
            {
                stageOne.transform.localScale = restScale * value;
                AnchorStageBottom(stageOne);
            }, 1f, settleDuration).SetEase(Ease.OutBack));
            _stageSequence.OnComplete(() =>
            {
                _stageOneLocked = false;
                ClearStageBrightness(stageOne);
                SetColliderEnabled(true);
                OnCapacityChanged?.Invoke(_currentCapacity);
                _stageSequence = null;
            });
            return true;
        }

        private bool PlayStageGrowth(int currentCapacity, int nextCapacity)
        {
            GameObject currentStage = GetStage(currentCapacity);
            GameObject nextStage = GetStage(nextCapacity);
            if (nextStage == null) return false;

            SetColliderEnabled(false);
            nextStage.SetActive(true);
            SetStageVerticalScale(nextCapacity, .01f);
            AnchorStageBottom(nextStage);
            SetStageBrightness(nextStage, .55f);

            _stageSequence?.Kill(false);
            _stageSequence = DOTween.Sequence().SetTarget(this);
            _stageSequence.Append(DOTween.To(() => .01f, value =>
            {
                SetStageVerticalScale(nextCapacity, value);
                AnchorStageBottom(nextStage);
            }, 1f, extensionDuration).SetEase(Ease.OutCubic));
            _stageSequence.Join(DOTween.To(() => .55f,
                value => SetStageBrightness(nextStage, value), 1f, extensionDuration).SetEase(Ease.OutCubic));
            _stageSequence.Append(DOTween.To(() => 1f, value =>
            {
                SetStageVerticalScale(nextCapacity, value);
                AnchorStageBottom(nextStage);
            }, 1.025f, settleDuration * .5f).SetEase(Ease.OutBack));
            _stageSequence.Append(DOTween.To(() => 1.025f, value =>
            {
                SetStageVerticalScale(nextCapacity, value);
                AnchorStageBottom(nextStage);
            }, 1f, settleDuration * .5f).SetEase(Ease.OutBack));
            _stageSequence.OnComplete(() =>
            {
                if (currentStage != null) currentStage.SetActive(false);
                SetStageVerticalScale(nextCapacity, 1f);
                AnchorStageBottom(nextStage);
                ClearStageBrightness(nextStage);
                if (currentStage != null) ClearStageBrightness(currentStage);
                _currentCapacity = nextCapacity;
                SetColliderEnabled(true);
                OnCapacityChanged?.Invoke(_currentCapacity);
                _stageSequence = null;
            });
            return true;
        }

        private void ApplyImmediateStage(int capacity, bool stageOneLocked)
        {
            for (int i = 1; i <= BoltView.Capacity; i++)
            {
                GameObject stage = GetStage(i);
                if (stage == null) continue;
                stage.transform.localScale = GetRestScale(i);
                ClearStageBrightness(stage);
                stage.SetActive(i == capacity);
                if (i == capacity) AnchorStageBottom(stage);
            }

            GameObject activeStage = GetStage(capacity);
            if (stageOneLocked && activeStage != null) SetStageBrightness(activeStage, lockedBrightness);
            SetColliderEnabled(!stageOneLocked && capacity > 0);
        }

        private void DisableLegacyCylinder()
        {
            Transform legacyCover = transform.Find("ExpandableCover");
            if (legacyCover != null) legacyCover.gameObject.SetActive(false);
        }

        private GameObject GetStage(int capacity)
        {
            int index = capacity - 1;
            return _stages != null && index >= 0 && index < _stages.Length ? _stages[index] : null;
        }

        private Vector3 GetRestScale(int capacity)
        {
            int index = capacity - 1;
            return _stageRestScales != null && index >= 0 && index < _stageRestScales.Length
                ? _stageRestScales[index] : Vector3.one;
        }

        private void AlignStagesToSharedBase()
        {
            GameObject stageOne = GetStage(1);
            if (stageOne == null) return;

            foreach (GameObject stage in _stages)
                if (stage != null) stage.SetActive(true);

            if (!TryGetStageBounds(stageOne, out Bounds stageOneBounds)) return;
            _baseBottomWorldY = stageOneBounds.min.y;
            Vector3 baseCenter = stageOneBounds.center;

            foreach (GameObject stage in _stages)
            {
                if (stage == null || !TryGetStageBounds(stage, out Bounds bounds)) continue;
                stage.transform.position += new Vector3(
                    baseCenter.x - bounds.center.x,
                    _baseBottomWorldY - bounds.min.y,
                    baseCenter.z - bounds.center.z);
            }
        }

        private static int GetVerticalScaleAxis(Transform stageTransform)
        {
            Vector3 localUp = stageTransform.InverseTransformDirection(Vector3.up);
            float x = Mathf.Abs(localUp.x);
            float y = Mathf.Abs(localUp.y);
            float z = Mathf.Abs(localUp.z);
            return x >= y && x >= z ? 0 : y >= z ? 1 : 2;
        }

        private void SetStageVerticalScale(int capacity, float multiplier)
        {
            GameObject stage = GetStage(capacity);
            if (stage == null) return;

            int index = capacity - 1;
            Vector3 scale = GetRestScale(capacity);
            int verticalAxis = _verticalScaleAxes != null && index >= 0 && index < _verticalScaleAxes.Length
                ? _verticalScaleAxes[index] : 1;
            scale[verticalAxis] *= multiplier;
            stage.transform.localScale = scale;
        }

        private void AnchorStageBottom(GameObject stage)
        {
            if (stage == null || !TryGetStageBounds(stage, out Bounds bounds)) return;
            stage.transform.position += Vector3.up * (_baseBottomWorldY - bounds.min.y);
        }

        private static bool TryGetStageBounds(GameObject stage, out Bounds bounds)
        {
            bounds = default;
            if (stage == null) return false;

            Renderer[] renderers = stage.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else bounds.Encapsulate(renderer.bounds);
            }
            return hasBounds;
        }

        private void SetColliderEnabled(bool enabled)
        {
            if (_bolt != null && _bolt.Collider != null) _bolt.Collider.enabled = enabled;
        }

        private void SetStageBrightness(GameObject stage, float multiplier)
        {
            if (stage == null) return;
            if (_propertyBlock == null) _propertyBlock = new MaterialPropertyBlock();
            foreach (Renderer renderer in stage.GetComponentsInChildren<Renderer>(true))
            {
                Color baseColor = renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_BaseColor")
                    ? renderer.sharedMaterial.GetColor("_BaseColor")
                    : renderer.sharedMaterial != null ? renderer.sharedMaterial.color : Color.white;
                Color shaded = baseColor * multiplier;
                shaded.a = Mathf.Lerp(.55f, 1f, multiplier);
                _propertyBlock.SetColor("_BaseColor", shaded);
                _propertyBlock.SetColor("_Color", shaded);
                _propertyBlock.SetColor("_EmissionColor", shaded * Mathf.Max(0f, multiplier - .85f));
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        private static void ClearStageBrightness(GameObject stage)
        {
            if (stage == null) return;
            foreach (Renderer renderer in stage.GetComponentsInChildren<Renderer>(true))
                renderer.SetPropertyBlock(null);
        }

        private void OnDestroy()
        {
            _stageSequence?.Kill(false);
        }
    }
}
