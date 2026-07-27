using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NutBoltSort
{
    /// <summary>Owns the single DOTween animation flow for nut selection and moves.</summary>
    public class GameManager : MonoBehaviour
    {
        [Header("Manager References")]
        [SerializeField] private LevelManager levelManager;
        [SerializeField] private UIManager uiManager;

        [Header("Nut Motion")]
        [SerializeField, Min(0f)] private float selectionLiftHeight = .72f;
        [SerializeField, Min(.01f)] private float selectionDuration = .22f;
        [SerializeField, Min(0f)] private float hoverAmount = .035f;
        [SerializeField, Min(.01f)] private float hoverHalfCycleDuration = .32f;
        [SerializeField, Min(0f)] private float moveArcHeight = .58f;
        [SerializeField, Min(.01f)] private float minimumFlightDuration = .28f;
        [SerializeField, Min(.01f)] private float flightSecondsPerWorldUnit = .12f;
        [SerializeField, Min(.01f)] private float landingDuration = .16f;
        [SerializeField, Range(.05f, .08f)] private float queueMinimumGap = .06f;
        [SerializeField, Min(0f)] private float threadedDegreesPerSecond = 260f;
        [SerializeField, Range(1f, 1.2f)] private float flightScale = 1.07f;

        [Header("Bolt Feedback (scale only)")]
        [SerializeField, Range(1f, 1.15f)] private float boltSelectionScale = 1.045f;
        [SerializeField, Min(.01f)] private float boltSelectionPulseDuration = .12f;
        [SerializeField, Range(.9f, 1f)] private float boltAttachYScale = .965f;
        [SerializeField, Min(.01f)] private float boltAttachPulseDuration = .11f;
        [SerializeField, Range(.9f, 1.1f)] private float invalidScalePulse = .965f;
        [SerializeField, Min(.01f)] private float invalidPulseDuration = .16f;
        [SerializeField, Range(1f, 1.1f)] private float completionScale = 1.03f;

        private BoltView selectedBolt;
        private List<NutView> selectedNuts = new List<NutView>(); // Logical order: bottom to top.
        private Coroutine selectionRoutine;
        private Coroutine moveRoutine;
        private Sequence gameplaySequence;
        private readonly List<Sequence> hoverSequences = new List<Sequence>();
        private bool inputLocked;
        private bool won;
        private int currentLevelIndex;

        public bool IsWon => won;
        public int CurrentLevelIndex => currentLevelIndex;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;
            if (levelManager == null) levelManager = GetComponent<LevelManager>() ?? FindObjectOfType<LevelManager>() ?? gameObject.AddComponent<LevelManager>();
            if (uiManager == null) uiManager = GetComponent<UIManager>() ?? FindObjectOfType<UIManager>() ?? gameObject.AddComponent<UIManager>();
        }

        private void Start() => LoadCurrentLevel();

        public void LoadCurrentLevel()
        {
            StopAllCoroutines();
            KillGameplayTweens();
            inputLocked = true;
            selectedBolt = null;
            selectedNuts.Clear();
            selectionRoutine = null;
            moveRoutine = null;
            won = false;
            if (levelManager != null && levelManager.BuildLevel(currentLevelIndex, out _)) CheckWin();
            if (uiManager != null) uiManager.UpdateLevelDisplay();
            inputLocked = false;
        }

        public void RestartLevel() => LoadCurrentLevel();
        public void LoadNextLevel()
        {
            if (levelManager != null && levelManager.TotalLevels > 0) currentLevelIndex = (currentLevelIndex + 1) % levelManager.TotalLevels;
            LoadCurrentLevel();
        }

        private void Update()
        {
            if (inputLocked || won || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) || (uiManager != null && uiManager.IsUIOpen)) return;
            if (!Input.GetMouseButtonDown(0)) return;
            Ray ray = Camera.main != null ? Camera.main.ScreenPointToRay(Input.mousePosition) : default;
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                BoltView bolt = hit.collider.GetComponent<BoltView>() ?? hit.collider.GetComponentInParent<BoltView>();
                if (bolt != null) TapBolt(bolt);
            }
        }

        public void TapBolt(BoltView tapped)
        {
            if (won || inputLocked || tapped == null) return;
            if (selectedBolt == null)
            {
                if (!tapped.IsLocked && tapped.Nuts.Count > 0)
                {
                    PulseBoltSelection(tapped);
                    BeginSelection(tapped);
                }
                return;
            }
            if (selectedBolt == tapped) { PulseBoltSelection(tapped); BeginSelectionCancel(); return; }
            if (CanMove(selectedBolt, tapped, selectedNuts)) { PulseBoltSelection(tapped); BeginMove(tapped); }
            else StartCoroutine(PulseInvalidDestination(tapped));
        }

        private void BeginSelection(BoltView source)
        {
            selectedBolt = source;
            selectedNuts = TopMatchingGroup(source);
            if (selectedNuts.Count == 0) { selectedBolt = null; return; }
            source.SetSelectionEffect(true);
            inputLocked = true;
            selectionRoutine = StartCoroutine(LiftSelectedNuts());
        }

        private void BeginSelectionCancel()
        {
            if (selectionRoutine != null) StopCoroutine(selectionRoutine);
            StopHover();
            inputLocked = true;
            selectionRoutine = StartCoroutine(ReturnSelectedNuts());
        }

        private void BeginMove(BoltView destination)
        {
            if (selectionRoutine != null) StopCoroutine(selectionRoutine);
            StopHover();
            inputLocked = true;
            moveRoutine = StartCoroutine(MoveSelectedNuts(destination));
        }

        private List<NutView> TopMatchingGroup(BoltView bolt)
        {
            var group = new List<NutView>();
            if (bolt == null || bolt.Nuts.Count == 0) return group;
            NutColor color = bolt.Nuts[bolt.Nuts.Count - 1].Color;
            for (int i = bolt.Nuts.Count - 1; i >= 0 && bolt.Nuts[i].Color == color; i--) group.Insert(0, bolt.Nuts[i]);
            return group;
        }

        private bool CanMove(BoltView from, BoltView to, List<NutView> moving) => from != null && to != null && moving != null && moving.Count > 0 && !to.IsLocked && to.Nuts.Count + moving.Count <= BoltView.Capacity && (to.Nuts.Count == 0 || to.Nuts[to.Nuts.Count - 1].Color == moving[moving.Count - 1].Color);

        private IEnumerator LiftSelectedNuts()
        {
            KillGameplayTweens();
            gameplaySequence = DOTween.Sequence().SetTarget(this);
            float clearanceGap = Mathf.Max(queueMinimumGap, selectionDuration * .58f);
            int queueOrder = 0;
            // Top first prevents a lower nut clipping through the stack above it.
            for (int i = selectedNuts.Count - 1; i >= 0; i--, queueOrder++)
            {
                NutView nut = selectedNuts[i];
                int stackIndex = selectedBolt.Nuts.IndexOf(nut);
                RestoreNutToStack(selectedBolt, nut, stackIndex);
                float delay = queueOrder * clearanceGap;
                Vector3 liftedPosition = nut.transform.position + Vector3.up * selectionLiftHeight;
                Sequence nutSequence = DOTween.Sequence().SetTarget(nut.transform);
                nutSequence.Join(nut.transform.DOMove(liftedPosition, selectionDuration).SetEase(Ease.OutCubic));
                nutSequence.Join(nut.transform.DORotate(Vector3.up * (threadedDegreesPerSecond * selectionDuration), selectionDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));
                gameplaySequence.Insert(delay, nutSequence);
            }
            yield return gameplaySequence.WaitForCompletion();
            gameplaySequence = null;
            StartHover();
            selectionRoutine = null;
            inputLocked = false;
        }

        private void StartHover()
        {
            StopHover();
            foreach (NutView nut in selectedNuts)
            {
                if (nut == null) continue;
                Sequence hover = DOTween.Sequence().SetTarget(nut.transform);
                hover.Append(nut.transform.DOMoveY(nut.transform.position.y + hoverAmount, hoverHalfCycleDuration).SetEase(Ease.InOutSine));
                hover.Append(nut.transform.DOMoveY(nut.transform.position.y, hoverHalfCycleDuration).SetEase(Ease.InOutSine));
                hover.SetLoops(-1);
                hoverSequences.Add(hover);
            }
        }

        private void StopHover()
        {
            foreach (Sequence hover in hoverSequences) if (hover != null && hover.IsActive()) hover.Kill(false);
            hoverSequences.Clear();
        }

        private IEnumerator ReturnSelectedNuts()
        {
            KillGameplayTweens();
            gameplaySequence = DOTween.Sequence().SetTarget(this);
            BoltView source = selectedBolt;
            foreach (NutView nut in selectedNuts)
            {
                int index = source.Nuts.IndexOf(nut);
                KillNutTween(nut);
                Sequence nutSequence = DOTween.Sequence().SetTarget(nut.transform);
                nutSequence.Join(nut.transform.DOMove(source.NutContainer.TransformPoint(source.GetStackPosition(index)), selectionDuration).SetEase(Ease.InOutCubic));
                nutSequence.Join(nut.transform.DORotate(Vector3.down * (threadedDegreesPerSecond * selectionDuration), selectionDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));
                gameplaySequence.Join(nutSequence);
            }
            yield return gameplaySequence.WaitForCompletion();
            RestoreNutsToStack(source, selectedNuts, source.Nuts.Count - selectedNuts.Count);
            source.SetSelectionEffect(false);
            ClearSelection();
            gameplaySequence = null;
            inputLocked = false;
        }

        private IEnumerator MoveSelectedNuts(BoltView destination)
        {
            KillGameplayTweens();
            BoltView source = selectedBolt;
            List<NutView> moving = new List<NutView>(selectedNuts);
            int destinationStartIndex = destination.Nuts.Count;
            source.SetSelectionEffect(false);
            gameplaySequence = DOTween.Sequence().SetTarget(this);

            int queueOrder = 0;
            // Queue order is top-to-bottom: it is the order visibly lifted and attached.
            for (int i = moving.Count - 1; i >= 0; i--, queueOrder++)
            {
                NutView nut = moving[i];
                Transform tr = nut.transform;
                KillNutTween(nut);
                int destinationIndex = destinationStartIndex + queueOrder;
                Vector3 travelEnd = destination.NutContainer.TransformPoint(destination.GetStackPosition(destinationIndex)) + Vector3.up * selectionLiftHeight;
                Vector3 start = tr.position;
                float flightDuration = Mathf.Max(minimumFlightDuration, Vector3.Distance(start, travelEnd) * flightSecondsPerWorldUnit);
                float delay = queueOrder * Mathf.Max(queueMinimumGap, selectionDuration * .32f);
                float rotationDegrees = threadedDegreesPerSecond * flightDuration;
                Vector3 arcMidpoint = Vector3.Lerp(start, travelEnd, .5f) + Vector3.up * moveArcHeight;
                Sequence nutSequence = DOTween.Sequence().SetTarget(tr);
                nutSequence.Append(tr.DOPath(new[] { arcMidpoint, travelEnd }, flightDuration, PathType.CatmullRom, PathMode.Full3D).SetEase(Ease.InOutCubic));
                nutSequence.Join(tr.DORotate(Vector3.up * rotationDegrees, flightDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));
                nutSequence.Insert(0f, tr.DOScale(nut.RestingLocalScale * flightScale, flightDuration * .5f).SetEase(Ease.OutQuad));
                nutSequence.Insert(flightDuration * .5f, tr.DOScale(nut.RestingLocalScale, flightDuration * .5f).SetEase(Ease.InOutSine));
                nutSequence.AppendCallback(() => tr.SetParent(destination.NutContainer, true));
                nutSequence.Append(tr.DOMove(destination.NutContainer.TransformPoint(destination.GetStackPosition(destinationIndex)), landingDuration).SetEase(Ease.InOutCubic));
                nutSequence.Join(tr.DORotate(Vector3.down * (threadedDegreesPerSecond * landingDuration), landingDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));
                nutSequence.AppendCallback(() =>
                {
                    SnapNutToStack(destination, nut, destinationIndex);
                    PulseBoltAttach(destination);
                });
                nutSequence.Append(tr.DOScale(nut.RestingLocalScale * 1.045f, .055f).SetEase(Ease.OutQuad));
                nutSequence.Append(tr.DOScale(nut.RestingLocalScale, .085f).SetEase(Ease.InOutSine));
                gameplaySequence.Insert(delay, nutSequence);
            }
            yield return gameplaySequence.WaitForCompletion();

            source.Nuts.RemoveRange(source.Nuts.Count - moving.Count, moving.Count);
            for (int queueOrderIndex = 0; queueOrderIndex < moving.Count; queueOrderIndex++) destination.Nuts.Add(moving[moving.Count - 1 - queueOrderIndex]);
            // Reassert exact stack references after the soft landing bounce.
            for (int i = 0; i < moving.Count; i++) RestoreNutToStack(destination, moving[moving.Count - 1 - i], destinationStartIndex + i);

            bool sourceCompleted = LockCompletedBolt(source);
            bool destinationCompleted = LockCompletedBolt(destination);
            CheckWin();
            if (sourceCompleted) yield return PlayCompletionFeedback(source);
            if (destinationCompleted && destination != source) yield return PlayCompletionFeedback(destination);
            ClearSelection();
            gameplaySequence = null;
            moveRoutine = null;
            if (won) uiManager?.ShowWinPopup(); else inputLocked = false;
        }

        private IEnumerator PulseInvalidDestination(BoltView bolt)
        {
            inputLocked = true;
            PulseBoltScale(bolt, new Vector3(1.02f, invalidScalePulse, 1.02f), invalidPulseDuration);
            yield return new WaitForSeconds(invalidPulseDuration);
            inputLocked = false;
        }

        private void PulseBoltSelection(BoltView bolt) => PulseBoltScale(bolt, Vector3.one * boltSelectionScale, boltSelectionPulseDuration);
        private void PulseBoltAttach(BoltView bolt) => PulseBoltScale(bolt, new Vector3(1.012f, boltAttachYScale, 1.012f), boltAttachPulseDuration);

        private void PulseBoltScale(BoltView bolt, Vector3 multiplier, float duration)
        {
            if (bolt == null) return;
            Transform tr = bolt.transform;
            DOTween.Kill(tr, false);
            tr.localPosition = tr.localPosition; // Feedback never changes root position or rotation.
            tr.localRotation = tr.localRotation;
            tr.localScale = bolt.RestingLocalScale;
            Sequence pulse = DOTween.Sequence().SetTarget(tr);
            pulse.Append(tr.DOScale(Vector3.Scale(bolt.RestingLocalScale, multiplier), duration * .45f).SetEase(Ease.OutQuad));
            pulse.Append(tr.DOScale(bolt.RestingLocalScale, duration * .55f).SetEase(Ease.InOutSine));
        }

        private bool LockCompletedBolt(BoltView bolt)
        {
            if (bolt == null || bolt.IsLocked || !bolt.IsComplete()) return false;
            bolt.LockBoltSilently();
            bolt.SetCompletionEffect(true);
            return true;
        }

        private IEnumerator PlayCompletionFeedback(BoltView bolt)
        {
            PulseBoltScale(bolt, Vector3.one * completionScale, .16f);
            yield return new WaitForSeconds(.16f);
        }

        private void KillGameplayTweens()
        {
            if (gameplaySequence != null && gameplaySequence.IsActive()) gameplaySequence.Kill(false);
            gameplaySequence = null;
            StopHover();
        }

        private static void KillNutTween(NutView nut)
        {
            if (nut != null) DOTween.Kill(nut.transform, false);
        }

        private void RestoreNutsToStack(BoltView bolt, List<NutView> nuts, int startIndex)
        {
            for (int i = 0; i < nuts.Count; i++) RestoreNutToStack(bolt, nuts[i], startIndex + i);
        }

        private static void RestoreNutToStack(BoltView bolt, NutView nut, int index)
        {
            if (bolt == null || nut == null) return;
            KillNutTween(nut);
            SnapNutToStack(bolt, nut, index);
        }

        // Used from inside a nut's active sequence: do not kill that sequence here.
        private static void SnapNutToStack(BoltView bolt, NutView nut, int index)
        {
            if (bolt == null || nut == null) return;
            Transform tr = nut.transform;
            if (tr.parent != bolt.NutContainer) tr.SetParent(bolt.NutContainer, false);
            tr.localPosition = bolt.GetStackPosition(index);
            tr.localRotation = nut.RestingLocalRotation;
            tr.localScale = nut.RestingLocalScale;
        }

        private void ClearSelection()
        {
            StopHover();
            if (selectedBolt != null) selectedBolt.SetSelectionEffect(false);
            selectedBolt = null;
            selectedNuts.Clear();
        }

        private void CheckWin()
        {
            if (won || levelManager == null) return;
            won = levelManager.ActiveBolts.All(b => b.Nuts.Count == 0 || (b.Nuts.Count == BoltView.Capacity && b.IsComplete()));
        }

        // Retain the project's no-UI-manager fallback controls.
        private void OnGUI()
        {
            if (uiManager != null && uiManager.enabled) return;
            Matrix4x4 old = GUI.matrix;
            float scale = Screen.width / 1080f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            var style = new GUIStyle(GUI.skin.button) { fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            if (GUI.Button(new Rect(50, 50, 190, 68), "↻  RESTART", style) && !inputLocked) RestartLevel();
            if (GUI.Button(new Rect(840, 50, 190, 68), "LEVEL " + (currentLevelIndex + 1), style) && !inputLocked) LoadNextLevel();
            if (won)
            {
                var wonStyle = new GUIStyle(GUI.skin.label) { fontSize = 66, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                GUI.Label(new Rect(0, Screen.height / scale * .43f, 1080, 100), "LEVEL COMPLETE!", wonStyle);
            }
            GUI.matrix = old;
        }
    }
}
