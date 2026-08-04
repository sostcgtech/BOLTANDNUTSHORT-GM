using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

namespace NutBoltSort
{
    /// <summary>
    /// Owns DOTween animation flows for nut selection, sequential follower transfers,
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
        [Tooltip("Time between followers beginning their transfer. Their landings overlap while the previous nut settles.")]
        [FormerlySerializedAs("followerDelay")]
        [SerializeField, Range(.06f, .10f)] private float followerDropDelay = .08f;

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
        [Tooltip("Base duration of the threaded vertical drop. It scales slightly with drop distance.")]
        [FormerlySerializedAs("landingDuration")]
        [SerializeField, Range(.20f, .28f)] private float dropDuration = .24f;
        [SerializeField] private Ease dropEase = Ease.InOutSine;
        [Tooltip("Degrees of rethread rotation per world-unit descended.")]
        [SerializeField, Min(0f)] private float threadRotationMultiplier = 520f;
        [Tooltip("Y scale at the end of the screw-down. X/Z expand proportionally to retain a firm mechanical contact.")]
        [SerializeField, Range(.94f, 1f)] private float landingCompression = .96f;
        [SerializeField, Range(1f, 1.1f)] private float landingBounceScale = 1.025f;
        [SerializeField, Range(.05f, .07f)] private float landingImpactDuration = .06f;
        [SerializeField, Range(.08f, .12f)] private float landingRecoveryDuration = .10f;
        [SerializeField, Range(1f, 1.1f)] private float finalNutImpactMultiplier = 1.01f;
        [SerializeField] private bool useLandingRotationVariation = true;
        [SerializeField, Range(8f, 15f)] private float landingRotationVariation = 10f;
        [FormerlySerializedAs("completionEffectDelay")]
        [SerializeField, Range(.03f, .06f)] private float completionStartDelay = .04f;

        // Entry Animation ─────────────────────────────────────────────────────
        [Header("Entry Animation")]
        [SerializeField] private Vector3 boltEntryOffset = new Vector3(0f, -.35f, 0f);
        [SerializeField, Range(.85f, .92f)] private float boltEntryStartScale = .90f;
        [SerializeField, Range(.22f, .32f)] private float boltEntryDuration = .26f;
        [SerializeField, Range(.03f, .06f)] private float boltEntryStagger = .04f;
        [SerializeField, Range(.08f, .14f)] private float rowEntryDelay = .10f;
        [SerializeField] private bool animateBoltsInParallel = true;
        [SerializeField, Range(1, 4)] private int maximumConcurrentBoltEntrySequences = 4;
        [FormerlySerializedAs("entryHeightOffset")]
        [SerializeField, Range(.55f, .85f)] private float nutEntryHeight = .65f;
        [SerializeField, Range(.88f, .95f)] private float nutEntryStartScale = .92f;
        [FormerlySerializedAs("entryDuration")]
        [SerializeField, Range(.18f, .28f)] private float nutEntryDuration = .22f;
        [FormerlySerializedAs("entryMaxDelay")]
        [SerializeField, Range(.05f, .09f)] private float nutEntryStagger = .07f;
        [SerializeField, Range(120f, 300f)] private float nutEntryRotation = 180f;
        [SerializeField, Range(1.02f, 1.04f)] private float nutSettleScale = 1.025f;
        [SerializeField, Range(.05f, .10f)] private float nutSettleDuration = .08f;
        [SerializeField] private bool useClearanceBasedNutStagger = true;
        [SerializeField] private bool allowParallelNutEntryAcrossBolts = true;

        // Completion Cap ──────────────────────────────────────────────────────
        [Header("Completion Cap")]
        [Header("Completion Wave")]
        [SerializeField, Range(1.06f, 1.10f)] private float nutWavePulseScale = 1.08f;
        [SerializeField, Range(.97f, .99f)] private float nutWaveCompressionScale = .98f;
        [SerializeField, Range(.03f, .06f)] private float nutWaveLiftAmount = .04f;
        [SerializeField, Range(.12f, .18f)] private float nutWavePulseDuration = .15f;
        [SerializeField, Range(.035f, .06f)] private float nutWaveStagger = .045f;
        [SerializeField, Range(0f, 2f)] private float nutWaveGlowStrength = .75f;
        [Header("Completion Cap")]
        [FormerlySerializedAs("capPopHeight")]
        [SerializeField, Range(.20f, .35f)] private float capStartHeight = .25f;
        [FormerlySerializedAs("capStartScale")]
        [SerializeField, Range(.65f, .80f)] private float capStartScale = .72f;
        [FormerlySerializedAs("capCloseDuration")]
        [SerializeField, Range(.16f, .24f)] private float capDropDuration = .20f;
        [SerializeField, Range(90f, 180f)] private float capRotationAmount = 120f;
        [SerializeField] private Vector3 capImpactScale = new Vector3(1.03f, .94f, 1.03f);
        [FormerlySerializedAs("capPopDuration")]
        [SerializeField, Range(.05f, .08f)] private float capImpactDuration = .06f;
        [SerializeField, Range(.06f, .10f)] private float capSettleDuration = .08f;
        [SerializeField, Range(.70f, .80f)] private float capStartProgress = .75f;
        [SerializeField, Range(1.02f, 1.04f)] private float finalStackPulseScale = 1.025f;
        [SerializeField, Range(.08f, .14f)] private float finalStackPulseDuration = .10f;
        [SerializeField, Range(.10f, .20f)] private float winDelayAfterCompletion = .15f;

        [Header("Action Uses")]
        [SerializeField, Min(0)] private int startingUndoUses = 5;
        [SerializeField, Min(0)] private int startingExpandUses = 1;
        [SerializeField, Min(1)] private int undoRefillAmount = 5;
        [SerializeField, Min(1)] private int expandRefillAmount = 1;

        // Bolt Feedback ───────────────────────────────────────────────────────
        [Header("Bolt Feedback (scale only)")]
        [SerializeField, Range(1f, 1.15f)] private float boltSelectionScale        = 1.045f;
        [SerializeField, Min(.01f)]        private float boltSelectionPulseDuration = .12f;
        [FormerlySerializedAs("boltAttachYScale")]
        [SerializeField, Range(.97f, 1f)]  private float boltContactCompression     = .985f;
        [FormerlySerializedAs("boltAttachPulseDuration")]
        [SerializeField, Range(.06f, .10f)] private float boltContactDuration       = .08f;
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
        // Transfers own their sequences so unrelated transfers never cancel one another.
        private readonly HashSet<Sequence> activeMoveSequences = new HashSet<Sequence>();
        // A transfer commits its stack state when it starts. Its two bolts remain reserved until
        // the visual path and any completion cap have finished, preventing transform races.
        private readonly HashSet<BoltView> busyBolts = new HashSet<BoltView>();
        private readonly List<MoveRecord> pendingMoveRecords = new List<MoveRecord>();
        private int activeTransferCount;
        private readonly List<Sequence>              hoverSequences = new List<Sequence>();
        private readonly Dictionary<BoltView, Sequence> capSequences  = new Dictionary<BoltView, Sequence>();
        private readonly List<Sequence> entrySequences = new List<Sequence>();
        private int activeBoltEntryCount;
        private int activeNutEntryCount;
        private int entryGenerationVersion;
        private bool isLevelEntryPlaying;

        private bool inputLocked;
        private bool won;
        private int  currentLevelNumber = 1; // 1-based; persisted via PlayerPrefs
        private int  remainingUndoUses;
        private int  remainingExpandUses;
        private bool isUndoAdRequestActive;
        private bool isExpandAdRequestActive;

        private const string PREFS_LEVEL = "CurrentLevelNumber";

        /// <summary>Optional hook fired at the precise moment each transferred nut contacts its destination slot.</summary>
        public event Action<NutView, BoltView> OnNutLanded;
        public event Action<BoltView> OnCompletionWaveStarted;
        public event Action<BoltView> OnCompletionWaveReachedTop;
        public event Action<BoltView> OnCompletionCapClosed;

        // ─────────────────────────────────────────────────────────────────────
        // Public Properties
        // ─────────────────────────────────────────────────────────────────────

        public bool IsWon            => won;
        /// <summary>1-based current level number.</summary>
        public int  CurrentLevelNumber => currentLevelNumber;
        /// <summary>0-based index — kept for backward-compatible UIManager calls.</summary>
        public int  CurrentLevelIndex  => currentLevelNumber - 1;
        public int StartingUndoUses => startingUndoUses;
        public int StartingExpandUses => startingExpandUses;
        public int UndoRefillAmount => undoRefillAmount;
        public int ExpandRefillAmount => expandRefillAmount;
        public int RemainingUndoUses => remainingUndoUses;
        public int RemainingExpandUses => remainingExpandUses;
        public bool IsUndoAdRequestActive => isUndoAdRequestActive;
        public bool IsExpandAdRequestActive => isExpandAdRequestActive;
        public int ActiveBoltEntryCount => activeBoltEntryCount;
        public int ActiveNutEntryCount => activeNutEntryCount;
        public int EntryGenerationVersion => entryGenerationVersion;
        public bool IsLevelEntryPlaying => isLevelEntryPlaying;
        /// <summary>Display availability: only uses and move history control the Undo button's visual state.</summary>
        public bool HasUndoAction => remainingUndoUses > 0 && undoManager != null && undoManager.CanUndo;
        /// <summary>Display availability: only uses and an unexpanded bolt control the Expand button's visual state.</summary>
        public bool HasExpandAction => remainingExpandUses > 0 && HasExpandableBoltBelowMax();
        /// <summary>True when undo has a use, a history record, and no gameplay/UI blocker.</summary>
        public bool CanUndo => HasUndoAction &&
                               !inputLocked && activeTransferCount == 0 && !won &&
                               (uiManager == null || !uiManager.IsUIOpen);
        /// <summary>True when an expandable bolt can accept one paid stage increase.</summary>
        public bool CanExpand => HasExpandAction && selectedBolt == null && !inputLocked && activeTransferCount == 0 && !won &&
                                 (uiManager == null || !uiManager.IsUIOpen) && HasAvailableExpandableBolt();

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
            ResetActionUses();
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

        public void LoadCurrentLevel(bool isRestart = false)
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
            activeTransferCount = 0;
            busyBolts.Clear();
            pendingMoveRecords.Clear();
            won              = false;
            ResetActionUses();

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

            uiManager?.ShowLevelRibbon(isRestart);
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
                activeTransferCount = 0;
                busyBolts.Clear();
                pendingMoveRecords.Clear();
                won              = false;
                ResetActionUses();

                levelManager?.BuildLevel(levelProvider.CurrentSnapshot, out _);
                if (uiManager != null)
                {
                    uiManager.UpdateLevelDisplay();
                    uiManager.RefreshActionButtonStates();
                }

                tutorialController?.OnLevelLoaded(currentLevelNumber, this, levelManager);
                uiManager?.ShowLevelRibbon(isRestart: true);
                StartCoroutine(PlayEntryAnimation());
            }
            else
            {
                LoadCurrentLevel(isRestart: true);
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
            if (!CanExpand || levelManager == null) return;
            foreach (BoltView bolt in levelManager.ActiveBolts)
            {
                if (bolt == null) continue;
                var exp = bolt.GetComponent<ExpandableBoltController>();
                if (exp != null && !exp.IsAtMax && !exp.IsExpanding && !IsBoltBusy(bolt))
                {
                    bool increased = exp.IncreaseCapacity();
                    if (increased)
                    {
                        remainingExpandUses = Mathf.Max(0, remainingExpandUses - 1);
                        tutorialController?.OnExpandableBoltUsed();
                        uiManager?.RefreshActionButtonStates();
                        StartCoroutine(RefreshActionButtonsAfterExpansion(exp));
                        return;
                    }
                }
            }
        }

        /// <summary>Temporary rewarded-ad entry point. Replace only the simulated callback when an ad SDK is added.</summary>
        public void RequestUndoRewardAd()
        {
            if (remainingUndoUses > 0 || isUndoAdRequestActive) return;
            isUndoAdRequestActive = true;
            uiManager?.RefreshUndoAndExpandUI();
            StartCoroutine(SimulateUndoRewardAd());
        }

        /// <summary>Temporary rewarded-ad entry point. Replace only the simulated callback when an ad SDK is added.</summary>
        public void RequestExpandRewardAd()
        {
            if (remainingExpandUses > 0 || isExpandAdRequestActive) return;
            isExpandAdRequestActive = true;
            uiManager?.RefreshUndoAndExpandUI();
            StartCoroutine(SimulateExpandRewardAd());
        }

        public void OnUndoRewardAdSucceeded()
        {
            if (!isUndoAdRequestActive) return;
            isUndoAdRequestActive = false;
            remainingUndoUses = Mathf.Max(0, remainingUndoUses + undoRefillAmount);
            uiManager?.RefreshUndoAndExpandUI(animateUndoReward: true);
        }

        public void OnUndoRewardAdFailed()
        {
            if (!isUndoAdRequestActive) return;
            isUndoAdRequestActive = false;
            uiManager?.RefreshUndoAndExpandUI();
        }

        public void OnExpandRewardAdSucceeded()
        {
            if (!isExpandAdRequestActive) return;
            isExpandAdRequestActive = false;
            remainingExpandUses = Mathf.Max(0, remainingExpandUses + expandRefillAmount);
            uiManager?.RefreshUndoAndExpandUI(animateExpandReward: true);
        }

        public void OnExpandRewardAdFailed()
        {
            if (!isExpandAdRequestActive) return;
            isExpandAdRequestActive = false;
            uiManager?.RefreshUndoAndExpandUI();
        }

        /// <summary>
        /// Unlocks the first locked bolt.
        /// Called by UIManager when the player presses the Unlock button.
        /// </summary>
        public void UnlockFirstLockedBolt()
        {
            if (inputLocked || activeTransferCount > 0 || won || levelManager == null) return;
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
            if (srcBolt == null || dstBolt == null || record.movedNutCount <= 0 || dstBolt.Nuts.Count == 0) return;

            // Undo transfers nuts FROM destination BACK TO source.
            remainingUndoUses = Mathf.Max(0, remainingUndoUses - 1);
            moveRoutine = StartCoroutine(ExecuteUndoTransfer(dstBolt, srcBolt, record));
            uiManager?.RefreshActionButtonStates();
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
            float followerClearanceDelay = Mathf.Clamp(followerDropDelay, .06f, .10f);
            // Travel directly from the source bolt's hover point to the destination
            // bolt's hover point. This preserves each row's intentional height offset.
            Vector3 sourceHover = undoSrc.GetHoverWorldPosition();
            Vector3 destinationHover = undoDst.GetHoverWorldPosition();

            gameplaySequence = DOTween.Sequence().SetTarget(this);

            for (int q = 0; q < toMove.Count; q++)
            {
                int     srcIdx   = toMove.Count - 1 - q;
                NutView nut      = toMove[srcIdx];
                Transform tr     = nut.transform;
                Transform visual = nut.RotatingNutVisual;
                KillNutTween(nut);

                int     destIdx   = destStartIdx + q;
                float   seqStart  = q * followerClearanceDelay;

                Sequence nutSeq = DOTween.Sequence().SetTarget(tr);

                // Phase 1: lift (all nuts, as they all start on the stack)
                Vector3 liftTarget = sourceHover;
                Sequence liftSeq = DOTween.Sequence();
                liftSeq.Join(tr.DOMove(liftTarget, liftDuration).SetEase(Ease.OutCubic));
                liftSeq.Join(visual.DORotate(Vector3.up * (selectionRotationSpeed * liftDuration),
                    liftDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));
                nutSeq.Append(liftSeq);

                // Phase 2: horizontal travel
                Vector3 horizontalDest = destinationHover;
                float horizDist = Vector3.Distance(
                    new Vector3(tr.position.x, 0f, tr.position.z),
                    new Vector3(horizontalDest.x, 0f, horizontalDest.z));
                float horizDuration = Mathf.Max(travelDurationMin, horizDist * travelDurationPerUnit);
                Sequence travelSeq = DOTween.Sequence();
                travelSeq.Append(tr.DOMove(horizontalDest, horizDuration).SetEase(Ease.InOutSine));
                travelSeq.Join(visual.DORotate(Vector3.up * (travelRotationSpeed * horizDuration),
                    horizDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));
                nutSeq.Append(travelSeq);

                AppendThreadedLanding(nutSeq, nut, undoDst, destIdx, q == toMove.Count - 1);

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
            yield return RevealNewTopIfHidden(undoSrc);
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
            if (won || inputLocked || tapped == null || IsBoltBusy(tapped)) return;

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
            uiManager?.RefreshActionButtonStates();
            selectionRoutine = StartCoroutine(LiftTopNut());
        }

        private void BeginSelectionCancel()
        {
            if (selectionRoutine != null) StopCoroutine(selectionRoutine);
            StopHover();
            inputLocked = true;
            uiManager?.RefreshActionButtonStates();
            selectionRoutine = StartCoroutine(ReturnTopNut());
        }

        private void BeginSelectionSwitch(BoltView nextBolt)
        {
            if (selectionRoutine != null) StopCoroutine(selectionRoutine);
            StopHover();
            inputLocked = true;
            uiManager?.RefreshActionButtonStates();
            selectionRoutine = StartCoroutine(ReturnThenSelect(nextBolt));
        }

        private void BeginMove(BoltView destination)
        {
            if (selectionRoutine != null) StopCoroutine(selectionRoutine);
            StopHover();

            BoltView source = selectedBolt;
            int moveCount = GetMoveCount(destination, selectedNuts);
            if (source == null || moveCount <= 0) return;

            List<NutView> moving = selectedNuts.Skip(selectedNuts.Count - moveCount).ToList();
            int destStartIdx = destination.Nuts.Count;

            // Commit the board state before the animation begins. This makes a later move use
            // the same rules and capacity that will exist once this transfer lands.
            source.Nuts.RemoveRange(source.Nuts.Count - moving.Count, moving.Count);
            for (int q = 0; q < moving.Count; q++)
                destination.Nuts.Add(moving[moving.Count - 1 - q]);

            // Undo is unavailable until all active transfers settle. Queue the record now so
            // the completed history still follows the player's move order, even if a shorter
            // overlapping path finishes first.
            if (undoManager != null)
            {
                int srcBoltIdx = levelManager != null ? levelManager.IndexOfBolt(source) : -1;
                int dstBoltIdx = levelManager != null ? levelManager.IndexOfBolt(destination) : -1;
                pendingMoveRecords.Add(new MoveRecord
                {
                    sourceBoltIndex              = srcBoltIdx,
                    destinationBoltIndex         = dstBoltIdx,
                    movedColor                   = moving[0].Color,
                    movedNutCount                = moving.Count,
                    sourceWasCompletedAfter      = source.IsComplete(),
                    destinationWasCompletedAfter = destination.IsComplete()
                });
            }

            busyBolts.Add(source);
            busyBolts.Add(destination);
            activeTransferCount++;
            ClearSelection();
            uiManager?.RefreshActionButtonStates();

            // Tutorial observes the transfer; it never starts or owns movement.
            tutorialController?.OnTransferStarted();
            StartCoroutine(MoveSelectedNuts(source, destination, moving, destStartIdx));
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
            liftSeq.Join(liftedNut.RotatingNutVisual.DORotate(
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
            uiManager?.RefreshActionButtonStates();
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
            uiManager?.RefreshActionButtonStates();
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
                uiManager?.RefreshActionButtonStates();
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
            returnSeq.Join(returningNut.RotatingNutVisual.DORotate(
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

        private IEnumerator MoveSelectedNuts(BoltView source, BoltView destination,
                                             List<NutView> moving, int destStartIdx)
        {
            Sequence moveSequence = DOTween.Sequence().SetTarget(this);
            activeMoveSequences.Add(moveSequence);
            float followerClearanceDelay = Mathf.Clamp(followerDropDelay, .06f, .10f);
            // Use the authored hover points at both ends. A row-height offset is part
            // of those positions, so the travel path naturally reaches the back row's
            // hover point instead of forcing the nut through the bolt centre.
            Vector3 sourceHover = source.GetHoverWorldPosition();
            Vector3 destinationHover = destination.GetHoverWorldPosition();

            for (int q = 0; q < moving.Count; q++)
            {
                int      srcIdx = moving.Count - 1 - q;
                NutView  nut    = moving[srcIdx];
                Transform tr    = nut.transform;
                Transform visual = nut.RotatingNutVisual;
                KillNutTween(nut);

                int     destIdx   = destStartIdx + q;
                float   seqStart  = q * followerClearanceDelay;

                Sequence nutSeq = DOTween.Sequence().SetTarget(tr);

                // ── Phase 1: Vertical Lift (followers only) ──────────────────
                if (q == 0)
                {
                    // The leader is already at the source hover point after selection.
                    // Do not change its position before the hover-to-hover travel.
                }
                else
                {
                    Vector3 liftTarget = sourceHover;
                    Sequence liftSeq = DOTween.Sequence();
                    liftSeq.Join(tr.DOMove(liftTarget, liftDuration).SetEase(Ease.OutCubic));
                    liftSeq.Join(visual.DORotate(
                        Vector3.up * (selectionRotationSpeed * liftDuration),
                        liftDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));
                    nutSeq.Append(liftSeq);
                }

                // ── Phase 2: Horizontal Travel ───────────────────────────────
                Vector3 horizontalDest = destinationHover;
                float   horizDist      = Vector3.Distance(
                    new Vector3(tr.position.x, 0f, tr.position.z),
                    new Vector3(horizontalDest.x, 0f, horizontalDest.z));
                float   horizDuration  = Mathf.Max(travelDurationMin, horizDist * travelDurationPerUnit);
                float   rotDeg         = travelRotationSpeed * horizDuration;

                Sequence travelSeq = DOTween.Sequence();
                travelSeq.Append(tr.DOMove(horizontalDest, horizDuration).SetEase(Ease.InOutSine));
                travelSeq.Join(visual.DORotate(Vector3.up * rotDeg, horizDuration,
                    RotateMode.WorldAxisAdd).SetEase(Ease.Linear));
                nutSeq.Append(travelSeq);

                // ── Phase 3: reparent → threaded vertical drop ──────────────
                AppendThreadedLanding(nutSeq, nut, destination, destIdx, q == moving.Count - 1);

                moveSequence.Insert(seqStart, nutSeq);
            }

            yield return moveSequence.WaitForCompletion();
            activeMoveSequences.Remove(moveSequence);

            for (int q = 0; q < moving.Count; q++)
                RestoreNutToStack(destination, moving[moving.Count - 1 - q], destStartIdx + q);

            // Only the newly exposed source top may reveal. The source stays reserved
            // until this DOTween sequence settles; unrelated transfers remain available.
            yield return RevealNewTopIfHidden(source);

            bool srcCompleted  = TryLockCompleted(source);
            bool destCompleted = TryLockCompleted(destination);

            // Let the final threaded contact read clearly before the existing cap effect.
            if (destCompleted) yield return new WaitForSeconds(completionStartDelay);

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

            busyBolts.Remove(source);
            busyBolts.Remove(destination);
            activeTransferCount = Mathf.Max(0, activeTransferCount - 1);

            // A win can only be presented after every committed transfer has landed.
            if (activeTransferCount == 0)
            {
                if (undoManager != null)
                    foreach (MoveRecord record in pendingMoveRecords) undoManager.RecordMove(record);
                pendingMoveRecords.Clear();
                uiManager?.RefreshActionButtonStates();
                CheckWin();
                if (won) uiManager?.ShowWinPopup();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Entry Animation
        // ─────────────────────────────────────────────────────────────────────

        private IEnumerator PlayEntryAnimation()
        {
            inputLocked = true;
            int version = ++entryGenerationVersion;
            isLevelEntryPlaying = true;
            activeBoltEntryCount = 0;
            activeNutEntryCount = 0;
            entrySequences.Clear();

            if (levelManager == null || levelManager.ActiveBolts.Count == 0)
            {
                ShowSilentCaps();
                tutorialController?.OnBoardReady(currentLevelNumber);
                isLevelEntryPlaying = false;
                inputLocked = false;
                uiManager?.RefreshActionButtonStates();
                yield break;
            }

            int boltOrdinal = 0;
            int concurrentBoltLimit = animateBoltsInParallel
                ? Mathf.Clamp(maximumConcurrentBoltEntrySequences, 1, 4) : 1;
            float boltBatchDuration = boltEntryDuration + (concurrentBoltLimit - 1) * boltEntryStagger;

            foreach (BoltView bolt in levelManager.ActiveBolts)
            {
                if (bolt == null) continue;
                Transform boltTransform = bolt.transform;
                DOTween.Kill(boltTransform, false);

                Vector3 finalBoltPosition = boltTransform.localPosition;
                Quaternion finalBoltRotation = boltTransform.localRotation;
                Vector3 finalBoltScale = bolt.RestingLocalScale;
                int row = GetEntryRowIndex(bolt);
                int batch = boltOrdinal / concurrentBoltLimit;
                int batchSlot = boltOrdinal % concurrentBoltLimit;
                float boltStart = batch * boltBatchDuration + batchSlot * boltEntryStagger + row * rowEntryDelay;

                boltTransform.localPosition = finalBoltPosition + boltEntryOffset;
                boltTransform.localRotation = finalBoltRotation;
                boltTransform.localScale = finalBoltScale * boltEntryStartScale;

                activeBoltEntryCount++;
                Sequence boltEntry = DOTween.Sequence().SetTarget(boltTransform).SetDelay(boltStart);
                boltEntry.Join(boltTransform.DOLocalMove(finalBoltPosition, boltEntryDuration).SetEase(Ease.OutCubic));
                boltEntry.Join(boltTransform.DOScale(finalBoltScale, boltEntryDuration).SetEase(Ease.OutBack));
                boltEntry.OnComplete(() =>
                {
                    if (version != entryGenerationVersion) return;
                    boltTransform.localPosition = finalBoltPosition;
                    boltTransform.localRotation = finalBoltRotation;
                    boltTransform.localScale = finalBoltScale;
                    activeBoltEntryCount--;
                    entrySequences.Remove(boltEntry);
                });
                entrySequences.Add(boltEntry);

                float nutStartBase = boltStart + boltEntryDuration * .70f;
                float safeNutStagger = GetSafeNutEntryStagger(bolt);
                for (int i = 0; i < bolt.Nuts.Count; i++)
                {
                    NutView nut = bolt.Nuts[i];
                    if (nut == null) continue;

                    Transform nutTransform = nut.transform;
                    DOTween.Kill(nutTransform, false);
                    nutTransform.SetParent(bolt.NutContainer, false);
                    nutTransform.localPosition = bolt.GetStackPosition(i) + Vector3.up * nutEntryHeight;
                    nutTransform.localRotation = nut.RestingLocalRotation;
                    nut.RotatingNutVisual.localRotation = nut.RestingVisualLocalRotation * Quaternion.Euler(0f, nutEntryRotation, 0f);
                    nutTransform.localScale = nut.RestingLocalScale * nutEntryStartScale;
                    nut.gameObject.SetActive(false);

                    int nutIndex = i;
                    float nutStart = nutStartBase + nutIndex * safeNutStagger;
                    if (!allowParallelNutEntryAcrossBolts)
                        nutStart += boltOrdinal * (nutEntryDuration + nutSettleDuration + safeNutStagger * 3f);

                    activeNutEntryCount++;
                    Sequence nutEntry = DOTween.Sequence().SetTarget(nutTransform).SetDelay(nutStart);
                    nutEntry.AppendCallback(() => nut.gameObject.SetActive(true));
                    nutEntry.Append(nutTransform.DOLocalMove(bolt.GetStackPosition(nutIndex), nutEntryDuration).SetEase(Ease.OutCubic));
                    nutEntry.Join(nut.RotatingNutVisual.DOLocalRotateQuaternion(nut.RestingVisualLocalRotation, nutEntryDuration).SetEase(Ease.OutCubic));
                    nutEntry.Join(nutTransform.DOScale(nut.RestingLocalScale, nutEntryDuration * .70f).SetEase(Ease.OutCubic));
                    nutEntry.Append(nutTransform.DOScale(nut.RestingLocalScale * nutSettleScale, nutSettleDuration * .45f).SetEase(Ease.OutQuad));
                    nutEntry.Append(nutTransform.DOScale(nut.RestingLocalScale, nutSettleDuration * .55f).SetEase(Ease.OutBack));
                    nutEntry.OnComplete(() =>
                    {
                        if (version != entryGenerationVersion) return;
                        SnapNutToStack(bolt, nut, nutIndex);
                        activeNutEntryCount--;
                        entrySequences.Remove(nutEntry);
                    });
                    entrySequences.Add(nutEntry);
                }

                boltOrdinal++;
            }

            while (version == entryGenerationVersion && (activeBoltEntryCount > 0 || activeNutEntryCount > 0))
                yield return null;

            if (version != entryGenerationVersion) yield break;

            ShowSilentCaps();
            tutorialController?.OnBoardReady(currentLevelNumber);
            isLevelEntryPlaying = false;
            inputLocked = false;
            uiManager?.RefreshActionButtonStates();
        }

        private float GetSafeNutEntryStagger(BoltView bolt)
        {
            float configured = Mathf.Clamp(nutEntryStagger, .05f, .09f);
            if (!useClearanceBasedNutStagger || bolt == null || bolt.Nuts.Count < 2) return configured;

            float slotSpacing = Mathf.Abs(bolt.GetStackPosition(1).y - bolt.GetStackPosition(0).y);
            float entrySpeed = Mathf.Max(.01f, nutEntryHeight / Mathf.Max(.01f, nutEntryDuration));
            return Mathf.Max(configured, slotSpacing / entrySpeed);
        }

        private int GetEntryRowIndex(BoltView bolt)
        {
            if (bolt == null || levelManager == null) return 0;
            float maxZ = float.NegativeInfinity;
            foreach (BoltView candidate in levelManager.ActiveBolts)
                if (candidate != null) maxZ = Mathf.Max(maxZ, candidate.transform.localPosition.z);
            float spacing = Mathf.Max(.01f, levelManager.GridLayoutSettings.RowDepthSpacing);
            return Mathf.Max(0, Mathf.RoundToInt((maxZ - bolt.transform.localPosition.z) / spacing));
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

            Color capColor = GetTopNutColor(bolt);
            Transform capTr = bolt.CompletionCapTransform;
            Vector3 capScale = bolt.CompletionCapRestingLocalScale;
            Vector3 restPos = bolt.GetCapWorldPosition();
            Quaternion restRotation = capTr.localRotation;
            int nutCount = bolt.Nuts.Count;

            if (capSequences.TryGetValue(bolt, out Sequence prev) && prev != null && prev.IsActive())
                prev.Kill(false);

            bolt.DeactivateCap();
            Sequence completionSeq = DOTween.Sequence().SetTarget(capTr);
            completionSeq.AppendCallback(() => OnCompletionWaveStarted?.Invoke(bolt));

            for (int index = 0; index < nutCount; index++)
            {
                NutView nut = bolt.Nuts[index];
                if (nut == null) continue;
                completionSeq.Insert(index * nutWaveStagger, CreateNutWavePulse(bolt, nut, index, capColor));
            }

            float waveDuration = (nutCount - 1) * nutWaveStagger + nutWavePulseDuration;
            float capStartTime = Mathf.Clamp(waveDuration * capStartProgress, 0f, waveDuration);
            completionSeq.InsertCallback((nutCount - 1) * nutWaveStagger, () => OnCompletionWaveReachedTop?.Invoke(bolt));
            completionSeq.InsertCallback(capStartTime, () =>
            {
                bolt.ActivateCap(capColor);
                capTr.position = restPos + Vector3.up * capStartHeight;
                capTr.localScale = capScale * capStartScale;
                capTr.localRotation = restRotation * Quaternion.Euler(0f, capRotationAmount, 0f);
            });

            Sequence capClose = DOTween.Sequence();
            capClose.Append(capTr.DOMove(restPos, capDropDuration).SetEase(Ease.OutCubic));
            capClose.Join(capTr.DOLocalRotateQuaternion(restRotation, capDropDuration).SetEase(Ease.OutCubic));
            capClose.Join(capTr.DOScale(capScale * 1.02f, capDropDuration).SetEase(Ease.OutCubic));
            capClose.Append(capTr.DOScale(Vector3.Scale(capScale, capImpactScale), capImpactDuration).SetEase(Ease.OutQuad));
            capClose.Append(capTr.DOScale(capScale, capSettleDuration).SetEase(Ease.OutBack));
            completionSeq.Insert(capStartTime, capClose);

            float finalPulseStart = capStartTime + capDropDuration + capImpactDuration + capSettleDuration;
            for (int index = 0; index < nutCount; index++)
            {
                NutView nut = bolt.Nuts[index];
                if (nut == null) continue;
                completionSeq.Insert(finalPulseStart, CreateFinalStackPulse(nut));
            }
            completionSeq.InsertCallback(finalPulseStart, () => OnCompletionCapClosed?.Invoke(bolt));
            completionSeq.Insert(finalPulseStart + finalStackPulseDuration,
                DOTween.Sequence().AppendInterval(winDelayAfterCompletion));
            completionSeq.OnComplete(() =>
            {
                foreach (NutView nut in bolt.Nuts)
                {
                    if (nut == null) continue;
                    int index = bolt.Nuts.IndexOf(nut);
                    SnapNutToStack(bolt, nut, index);
                    nut.SetCompletionGlow(Color.black, 0f);
                }
                capTr.position = restPos;
                capTr.localRotation = restRotation;
                capTr.localScale = capScale;
                capSequences.Remove(bolt);
            });

            capSequences[bolt] = completionSeq;
            yield return completionSeq.WaitForCompletion();
        }

        private Sequence CreateNutWavePulse(BoltView bolt, NutView nut, int index, Color completedColor)
        {
            Transform tr = nut.transform;
            Vector3 slot = bolt.GetStackPosition(index);
            Vector3 scale = nut.RestingLocalScale;
            float firstPart = nutWavePulseDuration * .40f;
            float compressionPart = nutWavePulseDuration * .25f;
            float recoverPart = nutWavePulseDuration - firstPart - compressionPart;
            Sequence pulse = DOTween.Sequence();
            pulse.Append(tr.DOLocalMove(slot + Vector3.up * nutWaveLiftAmount, firstPart).SetEase(Ease.OutQuad));
            pulse.Join(tr.DOScale(scale * nutWavePulseScale, firstPart).SetEase(Ease.OutQuad));
            pulse.Join(DOTween.To(() => 0f, value => nut.SetCompletionGlow(completedColor, value), nutWaveGlowStrength, firstPart));
            pulse.Append(tr.DOLocalMove(slot, compressionPart).SetEase(Ease.InQuad));
            pulse.Join(tr.DOScale(scale * nutWaveCompressionScale, compressionPart).SetEase(Ease.InQuad));
            pulse.Join(DOTween.To(() => nutWaveGlowStrength, value => nut.SetCompletionGlow(completedColor, value), 0f, compressionPart));
            pulse.Append(tr.DOScale(scale, recoverPart).SetEase(Ease.OutQuad));
            return pulse;
        }

        private Sequence CreateFinalStackPulse(NutView nut)
        {
            Transform tr = nut.transform;
            Vector3 scale = nut.RestingLocalScale;
            Sequence pulse = DOTween.Sequence();
            pulse.Append(tr.DOScale(scale * finalStackPulseScale, finalStackPulseDuration * .45f).SetEase(Ease.OutQuad));
            pulse.Append(tr.DOScale(scale, finalStackPulseDuration * .55f).SetEase(Ease.OutBack));
            return pulse;
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

        /// <summary>
        /// Appends the existing transfer path's landing phase. The nut stays parented to the
        /// destination while it visibly screws down, then is snapped only to remove final-frame drift.
        /// </summary>
        private void AppendThreadedLanding(Sequence nutSequence, NutView nut, BoltView destination,
                                           int destinationIndex, bool isFinalNut)
        {
            if (nutSequence == null || nut == null || destination == null) return;

            Transform tr = nut.transform;
            Transform visual = nut.RotatingNutVisual;
            Vector3 destinationWorld = destination.NutContainer.TransformPoint(destination.GetStackPosition(destinationIndex));

            nutSequence.AppendCallback(() =>
            {
                tr.SetParent(destination.NutContainer, worldPositionStays: true);
                if (useLandingRotationVariation && landingRotationVariation > 0f)
                    visual.Rotate(Vector3.up, UnityEngine.Random.Range(-landingRotationVariation, landingRotationVariation), Space.World);
            });

            // A tiny beat makes the transition from horizontal travel into threading legible.
            nutSequence.AppendInterval(.03f);

            // The horizontal phase always ends at this authored hover point, so calculate
            // thread amount from that known approach height rather than the current source position.
            float dropDistance = Mathf.Abs(destination.GetHoverWorldPosition().y - destinationWorld.y);
            float threadedDuration = Mathf.Clamp(dropDuration + Mathf.Max(0f, dropDistance - .5f) * .02f, .20f, .28f);
            float compressionStart = threadedDuration * .82f;
            float compressionDuration = threadedDuration - compressionStart;
            float xzCompression = 1f + (1f - landingCompression) * .5f;
            Vector3 compressedScale = Vector3.Scale(nut.RestingLocalScale,
                new Vector3(xzCompression, landingCompression, xzCompression));
            float threadDegrees = dropDistance * threadRotationMultiplier;

            Sequence threadDown = DOTween.Sequence();
            threadDown.Append(tr.DOMove(destinationWorld, threadedDuration).SetEase(dropEase));
            threadDown.Join(visual.DORotate(Vector3.down * threadDegrees, threadedDuration,
                RotateMode.WorldAxisAdd).SetEase(dropEase));
            threadDown.Insert(compressionStart,
                tr.DOScale(compressedScale, compressionDuration).SetEase(Ease.InQuad));
            nutSequence.Append(threadDown);

            nutSequence.AppendCallback(() =>
            {
                // Position and rotation snap only after the complete visible descent.
                SnapNutPositionAndRotation(destination, nut, destinationIndex);
                PulseBoltAttach(destination);
                OnNutLanded?.Invoke(nut, destination);
            });

            float impactScale = landingBounceScale * (isFinalNut ? finalNutImpactMultiplier : 1f);
            nutSequence.Append(tr.DOScale(nut.RestingLocalScale * impactScale, landingImpactDuration).SetEase(Ease.OutQuad));
            nutSequence.Append(tr.DOScale(nut.RestingLocalScale, landingRecoveryDuration).SetEase(Ease.OutBack));
            nutSequence.AppendCallback(() => SnapNutToStack(destination, nut, destinationIndex));
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
            PulseBoltScale(bolt,
                new Vector3(1f, Mathf.Clamp(boltContactCompression, .97f, 1f), 1f),
                Mathf.Clamp(boltContactDuration, .06f, .10f));

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

            NutView top = bolt.Nuts[bolt.Nuts.Count - 1];
            if (top == null || top.IsHidden) return group;

            NutColor color = top.Color;
            for (int i = bolt.Nuts.Count - 1; i >= 0; i--)
            {
                NutView nut = bolt.Nuts[i];
                // An unrevealed nut is an unknown state, not a color match.
                if (nut == null || nut.IsHidden || nut.Color != color) break;
                group.Insert(0, nut);
            }
            return group;
        }

        private bool CanSelect(BoltView bolt) =>
            bolt != null && !IsBoltBusy(bolt) && !bolt.IsLocked && bolt.Nuts.Count > 0;

        private int GetMoveCount(BoltView destination, List<NutView> matchingGroup)
        {
            if (destination == null || matchingGroup == null || matchingGroup.Count == 0 || destination.IsLocked)
                return 0;

            // Use ExpandableBoltController capacity if present.
            int effectiveCapacity = GetEffectiveCapacity(destination);
            int availableSpaces   = effectiveCapacity - destination.Nuts.Count;
            if (availableSpaces <= 0) return 0;

            NutColor movingColor = matchingGroup[matchingGroup.Count - 1].Color;
            if (destination.Nuts.Count > 0)
            {
                NutView destinationTop = destination.Nuts[destination.Nuts.Count - 1];
                // A hidden top nut must reveal before the destination can match a color.
                if (destinationTop == null || destinationTop.IsHidden || destinationTop.Color != movingColor)
                    return 0;
            }

            return Mathf.Min(matchingGroup.Count, availableSpaces);
        }

        /// <summary>Returns effective capacity, respecting ExpandableBoltController if present.</summary>
        private static int GetEffectiveCapacity(BoltView bolt)
        {
            if (bolt == null) return 0;
            var exp = bolt.GetComponent<ExpandableBoltController>();
            return exp != null ? exp.CurrentCapacity : BoltView.Capacity;
        }

        private void ResetActionUses()
        {
            remainingUndoUses = Mathf.Max(0, startingUndoUses);
            remainingExpandUses = Mathf.Max(0, startingExpandUses);
            isUndoAdRequestActive = false;
            isExpandAdRequestActive = false;
        }

        private IEnumerator SimulateUndoRewardAd()
        {
            yield return new WaitForSeconds(.15f);
            OnUndoRewardAdSucceeded();
        }

        private IEnumerator SimulateExpandRewardAd()
        {
            yield return new WaitForSeconds(.15f);
            OnExpandRewardAdSucceeded();
        }

        private bool HasAvailableExpandableBolt()
        {
            if (levelManager == null) return false;

            foreach (BoltView bolt in levelManager.ActiveBolts)
            {
                if (bolt == null || IsBoltBusy(bolt)) continue;
                var expandable = bolt.GetComponent<ExpandableBoltController>();
                if (expandable != null && !expandable.IsAtMax && !expandable.IsExpanding)
                    return true;
            }
            return false;
        }

        private bool HasExpandableBoltBelowMax()
        {
            if (levelManager == null) return false;

            foreach (BoltView bolt in levelManager.ActiveBolts)
            {
                var expandable = bolt != null ? bolt.GetComponent<ExpandableBoltController>() : null;
                if (expandable != null && !expandable.IsAtMax) return true;
            }
            return false;
        }

        private IEnumerator RefreshActionButtonsAfterExpansion(ExpandableBoltController expandable)
        {
            while (expandable != null && expandable.IsExpanding)
                yield return null;
            uiManager?.RefreshActionButtonStates();
        }

        private bool CanMove(BoltView from, BoltView to, List<NutView> moving) =>
            from != null && !IsBoltBusy(from) && !IsBoltBusy(to) && GetMoveCount(to, moving) > 0;

        private bool IsBoltBusy(BoltView bolt) => bolt != null && busyBolts.Contains(bolt);

        private IEnumerator RevealNewTopIfHidden(BoltView bolt)
        {
            if (bolt == null || bolt.Nuts.Count == 0) yield break;
            NutView top = bolt.Nuts[bolt.Nuts.Count - 1];
            if (top == null || !top.IsHidden || top.IsRevealing) yield break;
            Sequence reveal = top.Reveal();
            if (reveal != null) yield return reveal.WaitForCompletion();
        }

        private void CheckWin()
        {
            if (won || activeTransferCount > 0 || levelManager == null) return;
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
            CancelLevelEntry();
            KillGameplayTweens();
            foreach (Sequence moveSequence in activeMoveSequences)
                if (moveSequence != null && moveSequence.IsActive()) moveSequence.Kill(false);
            activeMoveSequences.Clear();
            pendingMoveRecords.Clear();
            foreach (var kv in capSequences)
                if (kv.Value != null && kv.Value.IsActive()) kv.Value.Kill(false);
            capSequences.Clear();
            if (levelManager != null)
                foreach (BoltView bolt in levelManager.ActiveBolts)
                {
                    if (bolt == null) continue;
                    for (int index = 0; index < bolt.Nuts.Count; index++)
                    {
                        NutView nut = bolt.Nuts[index];
                        if (nut == null) continue;
                        nut.CancelReveal();
                        nut.SetCompletionGlow(Color.black, 0f);
                        RestoreNutToStack(bolt, nut, index);
                    }
                    bolt.DeactivateCap();
                }
        }

        private void CancelLevelEntry()
        {
            entryGenerationVersion++;
            foreach (Sequence sequence in entrySequences)
                if (sequence != null && sequence.IsActive()) sequence.Kill(false);
            entrySequences.Clear();
            activeBoltEntryCount = 0;
            activeNutEntryCount = 0;
            isLevelEntryPlaying = false;
        }

        private static void KillNutTween(NutView nut)
        {
            if (nut == null) return;
            DOTween.Kill(nut.transform, false);
            DOTween.Kill(nut.RotatingNutVisual, false);
        }

        private void ClearSelection()
        {
            StopHover();
            if (selectedBolt != null) selectedBolt.SetSelectionEffect(false);
            selectedBolt = null;
            selectedNuts.Clear();
            liftedNut = null;
        }

        [ContextMenu("Reveal All Hidden Nuts")]
        private void RevealAllHiddenNuts()
        {
            if (levelManager == null) return;
            foreach (BoltView bolt in levelManager.ActiveBolts)
                foreach (NutView nut in bolt.Nuts) nut?.RevealSilently();
        }

        [ContextMenu("Reset Hidden Nuts")]
        private void ResetHiddenNuts()
        {
            if (levelManager == null) return;
            foreach (BoltView bolt in levelManager.ActiveBolts)
            {
                foreach (NutView nut in bolt.Nuts) nut?.ResetHiddenToStart();
                if (bolt.Nuts.Count > 0) bolt.Nuts[bolt.Nuts.Count - 1].RevealSilently();
            }
        }

        [ContextMenu("Print Hidden Nut Data")]
        private void PrintHiddenNutData()
        {
            if (levelManager == null) return;
            foreach (BoltView bolt in levelManager.ActiveBolts)
                for (int i = 0; i < bolt.Nuts.Count; i++)
                {
                    NutView nut = bolt.Nuts[i];
                    Debug.Log($"[HiddenNut] Bolt {bolt.BoltIndex}, slot {i}: color={nut.Color}, startsHidden={nut.StartsHidden}, revealed={nut.IsRevealed}");
                }
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
            SnapNutPositionAndRotation(bolt, nut, index);
            Transform tr = nut.transform;
            tr.localScale    = nut.RestingLocalScale;
        }

        private static void SnapNutPositionAndRotation(BoltView bolt, NutView nut, int index)
        {
            if (bolt == null || nut == null) return;
            Transform tr = nut.transform;
            if (tr.parent != bolt.NutContainer) tr.SetParent(bolt.NutContainer, false);
            tr.localPosition = bolt.GetStackPosition(index);
            tr.localRotation = nut.RestingLocalRotation;
            nut.RotatingNutVisual.localRotation = nut.RestingVisualLocalRotation;
            nut.OrientQuestionMarkToCamera();
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
            if (GUI.Button(new Rect(50,  50, 190, 68), "↻  RESTART",              style) && !inputLocked && activeTransferCount == 0) RestartLevel();
            if (GUI.Button(new Rect(840, 50, 190, 68), "LEVEL " + currentLevelNumber, style) && !inputLocked && activeTransferCount == 0) LoadNextLevel();
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
