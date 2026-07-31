using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NutBoltSort
{
    /// <summary>
    /// Owns the single DOTween animation flow for nut selection, sequential follower transfer,
    /// random entry animation, and per-bolt completion cap.
    ///
    /// New in this version:
    ///   - Routes level loading through StructuredLevelProvider (levels 1-5 fixed, 6+ procedural).
    ///   - Exposes UndoLastMove(), ExpandFirstAvailableBolt(), UnlockFirstLockedBolt().
    ///   - Records MoveRecords in UndoManager after every successful transfer.
    ///   - Fires TutorialController events at the correct lifecycle points.
    ///   - GetMoveCount respects ExpandableBoltController.CurrentCapacity.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        [Header("Manager References")]
        [SerializeField] private LevelManager            levelManager;
        [SerializeField] private UIManager               uiManager;
        [SerializeField] private StructuredLevelProvider levelProvider;
        [SerializeField] private UndoManager             undoManager;
        [SerializeField] private TutorialController      tutorialController;

        // Selection ───────────────────────────────────────────────────────────
        [Header("Selection")]
        [Tooltip("Duration of the top nut lifting to the hover point.")]
        [SerializeField, Min(.01f)] private float selectionLiftDuration   = .22f;

        [Tooltip("Degrees per second the nut rotates while unscrewing (lift) or rethreading (land).")]
        [SerializeField, Min(0f)]   private float selectionRotationSpeed  = 260f;

        [Header("Selection Switching")]
        [Tooltip("Return duration used only when switching from one selected bolt to another. Lower is faster.")]
        [SerializeField, Min(.01f)] private float selectionSwitchDuration = .18f;

        // Hover ───────────────────────────────────────────────────────────────
        [Header("Hover")]
        [SerializeField, Min(0f)]   private float hoverAmount            = .035f;
        [SerializeField, Min(.01f)] private float hoverHalfCycleDuration = .32f;

        // Followers ───────────────────────────────────────────────────────────
        [Header("Followers")]
        [Tooltip("Minimum clearance time before the next nut starts.")]
        [SerializeField, Range(.05f, .30f)] private float followerDelay  = .14f;

        // Travel ──────────────────────────────────────────────────────────────
        [Header("Travel")]
        [Tooltip("Duration of the vertical lift stage (followers only).")]
        [SerializeField, Min(.01f)] private float liftDuration           = .20f;

        [Tooltip("Minimum horizontal travel duration regardless of distance.")]
        [SerializeField, Min(.01f)] private float travelDurationMin      = .28f;

        [Tooltip("Horizontal travel time added per world-unit of distance.")]
        [SerializeField, Min(.01f)] private float travelDurationPerUnit  = .12f;

        [Tooltip("Degrees per second the nut gently rotates during travel.")]
        [SerializeField, Min(0f)]   private float travelRotationSpeed    = 200f;

        // Landing ─────────────────────────────────────────────────────────────
        [Header("Landing")]
        [SerializeField, Min(.01f)] private float landingDuration        = .16f;
        [SerializeField, Range(1f, 1.2f)] private float landingBounceScale = 1.045f;

        // Entry Animation ─────────────────────────────────────────────────────
        [Header("Entry Animation")]
        [Tooltip("Height above final slot each nut starts from.")]
        [SerializeField, Range(.3f, .7f)]   private float entryHeightOffset = .50f;

        [Tooltip("Minimum random Y-rotation added to each nut at entry start.")]
        [SerializeField, Range(90f, 360f)]  private float entryRotMin       = 90f;

        [Tooltip("Maximum random Y-rotation added to each nut at entry start.")]
        [SerializeField, Range(90f, 360f)]  private float entryRotMax       = 270f;

        [Tooltip("Bottom-to-top gap used by the ordered startup queue on each bolt.")]
        [SerializeField, Range(0f, .30f)]   private float entryMaxDelay     = .20f;

        [Tooltip("Duration of each nut's drop animation.")]
        [SerializeField, Range(.15f, .50f)] private float entryDuration     = .27f;

        // Completion Cap ──────────────────────────────────────────────────────
        [Header("Completion Cap")]
        [SerializeField, Min(0f)]          private float capPopHeight    = .18f;
        [SerializeField, Range(.5f, 1f)]   private float capStartScale   = .75f;
        [SerializeField, Min(.01f)]        private float capPopDuration  = .14f;
        [SerializeField, Min(.01f)]        private float capCloseDuration = .22f;
        [SerializeField, Range(0f, .15f)]  private float capBounceAmount = .06f;

        // Bolt Feedback ───────────────────────────────────────────────────────
        [Header("Bolt Feedback (scale only)")]
        [SerializeField, Range(1f, 1.15f)] private float boltSelectionScale        = 1.045f;
        [SerializeField, Min(.01f)]        private float boltSelectionPulseDuration = .12f;
        [SerializeField, Range(.9f, 1f)]   private float boltAttachYScale          = .965f;
        [SerializeField, Min(.01f)]        private float boltAttachPulseDuration   = .11f;
        [SerializeField, Range(.9f, 1.1f)] private float invalidScalePulse         = .965f;
        [SerializeField, Min(.01f)]        private float invalidPulseDuration       = .16f;

        // ─────────────────────────────────────────────────────────────────────
        // Runtime State
        // ─────────────────────────────────────────────────────────────────────

        private BoltView         selectedBolt;
        private List<NutView>    selectedNuts = new List<NutView>();
        private NutView          liftedNut;       // Only the topmost nut is visually hovering.
        private Coroutine        selectionRoutine;
        private Coroutine        moveRoutine;
        private Sequence         gameplaySequence;
        private readonly List<Sequence>              hoverSequences = new List<Sequence>();
        private readonly Dictionary<BoltView, Sequence> capSequences  = new Dictionary<BoltView, Sequence>();

        private bool inputLocked;
        private bool won;
        private int  currentLevelNumber = 1; // 1-based; persisted via PlayerPrefs

        private const string PREFS_LEVEL = "CurrentLevelNumber";

        // ─────────────────────────────────────────────────────────────────────
        // Public Properties
        // ─────────────────────────────────────────────────────────────────────

        public bool IsWon            => won;
        /// <summary>1-based current level number.</summary>
        public int  CurrentLevelNumber => currentLevelNumber;
        /// <summary>0-based index — kept for backward-compatible UIManager calls.</summary>
        public int  CurrentLevelIndex  => currentLevelNumber - 1;
        /// <summary>True when at least one move can be undone.</summary>
        public bool CanUndo => undoManager != null && undoManager.CanUndo && !inputLocked && !won;

        // ─────────────────────────────────────────────────────────────────────
        // Unity Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;

            if (levelManager       == null) levelManager       = GetComponent<LevelManager>()            ?? FindAnyObjectByType<LevelManager>()            ?? gameObject.AddComponent<LevelManager>();
            if (uiManager          == null) uiManager          = GetComponent<UIManager>()               ?? FindAnyObjectByType<UIManager>()               ?? gameObject.AddComponent<UIManager>();
            if (levelProvider      == null) levelProvider      = GetComponent<StructuredLevelProvider>() ?? FindAnyObjectByType<StructuredLevelProvider>()  ?? gameObject.AddComponent<StructuredLevelProvider>();
            if (undoManager        == null) undoManager        = GetComponent<UndoManager>()             ?? FindAnyObjectByType<UndoManager>()             ?? gameObject.AddComponent<UndoManager>();
            if (tutorialController == null) tutorialController = GetComponent<TutorialController>()      ?? FindAnyObjectByType<TutorialController>()      ?? gameObject.AddComponent<TutorialController>();

            // Restore persisted level number.
            currentLevelNumber = PlayerPrefs.GetInt(PREFS_LEVEL, 1);
            if (currentLevelNumber < 1) currentLevelNumber = 1;
        }

        private void Start() => LoadCurrentLevel();

        private void Update()
        {
            if (inputLocked || won ||
                (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) ||
                (uiManager != null && uiManager.IsUIOpen)) return;
            if (!Input.GetMouseButtonDown(0)) return;

            Ray ray = Camera.main != null ? Camera.main.ScreenPointToRay(Input.mousePosition) : default;
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                BoltView bolt = hit.collider.GetComponent<BoltView>() ?? hit.collider.GetComponentInParent<BoltView>();
                if (bolt != null) TapBolt(bolt);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Level Loading
        // ─────────────────────────────────────────────────────────────────────

        public void LoadCurrentLevel()
        {
            StopAllCoroutines();
            KillAllTweens();
            undoManager?.Clear();

            inputLocked      = true;
            selectedBolt     = null;
            selectedNuts.Clear();
            liftedNut        = null;
            selectionRoutine = null;
            moveRoutine      = null;
            won              = false;

            bool built;
            if (levelProvider != null)
            {
                LevelDataSO data = levelProvider.GetLevelData(currentLevelNumber);
                built = data != null && levelManager != null && levelManager.BuildLevel(data, out _);
            }
            else
            {
                built = levelManager != null && levelManager.BuildLevel(currentLevelNumber - 1, out _);
            }

            if (uiManager != null)
            {
                uiManager.UpdateLevelDisplay();
                uiManager.RefreshActionButtonStates();
            }
            if (!built) { inputLocked = false; return; }

            // Silently lock any bolt that starts already complete.
            foreach (BoltView bolt in levelManager.ActiveBolts)
            {
                if (bolt != null && !bolt.IsLocked && bolt.IsComplete())
                    bolt.LockBoltSilently();
            }
            CheckWin();

            // Notify tutorial controller.
            tutorialController?.OnLevelLoaded(currentLevelNumber, this, levelManager);

            StartCoroutine(PlayEntryAnimation());
        }

        public void RestartLevel()
        {
            // Use the stored snapshot so generation seed isn't reused unpredictably.
            if (levelProvider?.CurrentSnapshot != null)
            {
                StopAllCoroutines();
                KillAllTweens();
                undoManager?.Clear();

                inputLocked      = true;
                selectedBolt     = null;
                selectedNuts.Clear();
                liftedNut        = null;
                selectionRoutine = null;
                moveRoutine      = null;
                won              = false;

                levelManager?.BuildLevel(levelProvider.CurrentSnapshot, out _);
                if (uiManager != null)
                {
                    uiManager.UpdateLevelDisplay();
                    uiManager.RefreshActionButtonStates();
                }

                tutorialController?.OnLevelLoaded(currentLevelNumber, this, levelManager);
                StartCoroutine(PlayEntryAnimation());
            }
            else
            {
                LoadCurrentLevel();
            }
        }

        public void LoadNextLevel()
        {
            currentLevelNumber++;
            PlayerPrefs.SetInt(PREFS_LEVEL, currentLevelNumber);
            PlayerPrefs.Save();
            LoadCurrentLevel();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Special Bolt Actions
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Increases the capacity of the first non-maxed expandable bolt.
        /// Called by UIManager when the player presses the Expand button.
        /// </summary>
        public void ExpandFirstAvailableBolt()
        {
            if (inputLocked || won || levelManager == null) return;
            foreach (BoltView bolt in levelManager.ActiveBolts)
            {
                if (bolt == null) continue;
                var exp = bolt.GetComponent<ExpandableBoltController>();
                if (exp != null && !exp.IsAtMax)
                {
                    bool increased = exp.IncreaseCapacity();
                    if (increased)
                    {
                        tutorialController?.OnExpandableBoltUsed();
                        uiManager?.RefreshActionButtonStates();
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Unlocks the first locked bolt.
        /// Called by UIManager when the player presses the Unlock button.
        /// </summary>
        public void UnlockFirstLockedBolt()
        {
            if (inputLocked || won || levelManager == null) return;
            foreach (BoltView bolt in levelManager.ActiveBolts)
            {
                if (bolt == null) continue;
                var lck = bolt.GetComponent<LockedBoltController>();
                if (lck != null && !lck.IsUnlocked)
                {
                    bool unlocked = lck.TryUnlock();
                    if (unlocked)
                    {
                        tutorialController?.OnLockedBoltUnlocked();
                        return;
                    }
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Undo
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reverses the most recent completed transfer.
        /// Called by UIManager when the player presses the Undo button.
        /// </summary>
        public void UndoLastMove()
        {
            if (!CanUndo || levelManager == null) return;
            if (undoManager == null || !undoManager.TryPopLastMove(out MoveRecord record)) return;

            BoltView srcBolt  = levelManager.ActiveBolts.ElementAtOrDefault(record.sourceBoltIndex);
            BoltView dstBolt  = levelManager.ActiveBolts.ElementAtOrDefault(record.destinationBoltIndex);
            if (srcBolt == null || dstBolt == null) return;

            // Undo transfers nuts FROM destination BACK TO source.
            moveRoutine = StartCoroutine(ExecuteUndoTransfer(dstBolt, srcBolt, record));
        }

        private IEnumerator ExecuteUndoTransfer(BoltView undoSrc, BoltView undoDst, MoveRecord record)
        {
            KillGameplayTweens();
            inputLocked = true;

            // Unlock destination bolt if the original move completed it (remove the cap).
            if (record.destinationWasCompletedAfter && undoSrc.IsLocked)
            {
                if (undoSrc.CompletionCapTransform != null)
                {
                    var capSeq = DOTween.Sequence();
                    capSeq.Append(undoSrc.CompletionCapTransform.DOScale(Vector3.zero, 0.15f).SetEase(Ease.InBack));
                    capSeq.OnComplete(() => undoSrc.DeactivateCap());
                    yield return capSeq.WaitForCompletion();
                }
                // Re-open the bolt for interaction.
                undoSrc.UnlockSilently();
            }

            int moveCount = Mathf.Min(record.movedNutCount, undoSrc.Nuts.Count);
            if (moveCount <= 0)
            {
                Debug.LogWarning("[GameManager] UndoTransfer: nothing to move.");
                inputLocked = false;
                uiManager?.RefreshActionButtonStates();
                yield break;
            }

            // Grab the top N nuts from undoSrc.
            List<NutView> toMove = undoSrc.Nuts.GetRange(undoSrc.Nuts.Count - moveCount, moveCount);
            int destStartIdx     = undoDst.Nuts.Count;
            float followerClearanceDelay = Mathf.Max(followerDelay, liftDuration * .65f);
            float hoverWorldY    = undoSrc.GetHoverWorldPosition().y;

            gameplaySequence = DOTween.Sequence().SetTarget(this);

            for (int q = 0; q < toMove.Count; q++)
            {
                int     srcIdx   = toMove.Count - 1 - q;
                NutView nut      = toMove[srcIdx];
                Transform tr     = nut.transform;
                KillNutTween(nut);

                int     destIdx   = destStartIdx + q;
                Vector3 destWorld = undoDst.NutContainer.TransformPoint(undoDst.GetStackPosition(destIdx));
                float   seqStart  = q * followerClearanceDelay;

                Sequence nutSeq = DOTween.Sequence().SetTarget(tr);

                // Phase 1: lift (all nuts, as they all start on the stack)
                Vector3 liftTarget = new Vector3(tr.position.x, hoverWorldY, tr.position.z);
                Sequence liftSeq = DOTween.Sequence();
                liftSeq.Join(tr.DOMove(liftTarget, liftDuration).SetEase(Ease.OutCubic));
                liftSeq.Join(tr.DORotate(Vector3.up * (selectionRotationSpeed * liftDuration),
                    liftDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));
                nutSeq.Append(liftSeq);

                // Phase 2: horizontal travel
                Vector3 horizontalDest = new Vector3(destWorld.x, hoverWorldY, destWorld.z);
                float horizDist = Vector3.Distance(
                    new Vector3(tr.position.x, 0f, tr.position.z),
                    new Vector3(horizontalDest.x, 0f, horizontalDest.z));
                float horizDuration = Mathf.Max(travelDurationMin, horizDist * travelDurationPerUnit);
                Sequence travelSeq = DOTween.Sequence();
                travelSeq.Append(tr.DOMove(horizontalDest, horizDuration).SetEase(Ease.InOutSine));
                travelSeq.Join(tr.DORotate(Vector3.up * (travelRotationSpeed * horizDuration),
                    horizDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));
                nutSeq.Append(travelSeq);

                // Phase 3: reparent + drop
                NutView  capturedNut  = nut;
                int      capturedDIdx = destIdx;
                BoltView capturedDst  = undoDst;
                nutSeq.AppendCallback(() => tr.SetParent(capturedDst.NutContainer, worldPositionStays: true));
                Sequence landSeq = DOTween.Sequence();
                landSeq.Append(tr.DOMove(destWorld, landingDuration).SetEase(Ease.InCubic));
                landSeq.Join(tr.DORotate(Vector3.down * (selectionRotationSpeed * landingDuration),
                    landingDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));
                nutSeq.Append(landSeq);
                nutSeq.AppendCallback(() =>
                {
                    SnapNutToStack(capturedDst, capturedNut, capturedDIdx);
                    PulseBoltAttach(capturedDst);
                });
                nutSeq.Append(tr.DOScale(nut.RestingLocalScale * landingBounceScale, .055f).SetEase(Ease.OutQuad));
                nutSeq.Append(tr.DOScale(nut.RestingLocalScale, .085f).SetEase(Ease.InOutSine));

                gameplaySequence.Insert(seqStart, nutSeq);
            }

            yield return gameplaySequence.WaitForCompletion();

            // Update logical state.
            undoSrc.Nuts.RemoveRange(undoSrc.Nuts.Count - moveCount, moveCount);
            for (int q = 0; q < toMove.Count; q++)
                undoDst.Nuts.Add(toMove[toMove.Count - 1 - q]);

            // Reassert exact transforms after bounce.
            for (int q = 0; q < toMove.Count; q++)
                RestoreNutToStack(undoDst, toMove[toMove.Count - 1 - q], destStartIdx + q);

            // Re-evaluate completion.
            won = false; // undo cannot complete a level
            TryLockCompleted(undoSrc);
            TryLockCompleted(undoDst);
            CheckWin();

            gameplaySequence = null;
            moveRoutine      = null;
            inputLocked      = false;
            uiManager?.RefreshActionButtonStates();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Tap Handling
        // ─────────────────────────────────────────────────────────────────────

        public void TapBolt(BoltView tapped)
        {
            if (won || inputLocked || tapped == null) return;

            // Tutorial filter: only the expected bolt accepts a tap during tutorial.
            if (tutorialController != null && !tutorialController.AllowTap(tapped)) return;

            if (selectedBolt == null)
            {
                if (!tapped.IsLocked && tapped.Nuts.Count > 0)
                {
                    PulseBoltSelection(tapped);
                    BeginSelection(tapped);
                }
                return;
            }

            if (selectedBolt == tapped)
            {
                PulseBoltSelection(tapped);
                BeginSelectionCancel();
                return;
            }

            if (CanMove(selectedBolt, tapped, selectedNuts))
            {
                PulseBoltSelection(tapped);
                BeginMove(tapped);
            }
            else
            {
                BeginSelectionSwitch(tapped);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Selection — only the top nut lifts
        // ─────────────────────────────────────────────────────────────────────

        private void BeginSelection(BoltView source)
        {
            selectedBolt = source;
            selectedNuts = TopMatchingGroup(source);
            if (selectedNuts.Count == 0) { selectedBolt = null; return; }

            liftedNut = selectedNuts[selectedNuts.Count - 1];
            source.SetSelectionEffect(true);
            inputLocked = true;
            selectionRoutine = StartCoroutine(LiftTopNut());
        }

        private void BeginSelectionCancel()
        {
            if (selectionRoutine != null) StopCoroutine(selectionRoutine);
            StopHover();
            inputLocked = true;
            selectionRoutine = StartCoroutine(ReturnTopNut());
        }

        private void BeginSelectionSwitch(BoltView nextBolt)
        {
            if (selectionRoutine != null) StopCoroutine(selectionRoutine);
            StopHover();
            inputLocked = true;
            selectionRoutine = StartCoroutine(ReturnThenSelect(nextBolt));
        }

        private void BeginMove(BoltView destination)
        {
            if (selectionRoutine != null) StopCoroutine(selectionRoutine);
            StopHover();
            inputLocked = true;
            // Tutorial observes the transfer; it never starts or owns movement.
            tutorialController?.OnTransferStarted();
            moveRoutine = StartCoroutine(MoveSelectedNuts(destination));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Lift — single top nut to fixed SelectionHoverPoint
        // ─────────────────────────────────────────────────────────────────────

        private IEnumerator LiftTopNut()
        {
            KillGameplayTweens();
            if (liftedNut == null) { inputLocked = false; yield break; }

            int stackIdx = selectedBolt.Nuts.IndexOf(liftedNut);
            RestoreNutToStack(selectedBolt, liftedNut, stackIdx);

            Vector3 hoverTarget = selectedBolt.GetHoverWorldPosition();

            Sequence liftSeq = DOTween.Sequence().SetTarget(liftedNut.transform);
            liftSeq.Join(liftedNut.transform.DOMove(hoverTarget, selectionLiftDuration).SetEase(Ease.OutCubic));
            liftSeq.Join(liftedNut.transform.DORotate(
                Vector3.up * (selectionRotationSpeed * selectionLiftDuration),
                selectionLiftDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));

            gameplaySequence = DOTween.Sequence().SetTarget(this).Append(liftSeq);
            yield return gameplaySequence.WaitForCompletion();

            gameplaySequence = null;
            selectionRoutine = null;

            // Notify tutorial (WaitingForSource → WaitingForDest transition).
            tutorialController?.OnBoltSelected(selectedBolt);

            StartHover();
            inputLocked = false;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Cancel — only the single hovering nut returns
        // ─────────────────────────────────────────────────────────────────────

        private IEnumerator ReturnTopNut()
        {
            yield return ReturnHoveredNutToStack(selectionLiftDuration);
            ClearSelection();
            gameplaySequence = null;
            inputLocked = false;
        }

        private IEnumerator ReturnThenSelect(BoltView nextBolt)
        {
            yield return ReturnHoveredNutToStack(selectionSwitchDuration);
            ClearSelection();
            gameplaySequence = null;
            selectionRoutine = null;

            if (CanSelect(nextBolt))
            {
                PulseBoltSelection(nextBolt);
                BeginSelection(nextBolt);
            }
            else
            {
                inputLocked = false;
            }
        }

        private IEnumerator ReturnHoveredNutToStack(float returnDuration)
        {
            KillGameplayTweens();
            if (liftedNut == null || selectedBolt == null) yield break;

            BoltView source        = selectedBolt;
            NutView  returningNut  = liftedNut;
            int      stackIdx      = source.Nuts.IndexOf(returningNut);
            if (stackIdx < 0) yield break;

            Vector3 returnTarget = source.NutContainer.TransformPoint(source.GetStackPosition(stackIdx));
            KillNutTween(returningNut);
            Sequence returnSeq = DOTween.Sequence().SetTarget(returningNut.transform);
            returnSeq.Join(returningNut.transform.DOMove(returnTarget, returnDuration).SetEase(Ease.InOutCubic));
            returnSeq.Join(returningNut.transform.DORotate(
                Vector3.down * (selectionRotationSpeed * returnDuration),
                returnDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));

            gameplaySequence = DOTween.Sequence().SetTarget(this).Append(returnSeq);
            yield return gameplaySequence.WaitForCompletion();
            SnapNutToStack(source, returningNut, stackIdx);
            source.SetSelectionEffect(false);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Move — leader departs first; followers lift and follow sequentially
        //         Uses Lift → Horizontal → Drop path
        // ─────────────────────────────────────────────────────────────────────

        private IEnumerator MoveSelectedNuts(BoltView destination)
        {
            KillGameplayTweens();
            BoltView      source       = selectedBolt;
            int           moveCount    = GetMoveCount(destination, selectedNuts);
            List<NutView> moving       = selectedNuts.Skip(selectedNuts.Count - moveCount).ToList();
            int           destStartIdx = destination.Nuts.Count;
            source.SetSelectionEffect(false);

            gameplaySequence = DOTween.Sequence().SetTarget(this);
            float followerClearanceDelay = Mathf.Max(followerDelay, selectionLiftDuration * .65f);

            for (int q = 0; q < moving.Count; q++)
            {
                int      srcIdx = moving.Count - 1 - q;
                NutView  nut    = moving[srcIdx];
                Transform tr    = nut.transform;
                KillNutTween(nut);

                int     destIdx   = destStartIdx + q;
                Vector3 destWorld = destination.NutContainer.TransformPoint(destination.GetStackPosition(destIdx));
                float   seqStart  = q * followerClearanceDelay;

                Sequence nutSeq = DOTween.Sequence().SetTarget(tr);

                // ── Shared hover height ──────────────────────────────────────
                float hoverWorldY = source.GetHoverWorldPosition().y;

                // ── Phase 1: Vertical Lift (followers only) ──────────────────
                if (q == 0)
                {
                    // Leader: already at hover height — snap Y cleanly.
                    Vector3 snapPos = tr.position;
                    snapPos.y       = hoverWorldY;
                    tr.position     = snapPos;
                }
                else
                {
                    Vector3 liftTarget = new Vector3(tr.position.x, hoverWorldY, tr.position.z);
                    Sequence liftSeq = DOTween.Sequence();
                    liftSeq.Join(tr.DOMove(liftTarget, liftDuration).SetEase(Ease.OutCubic));
                    liftSeq.Join(tr.DORotate(
                        Vector3.up * (selectionRotationSpeed * liftDuration),
                        liftDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));
                    nutSeq.Append(liftSeq);
                }

                // ── Phase 2: Horizontal Travel ───────────────────────────────
                Vector3 horizontalDest = new Vector3(destWorld.x, hoverWorldY, destWorld.z);
                float   horizDist      = Vector3.Distance(
                    new Vector3(tr.position.x, 0f, tr.position.z),
                    new Vector3(horizontalDest.x, 0f, horizontalDest.z));
                float   horizDuration  = Mathf.Max(travelDurationMin, horizDist * travelDurationPerUnit);
                float   rotDeg         = travelRotationSpeed * horizDuration;

                Sequence travelSeq = DOTween.Sequence();
                travelSeq.Append(tr.DOMove(horizontalDest, horizDuration).SetEase(Ease.InOutSine));
                travelSeq.Join(tr.DORotate(Vector3.up * rotDeg, horizDuration,
                    RotateMode.WorldAxisAdd).SetEase(Ease.Linear));
                nutSeq.Append(travelSeq);

                // ── Phase 3: reparent → Vertical Drop ───────────────────────
                NutView  capturedNut     = nut;
                int      capturedDestIdx = destIdx;
                BoltView capturedDest    = destination;

                nutSeq.AppendCallback(() => tr.SetParent(capturedDest.NutContainer, worldPositionStays: true));

                Sequence landSeq = DOTween.Sequence();
                landSeq.Append(tr.DOMove(destWorld, landingDuration).SetEase(Ease.InCubic));
                landSeq.Join(tr.DORotate(
                    Vector3.down * (selectionRotationSpeed * landingDuration),
                    landingDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));
                nutSeq.Append(landSeq);

                nutSeq.AppendCallback(() =>
                {
                    SnapNutToStack(capturedDest, capturedNut, capturedDestIdx);
                    PulseBoltAttach(capturedDest);
                });
                nutSeq.Append(tr.DOScale(nut.RestingLocalScale * landingBounceScale, .055f).SetEase(Ease.OutQuad));
                nutSeq.Append(tr.DOScale(nut.RestingLocalScale, .085f).SetEase(Ease.InOutSine));

                gameplaySequence.Insert(seqStart, nutSeq);
            }

            yield return gameplaySequence.WaitForCompletion();

            // ── Update logical state ─────────────────────────────────────────
            source.Nuts.RemoveRange(source.Nuts.Count - moving.Count, moving.Count);
            for (int q = 0; q < moving.Count; q++)
                destination.Nuts.Add(moving[moving.Count - 1 - q]);

            for (int q = 0; q < moving.Count; q++)
                RestoreNutToStack(destination, moving[moving.Count - 1 - q], destStartIdx + q);

            // ── Record move for Undo ─────────────────────────────────────────
            bool srcCompleted  = TryLockCompleted(source);
            bool destCompleted = TryLockCompleted(destination);

            if (undoManager != null && moving.Count > 0)
            {
                int srcBoltIdx  = levelManager != null ? levelManager.IndexOfBolt(source)      : -1;
                int dstBoltIdx  = levelManager != null ? levelManager.IndexOfBolt(destination) : -1;
                undoManager.RecordMove(new MoveRecord
                {
                    sourceBoltIndex          = srcBoltIdx,
                    destinationBoltIndex     = dstBoltIdx,
                    movedColor               = moving[0].Color,
                    movedNutCount            = moving.Count,
                    sourceWasCompletedAfter  = srcCompleted,
                    destinationWasCompletedAfter = destCompleted
                });
            }

            // Notify tutorial.
            int srcIdx2 = levelManager != null ? levelManager.IndexOfBolt(source)      : -1;
            int dstIdx2 = levelManager != null ? levelManager.IndexOfBolt(destination) : -1;
            tutorialController?.OnTransferCompleted(srcIdx2, dstIdx2, moving.Count);

            // ── Check completion ─────────────────────────────────────────────
            CheckWin();

            Coroutine srcCapRoutine  = srcCompleted                          ? StartCoroutine(PlayCapAnimation(source))      : null;
            Coroutine destCapRoutine = (destCompleted && destination != source) ? StartCoroutine(PlayCapAnimation(destination)) : null;
            if (srcCapRoutine  != null) yield return srcCapRoutine;
            if (destCapRoutine != null) yield return destCapRoutine;

            ClearSelection();
            gameplaySequence = null;
            moveRoutine      = null;

            // Refresh Undo button.
            uiManager?.RefreshActionButtonStates();

            if (won) uiManager?.ShowWinPopup();
            else     inputLocked = false;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Entry Animation
        // ─────────────────────────────────────────────────────────────────────

        private IEnumerator PlayEntryAnimation()
        {
            inputLocked = true;

            if (levelManager == null || levelManager.ActiveBolts.Count == 0)
            {
                ShowSilentCaps();
                tutorialController?.OnBoardReady(currentLevelNumber);
                inputLocked = false;
                yield break;
            }

            Sequence entrySeq = DOTween.Sequence();

            foreach (BoltView bolt in levelManager.ActiveBolts)
            {
                if (bolt == null) continue;
                for (int i = 0; i < bolt.Nuts.Count; i++)
                {
                    NutView nut = bolt.Nuts[i];
                    if (nut == null) continue;

                    Vector3 finalWorld  = bolt.NutContainer.TransformPoint(bolt.GetStackPosition(i));
                    Vector3 startWorld  = finalWorld + Vector3.up * entryHeightOffset;
                    float   randomYRot  = Random.Range(entryRotMin, entryRotMax);
                    float   entryClearanceDelay = Mathf.Max(entryMaxDelay, entryDuration * .65f);
                    float   delay = i * entryClearanceDelay;

                    nut.transform.position     = startWorld;
                    nut.transform.localRotation = Quaternion.Euler(
                        nut.RestingLocalRotation.eulerAngles.x,
                        nut.RestingLocalRotation.eulerAngles.y + randomYRot,
                        nut.RestingLocalRotation.eulerAngles.z);

                    BoltView capturedBolt = bolt;
                    NutView  capturedNut  = nut;
                    int      capturedIdx  = i;

                    Sequence nutEntry = DOTween.Sequence().SetTarget(nut.transform);
                    nutEntry.Append(nut.transform.DOMove(finalWorld, entryDuration).SetEase(Ease.OutCubic));
                    nutEntry.Join(nut.transform.DOLocalRotateQuaternion(
                        nut.RestingLocalRotation, entryDuration).SetEase(Ease.OutCubic));
                    nutEntry.OnComplete(() => SnapNutToStack(capturedBolt, capturedNut, capturedIdx));

                    entrySeq.Insert(delay, nutEntry);
                }
            }

            yield return entrySeq.WaitForCompletion();

            ShowSilentCaps();
            tutorialController?.OnBoardReady(currentLevelNumber);
            inputLocked = false;
        }

        private void ShowSilentCaps()
        {
            if (levelManager == null) return;
            foreach (BoltView bolt in levelManager.ActiveBolts)
            {
                if (bolt == null || !bolt.IsLocked) continue;
                Color capColor = GetTopNutColor(bolt);
                bolt.ActivateCap(capColor);
                if (bolt.CompletionCapTransform != null)
                {
                    bolt.CompletionCapTransform.position   = bolt.GetCapWorldPosition();
                    bolt.CompletionCapTransform.localScale = bolt.CompletionCapRestingLocalScale;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Completion Cap Animation
        // ─────────────────────────────────────────────────────────────────────

        private bool TryLockCompleted(BoltView bolt)
        {
            if (bolt == null || bolt.IsLocked || !bolt.IsComplete()) return false;
            bolt.LockBoltSilently();
            return true;
        }

        private IEnumerator PlayCapAnimation(BoltView bolt)
        {
            if (bolt == null || bolt.CompletionCapTransform == null) yield break;

            Color     capColor = GetTopNutColor(bolt);
            Transform capTr    = bolt.CompletionCapTransform;
            Vector3   capScale = bolt.CompletionCapRestingLocalScale;
            Vector3   restPos  = bolt.GetCapWorldPosition();
            Vector3   startPos = restPos + Vector3.up * capPopHeight;
            Vector3   popPos   = restPos + Vector3.up * (capPopHeight * .45f);

            if (capSequences.TryGetValue(bolt, out Sequence prev) && prev != null && prev.IsActive())
                prev.Kill(false);

            bolt.ActivateCap(capColor);
            capTr.position   = startPos;
            capTr.localScale = capScale * capStartScale;

            Sequence capSeq = DOTween.Sequence().SetTarget(capTr);
            capSeq.Append(capTr.DOScale(capScale * 1.08f, capPopDuration).SetEase(Ease.OutBack));
            capSeq.Join(capTr.DOMove(popPos, capPopDuration).SetEase(Ease.OutQuad));
            capSeq.Append(capTr.DOMove(restPos, capCloseDuration).SetEase(Ease.InCubic));
            capSeq.Append(capTr.DOScale(capScale * (1f + capBounceAmount), .06f).SetEase(Ease.OutQuad));
            capSeq.Append(capTr.DOScale(capScale * (1f - capBounceAmount * .5f), .07f).SetEase(Ease.InOutSine));
            capSeq.Append(capTr.DOScale(capScale, .07f).SetEase(Ease.InOutSine));

            capSequences[bolt] = capSeq;
            yield return capSeq.WaitForCompletion();
        }

        private Color GetTopNutColor(BoltView bolt)
        {
            if (bolt == null || bolt.Nuts.Count == 0) return Color.white;
            return NutView.NutColorToUnityColor(bolt.Nuts[bolt.Nuts.Count - 1].Color);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Hover
        // ─────────────────────────────────────────────────────────────────────

        private void StartHover()
        {
            StopHover();
            if (liftedNut == null) return;
            float baseY = liftedNut.transform.position.y;
            Sequence hover = DOTween.Sequence().SetTarget(liftedNut.transform);
            hover.Append(liftedNut.transform.DOMoveY(baseY + hoverAmount, hoverHalfCycleDuration).SetEase(Ease.InOutSine));
            hover.Append(liftedNut.transform.DOMoveY(baseY, hoverHalfCycleDuration).SetEase(Ease.InOutSine));
            hover.SetLoops(-1);
            hoverSequences.Add(hover);
        }

        private void StopHover()
        {
            foreach (Sequence h in hoverSequences) if (h != null && h.IsActive()) h.Kill(false);
            hoverSequences.Clear();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Bolt Feedback
        // ─────────────────────────────────────────────────────────────────────

        private IEnumerator PulseInvalidDestination(BoltView bolt)
        {
            inputLocked = true;
            PulseBoltScale(bolt, new Vector3(1.02f, invalidScalePulse, 1.02f), invalidPulseDuration);
            yield return new WaitForSeconds(invalidPulseDuration);
            inputLocked = false;
        }

        private void PulseBoltSelection(BoltView bolt) =>
            PulseBoltScale(bolt, Vector3.one * boltSelectionScale, boltSelectionPulseDuration);

        private void PulseBoltAttach(BoltView bolt) =>
            PulseBoltScale(bolt, new Vector3(1.012f, boltAttachYScale, 1.012f), boltAttachPulseDuration);

        private void PulseBoltScale(BoltView bolt, Vector3 multiplier, float duration)
        {
            if (bolt == null) return;
            Transform tr = bolt.transform;
            DOTween.Kill(tr, false);
            tr.localScale = bolt.RestingLocalScale;
            Sequence pulse = DOTween.Sequence().SetTarget(tr);
            pulse.Append(tr.DOScale(Vector3.Scale(bolt.RestingLocalScale, multiplier), duration * .45f).SetEase(Ease.OutQuad));
            pulse.Append(tr.DOScale(bolt.RestingLocalScale, duration * .55f).SetEase(Ease.InOutSine));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers — game rules
        // ─────────────────────────────────────────────────────────────────────

        private List<NutView> TopMatchingGroup(BoltView bolt)
        {
            var group = new List<NutView>();
            if (bolt == null || bolt.Nuts.Count == 0) return group;
            NutColor color = bolt.Nuts[bolt.Nuts.Count - 1].Color;
            for (int i = bolt.Nuts.Count - 1; i >= 0 && bolt.Nuts[i].Color == color; i--)
                group.Insert(0, bolt.Nuts[i]);
            return group;
        }

        private static bool CanSelect(BoltView bolt) =>
            bolt != null && !bolt.IsLocked && bolt.Nuts.Count > 0;

        private int GetMoveCount(BoltView destination, List<NutView> matchingGroup)
        {
            if (destination == null || matchingGroup == null || matchingGroup.Count == 0 || destination.IsLocked)
                return 0;

            // Use ExpandableBoltController capacity if present.
            int effectiveCapacity = GetEffectiveCapacity(destination);
            int availableSpaces   = effectiveCapacity - destination.Nuts.Count;
            if (availableSpaces <= 0) return 0;

            NutColor movingColor = matchingGroup[matchingGroup.Count - 1].Color;
            if (destination.Nuts.Count > 0 && destination.Nuts[destination.Nuts.Count - 1].Color != movingColor)
                return 0;

            return Mathf.Min(matchingGroup.Count, availableSpaces);
        }

        /// <summary>Returns effective capacity, respecting ExpandableBoltController if present.</summary>
        private static int GetEffectiveCapacity(BoltView bolt)
        {
            if (bolt == null) return 0;
            var exp = bolt.GetComponent<ExpandableBoltController>();
            return exp != null ? exp.CurrentCapacity : BoltView.Capacity;
        }

        private bool CanMove(BoltView from, BoltView to, List<NutView> moving) =>
            from != null && GetMoveCount(to, moving) > 0;

        private void CheckWin()
        {
            if (won || levelManager == null) return;
            won = levelManager.ActiveBolts.All(b =>
                b.Nuts.Count == 0 || (b.Nuts.Count == BoltView.Capacity && b.IsComplete()));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Tween Safety
        // ─────────────────────────────────────────────────────────────────────

        private void KillGameplayTweens()
        {
            if (gameplaySequence != null && gameplaySequence.IsActive()) gameplaySequence.Kill(false);
            gameplaySequence = null;
            StopHover();
        }

        private void KillAllTweens()
        {
            KillGameplayTweens();
            foreach (var kv in capSequences)
                if (kv.Value != null && kv.Value.IsActive()) kv.Value.Kill(false);
            capSequences.Clear();
        }

        private static void KillNutTween(NutView nut)
        {
            if (nut != null) DOTween.Kill(nut.transform, false);
        }

        private void ClearSelection()
        {
            StopHover();
            if (selectedBolt != null) selectedBolt.SetSelectionEffect(false);
            selectedBolt = null;
            selectedNuts.Clear();
            liftedNut = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Stack Snapping
        // ─────────────────────────────────────────────────────────────────────

        private static void RestoreNutToStack(BoltView bolt, NutView nut, int index)
        {
            if (bolt == null || nut == null) return;
            KillNutTween(nut);
            SnapNutToStack(bolt, nut, index);
        }

        private static void SnapNutToStack(BoltView bolt, NutView nut, int index)
        {
            if (bolt == null || nut == null) return;
            Transform tr = nut.transform;
            if (tr.parent != bolt.NutContainer) tr.SetParent(bolt.NutContainer, false);
            tr.localPosition = bolt.GetStackPosition(index);
            tr.localRotation = nut.RestingLocalRotation;
            tr.localScale    = nut.RestingLocalScale;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Fallback GUI (no UIManager)
        // ─────────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (uiManager != null && uiManager.enabled) return;
            Matrix4x4 old   = GUI.matrix;
            float     scale = Screen.width / 1080f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            var style = new GUIStyle(GUI.skin.button) { fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            if (GUI.Button(new Rect(50,  50, 190, 68), "↻  RESTART",              style) && !inputLocked) RestartLevel();
            if (GUI.Button(new Rect(840, 50, 190, 68), "LEVEL " + currentLevelNumber, style) && !inputLocked) LoadNextLevel();
            if (GUI.Button(new Rect(260, 50, 160, 68), "↩ UNDO",                  style) && CanUndo)        UndoLastMove();
            if (won)
            {
                var wonStyle = new GUIStyle(GUI.skin.label) { fontSize = 66, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                GUI.Label(new Rect(0, Screen.height / scale * .43f, 1080, 100), "LEVEL COMPLETE!", wonStyle);
            }
            GUI.matrix = old;
        }
    }
}
