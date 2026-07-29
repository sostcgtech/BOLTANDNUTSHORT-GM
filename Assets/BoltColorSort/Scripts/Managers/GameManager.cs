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
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        [Header("Manager References")]
        [SerializeField] private LevelManager levelManager;
        [SerializeField] private UIManager    uiManager;
        [SerializeField] private ProceduralLevelGenerator proceduralLevelGenerator;

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
        [SerializeField, Min(0f)]   private float hoverAmount             = .035f;
        [SerializeField, Min(.01f)] private float hoverHalfCycleDuration  = .32f;

        // Followers ───────────────────────────────────────────────────────────
        [Header("Followers")]
        [Tooltip("Minimum clearance time before the next nut starts. The move also enforces enough time for the prior nut to clear the shared hover point.")]
        [SerializeField, Range(.05f, .30f)] private float followerDelay   = .14f;

        // Travel ──────────────────────────────────────────────────────────────
        [Header("Travel")]
        [Tooltip("Duration of the vertical lift stage (followers only).")]
        [SerializeField, Min(.01f)] private float liftDuration            = .20f;

        [Tooltip("Minimum horizontal travel duration regardless of distance.")]
        [SerializeField, Min(.01f)] private float travelDurationMin       = .28f;

        [Tooltip("Horizontal travel time added per world-unit of distance.")]
        [SerializeField, Min(.01f)] private float travelDurationPerUnit   = .12f;

        [Tooltip("Degrees per second the nut gently rotates during travel.")]
        [SerializeField, Min(0f)]   private float travelRotationSpeed     = 200f;

        // Landing ─────────────────────────────────────────────────────────────
        [Header("Landing")]
        [SerializeField, Min(.01f)] private float landingDuration         = .16f;
        [SerializeField, Range(1f, 1.2f)] private float landingBounceScale = 1.045f;

        // Entry Animation ─────────────────────────────────────────────────────
        [Header("Entry Animation")]
        [Tooltip("Height above final slot each nut starts from.")]
        [SerializeField, Range(.3f, .7f)]   private float entryHeightOffset  = .50f;

        [Tooltip("Minimum random Y-rotation added to each nut at entry start.")]
        [SerializeField, Range(90f, 360f)]  private float entryRotMin        = 90f;

        [Tooltip("Maximum random Y-rotation added to each nut at entry start.")]
        [SerializeField, Range(90f, 360f)]  private float entryRotMax        = 270f;

        [Tooltip("Bottom-to-top gap used by the ordered startup queue on each bolt.")]
        [SerializeField, Range(0f, .30f)]   private float entryMaxDelay      = .20f;

        [Tooltip("Duration of each nut's drop animation.")]
        [SerializeField, Range(.15f, .50f)] private float entryDuration      = .27f;

        // Completion Cap ──────────────────────────────────────────────────────
        [Header("Completion Cap")]
        [Tooltip("Height the cap starts above CompletionCapPoint.")]
        [SerializeField, Min(0f)]          private float capPopHeight    = .18f;

        [Tooltip("Scale the cap starts at before popping.")]
        [SerializeField, Range(.5f, 1f)]   private float capStartScale   = .75f;

        [Tooltip("Duration of the initial upward pop.")]
        [SerializeField, Min(.01f)]        private float capPopDuration  = .14f;

        [Tooltip("Duration of the downward close motion.")]
        [SerializeField, Min(.01f)]        private float capCloseDuration = .22f;

        [Tooltip("Scale overshoot on landing bounce (0 = no bounce).")]
        [SerializeField, Range(0f, .15f)]  private float capBounceAmount = .06f;

        // Bolt Feedback ───────────────────────────────────────────────────────
        [Header("Bolt Feedback (scale only)")]
        [SerializeField, Range(1f, 1.15f)] private float boltSelectionScale       = 1.045f;
        [SerializeField, Min(.01f)]        private float boltSelectionPulseDuration = .12f;
        [SerializeField, Range(.9f, 1f)]   private float boltAttachYScale         = .965f;
        [SerializeField, Min(.01f)]        private float boltAttachPulseDuration  = .11f;
        [SerializeField, Range(.9f, 1.1f)] private float invalidScalePulse        = .965f;
        [SerializeField, Min(.01f)]        private float invalidPulseDuration      = .16f;

        // ─────────────────────────────────────────────────────────────────────
        // Runtime State
        // ─────────────────────────────────────────────────────────────────────

        private BoltView         selectedBolt;
        private List<NutView>    selectedNuts = new List<NutView>();
        private NutView          liftedNut;        // Only the topmost nut is visually hovering.
        private Coroutine        selectionRoutine;
        private Coroutine        moveRoutine;
        private Sequence         gameplaySequence;
        private readonly List<Sequence>             hoverSequences = new List<Sequence>();
        private readonly Dictionary<BoltView, Sequence> capSequences  = new Dictionary<BoltView, Sequence>();

        private bool inputLocked;
        private bool won;
        private int  currentLevelIndex;

        public bool IsWon             => won;
        public int  CurrentLevelIndex => currentLevelIndex;

        // ─────────────────────────────────────────────────────────────────────
        // Unity Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;
            if (levelManager == null) levelManager = GetComponent<LevelManager>() ?? FindObjectOfType<LevelManager>() ?? gameObject.AddComponent<LevelManager>();
            if (uiManager    == null) uiManager    = GetComponent<UIManager>()    ?? FindObjectOfType<UIManager>()    ?? gameObject.AddComponent<UIManager>();
            if (proceduralLevelGenerator == null) proceduralLevelGenerator = GetComponent<ProceduralLevelGenerator>() ?? FindObjectOfType<ProceduralLevelGenerator>() ?? gameObject.AddComponent<ProceduralLevelGenerator>();
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

            inputLocked     = true;
            selectedBolt    = null;
            selectedNuts.Clear();
            liftedNut       = null;
            selectionRoutine = null;
            moveRoutine      = null;
            won              = false;

            LevelDataSO currentData = proceduralLevelGenerator != null
                ? proceduralLevelGenerator.GetOrGenerateCurrentLevel(currentLevelIndex, BoltView.Capacity)
                : null;
            bool built = levelManager != null && currentData != null && levelManager.BuildLevel(currentData, out _);
            if (uiManager != null) uiManager.UpdateLevelDisplay();
            if (!built) { inputLocked = false; return; }

            // Silently lock any bolt that starts already complete. Cap shown AFTER entry animation.
            foreach (BoltView bolt in levelManager.ActiveBolts)
            {
                if (bolt != null && !bolt.IsLocked && bolt.IsComplete())
                    bolt.LockBoltSilently();
            }
            CheckWin();

            // Entry animation locks input until all nuts have settled.
            StartCoroutine(PlayEntryAnimation());
        }

        // LoadCurrentLevel reads the generator's deep-copied snapshot; it never generates on restart.
        public void RestartLevel() => LoadCurrentLevel();

        public void LoadNextLevel()
        {
            currentLevelIndex++;
            if (proceduralLevelGenerator != null) proceduralLevelGenerator.AdvanceToNextLevel(currentLevelIndex);
            LoadCurrentLevel();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Tap Handling
        // ─────────────────────────────────────────────────────────────────────

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
                // A destination that cannot receive this group is still a useful
                // one-tap selection switch. The current hover always rethreads
                // before the next bolt is allowed to lift.
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

            // Topmost nut only lifts visually. The rest stay at their stack positions.
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
            moveRoutine = StartCoroutine(MoveSelectedNuts(destination));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Lift — single top nut to fixed SelectionHoverPoint
        // ─────────────────────────────────────────────────────────────────────

        private IEnumerator LiftTopNut()
        {
            KillGameplayTweens();
            if (liftedNut == null) { inputLocked = false; yield break; }

            // Snap to correct stack position first to ensure clean start.
            int stackIdx = selectedBolt.Nuts.IndexOf(liftedNut);
            RestoreNutToStack(selectedBolt, liftedNut, stackIdx);

            // Fixed target world position — never depends on stack height or which nut.
            Vector3 hoverTarget = selectedBolt.GetHoverWorldPosition();

            Sequence liftSeq = DOTween.Sequence().SetTarget(liftedNut.transform);
            liftSeq.Join(liftedNut.transform.DOMove(hoverTarget, selectionLiftDuration).SetEase(Ease.OutCubic));
            liftSeq.Join(liftedNut.transform.DORotate(
                Vector3.up * (selectionRotationSpeed * selectionLiftDuration),
                selectionLiftDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));

            gameplaySequence = DOTween.Sequence().SetTarget(this).Append(liftSeq);
            yield return gameplaySequence.WaitForCompletion();

            gameplaySequence    = null;
            selectionRoutine    = null;
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

            // Empty/locked bolts cannot become a source selection, but the prior
            // hover has still been cleanly returned instead of being left floating.
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

            BoltView source = selectedBolt;
            NutView returningNut = liftedNut;
            int stackIdx = source.Nuts.IndexOf(returningNut);
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
        // ─────────────────────────────────────────────────────────────────────

        private IEnumerator MoveSelectedNuts(BoltView destination)
        {
            KillGameplayTweens();
            BoltView      source             = selectedBolt;
            int           moveCount          = GetMoveCount(destination, selectedNuts);
            // selectedNuts is bottom-to-top; transfer the top portion only.
            List<NutView> moving             = selectedNuts.Skip(selectedNuts.Count - moveCount).ToList();
            int           destStartIdx       = destination.Nuts.Count;
            source.SetSelectionEffect(false);

            gameplaySequence = DOTween.Sequence().SetTarget(this);
            // Every follower targets the same hover point. This spacing makes the
            // previous nut visibly clear it before the next one can arrive.
            float followerClearanceDelay = Mathf.Max(followerDelay, selectionLiftDuration * .65f);

            // Queue order: top-of-source first (queueOrder 0) → bottom-of-source last.
            // queueOrder 0 is the leader (already hovering). Followers lift from stack.
            for (int q = 0; q < moving.Count; q++)
            {
                int      srcIdx  = moving.Count - 1 - q; // Count-1 = top (leader), 0 = bottom
                NutView  nut     = moving[srcIdx];
                Transform tr     = nut.transform;
                KillNutTween(nut);

                int     destIdx      = destStartIdx + q;
                Vector3 destWorld    = destination.NutContainer.TransformPoint(destination.GetStackPosition(destIdx));
                float   seqStart     = q * followerClearanceDelay;

                Sequence nutSeq = DOTween.Sequence().SetTarget(tr);

                // ── Shared hover height (world Y of SelectionHoverPoint) ─────
                float   hoverWorldY    = source.GetHoverWorldPosition().y;

                // ── Phase 1: Vertical Lift (followers only) ──────────────────
                if (q == 0)
                {
                    // Leader: already at hover height — snap cleanly, no lift.
                    Vector3 snapPos = tr.position;
                    snapPos.y       = hoverWorldY;
                    tr.position     = snapPos;
                }
                else
                {
                    // Follower: move straight up on Y only to reach hover height.
                    Vector3 liftTarget = new Vector3(tr.position.x, hoverWorldY, tr.position.z);

                    Sequence liftSeq = DOTween.Sequence();
                    liftSeq.Join(tr.DOMove(liftTarget, liftDuration).SetEase(Ease.OutCubic));
                    liftSeq.Join(tr.DORotate(
                        Vector3.up * (selectionRotationSpeed * liftDuration),
                        liftDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));
                    nutSeq.Append(liftSeq);
                }

                // ── Phase 2: Horizontal Travel ───────────────────────────────
                // Move only on X/Z — keep Y locked at hover height.
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

                // ── Phase 3: reparent → land → rethread ────────────────────
                // Capture loop vars for lambda.
                NutView  capturedNut     = nut;
                int      capturedDestIdx = destIdx;
                BoltView capturedDest    = destination;

                nutSeq.AppendCallback(() => tr.SetParent(capturedDest.NutContainer, worldPositionStays: true));

                // ── Phase 3: Vertical Drop ───────────────────────────────────
                // Move straight down on Y only — no horizontal drift.
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
                // Landing bounce
                nutSeq.Append(tr.DOScale(nut.RestingLocalScale * landingBounceScale, .055f).SetEase(Ease.OutQuad));
                nutSeq.Append(tr.DOScale(nut.RestingLocalScale, .085f).SetEase(Ease.InOutSine));

                gameplaySequence.Insert(seqStart, nutSeq);
            }

            yield return gameplaySequence.WaitForCompletion();

            // ── Update logical state ─────────────────────────────────────────
            source.Nuts.RemoveRange(source.Nuts.Count - moving.Count, moving.Count);
            for (int q = 0; q < moving.Count; q++)
                destination.Nuts.Add(moving[moving.Count - 1 - q]);

            // Reassert exact transforms after bounce.
            for (int q = 0; q < moving.Count; q++)
                RestoreNutToStack(destination, moving[moving.Count - 1 - q], destStartIdx + q);

            // ── Check completion ─────────────────────────────────────────────
            bool srcCompleted  = TryLockCompleted(source);
            bool destCompleted = TryLockCompleted(destination);

            // Win state updates immediately; popup waits for cap animations.
            CheckWin();

            Coroutine srcCapRoutine  = srcCompleted                          ? StartCoroutine(PlayCapAnimation(source))      : null;
            Coroutine destCapRoutine = (destCompleted && destination != source) ? StartCoroutine(PlayCapAnimation(destination)) : null;
            if (srcCapRoutine  != null) yield return srcCapRoutine;
            if (destCapRoutine != null) yield return destCapRoutine;

            ClearSelection();
            gameplaySequence = null;
            moveRoutine      = null;

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
                ShowSilentCaps(); // Show caps for already-locked bolts.
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
                    // Each bolt enters bottom-to-top. A nut starts only after the
                    // one below has substantially settled, avoiding stack overlap.
                    float   entryClearanceDelay = Mathf.Max(entryMaxDelay, entryDuration * .65f);
                    float   delay = i * entryClearanceDelay;

                    // Set the nut's start state immediately (before sequence plays).
                    nut.transform.position     = startWorld;
                    nut.transform.localRotation = Quaternion.Euler(
                        nut.RestingLocalRotation.eulerAngles.x,
                        nut.RestingLocalRotation.eulerAngles.y + randomYRot,
                        nut.RestingLocalRotation.eulerAngles.z);

                    // Capture for lambda.
                    BoltView capturedBolt  = bolt;
                    NutView  capturedNut   = nut;
                    int      capturedIdx   = i;

                    Sequence nutEntry = DOTween.Sequence().SetTarget(nut.transform);
                    nutEntry.Append(nut.transform.DOMove(finalWorld, entryDuration).SetEase(Ease.OutCubic));
                    nutEntry.Join(nut.transform.DOLocalRotateQuaternion(
                        nut.RestingLocalRotation, entryDuration).SetEase(Ease.OutCubic));
                    nutEntry.OnComplete(() => SnapNutToStack(capturedBolt, capturedNut, capturedIdx));

                    entrySeq.Insert(delay, nutEntry);
                }
            }

            yield return entrySeq.WaitForCompletion();

            // After entry animation, silently show caps for pre-locked bolts (no pop animation).
            ShowSilentCaps();

            inputLocked = false;
        }

        /// <summary>
        /// Instantly places caps at rest on any bolt that was locked before the level entry animation.
        /// No pop, bounce, or particle — just appears at CompletionCapPoint.
        /// </summary>
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

            Color     capColor   = GetTopNutColor(bolt);
            Transform capTr      = bolt.CompletionCapTransform;
            Vector3   capScale   = bolt.CompletionCapRestingLocalScale;
            Vector3   restPos    = bolt.GetCapWorldPosition();
            Vector3   startPos   = restPos + Vector3.up * capPopHeight;
            Vector3   popPos     = restPos + Vector3.up * (capPopHeight * .45f);

            // Kill any existing cap tween for this bolt.
            if (capSequences.TryGetValue(bolt, out Sequence prev) && prev != null && prev.IsActive())
                prev.Kill(false);

            bolt.ActivateCap(capColor);
            capTr.position   = startPos;
            capTr.localScale = capScale * capStartScale;

            Sequence capSeq = DOTween.Sequence().SetTarget(capTr);

            // Pop up + scale open
            capSeq.Append(capTr.DOScale(capScale * 1.08f, capPopDuration).SetEase(Ease.OutBack));
            capSeq.Join(capTr.DOMove(popPos, capPopDuration).SetEase(Ease.OutQuad));

            // Close down onto bolt
            capSeq.Append(capTr.DOMove(restPos, capCloseDuration).SetEase(Ease.InCubic));

            // Soft landing bounce
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
        // Helpers — logic unchanged from original
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

        private static int GetMoveCount(BoltView destination, List<NutView> matchingGroup)
        {
            if (destination == null || matchingGroup == null || matchingGroup.Count == 0 || destination.IsLocked)
                return 0;

            int availableSpaces = BoltView.Capacity - destination.Nuts.Count;
            if (availableSpaces <= 0) return 0;

            NutColor movingColor = matchingGroup[matchingGroup.Count - 1].Color;
            if (destination.Nuts.Count > 0 && destination.Nuts[destination.Nuts.Count - 1].Color != movingColor)
                return 0;

            return Mathf.Min(matchingGroup.Count, availableSpaces);
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

        // Used from inside active sequences — does NOT kill the tween.
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
            if (GUI.Button(new Rect(50,  50, 190, 68), "↻  RESTART",             style) && !inputLocked) RestartLevel();
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
