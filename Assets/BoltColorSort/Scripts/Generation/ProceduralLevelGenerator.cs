using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NutBoltSort
{
    // ─────────────────────────────────────────────────────────────────────────
    // Supporting enums and data types
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Broad difficulty band used by the wave pattern and template filter.</summary>
    public enum DifficultyRating { Recovery, Easy, Medium, Hard, Challenge }

    /// <summary>
    /// Defines the composition and quality thresholds for a family of procedurally
    /// generated puzzles. All counts refer to logical bolt positions.
    /// </summary>
    [Serializable]
    public class LevelGenerationTemplate
    {
        [Tooltip("Short unique identifier used in logs and the puzzle signature.")]
        public string templateId = "unnamed";

        [Tooltip("Human-readable description shown in the Inspector.")]
        public string description = "";

        [Tooltip("First level number this template may be chosen for (1-based, inclusive).")]
        [Min(1)] public int minimumLevel = 6;

        [Tooltip("Last level number this template may be chosen for (1-based, inclusive). Set to " + nameof(int.MaxValue) + " for endless.")]
        public int maximumLevel = int.MaxValue;

        [Tooltip("Broad difficulty band. Matched against the current wave-pattern position.")]
        public DifficultyRating difficulty = DifficultyRating.Medium;

        // ── Board composition ──────────────────────────────────────────────
        [Min(2)] public int activeColorCount       = 4;
        [Min(1)] public int filledBoltCount        = 4;
        [Min(0)] public int normalEmptyBoltCount   = 2;
        [Min(0)] public int lockedBoltCount        = 0;
        [Min(0)] public int expandableBoltCount    = 0;

        [Tooltip("Starting capacity for each expandable bolt (0 = fully covered).")]
        [Range(0, 4)] public int expandableStartingCapacity = 0;

        [Tooltip("When true the puzzle can be fully solved without unlocking. Unlocking only makes it easier.")]
        public bool lockedBoltOptional = true;

        // ── Quality thresholds ────────────────────────────────────────────
        [Min(8)]  public int targetInverseSteps              = 24;
        [Min(4)]  public int minimumAcceptedSteps            = 12;
        [Min(0)]  public int minimumMixedBolts               = 2;
        [Min(0)]  public int minimumColorTransitions         = 3;
        [Min(1)]  public int minimumStartingLegalMoves       = 1;
        [Min(0)]  public int maximumStartingLegalMoves       = 0; // 0 = unlimited
        [Min(0)]  public int maximumCompletedBoltsAtStart    = 1;
        [Min(4)]  public int minimumGuaranteedSolutionLength = 12;
        [Tooltip("0 disables this lower difficulty-score bound.")] public float minimumDifficultyScore;
        [Tooltip("0 disables this upper difficulty-score bound.")] public float maximumDifficultyScore;

        // ── Selection ─────────────────────────────────────────────────────
        [Tooltip("Relative probability weight. Higher values are selected more often.")]
        [Min(1)] public int selectionWeight = 1;

        // ── Computed helpers ──────────────────────────────────────────────
        public int TotalBoltCount => filledBoltCount + normalEmptyBoltCount + lockedBoltCount + expandableBoltCount;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Legacy tier (kept for backward-compat when useTemplateSystem = false)
    // ─────────────────────────────────────────────────────────────────────────
    [Serializable]
    public class DifficultyTier
    {
        public string name = "Beginner";
        [Min(1)] public int minLevel = 1;
        [Min(1)] public int maxLevel = 5;
        [Min(2)] public int activeColorCount = 3;
        [Min(1)] public int emptyBoltCount = 2;
        [Min(1)] public int targetInverseSteps = 24;
        [Min(1)] public int minimumAcceptedSteps = 12;
        [Min(0)] public int minimumMixedBolts = 2;
        [Min(0)] public int minimumColorTransitions = 3;
        [Min(0)] public int maximumCompletedBoltsAtStart = 1;
        [Min(1)] public int minimumGuaranteedSolutionLength = 12;
        [Min(1)] public int minimumStartingLegalMoves = 1;
        [Min(0)] public int maximumStartingLegalMoves = 0;
        [Min(1)] public int maxGenerationAttempts = 24;
    }

    public enum GenerationFailure
    {
        None, InvalidConfiguration, NoValidInverseMove, InsufficientAcceptedSteps,
        InvalidColorCount, IncorrectEmptyBoltCount, SolutionReplayFailed, PuzzleAlreadySolved,
        PuzzleOneMoveFromSolved, InsufficientMixedBolts, InsufficientTransitions,
        TooManyCompletedBolts, NoStartingLegalMoves, DuplicateSignature,
        ExceedsMaximumBoardPositions, NoTemplateAvailable, SolverUnsolved, SolverSearchLimitReached, DifficultyTooLow, DifficultyTooHigh
    }

    [Serializable]
    public struct LogicalMove
    {
        public int sourceBoltIndex;
        public int destinationBoltIndex;
        public NutColor color;
        public int count;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ProceduralLevelGenerator
    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Data-only endless puzzle generator.  It deliberately has no knowledge of scene objects,
    /// transforms, animation or layout.  The existing LevelManager receives its LevelDataSO unchanged.
    ///
    /// When <c>useTemplateSystem</c> is true (the default) the generator uses a curated list of
    /// <see cref="LevelGenerationTemplate"/> entries with a configurable difficulty-wave pattern and
    /// board-size plateaus.  The legacy <see cref="DifficultyTier"/> path is still available when
    /// <c>useTemplateSystem</c> is false.
    /// </summary>
    public sealed class ProceduralLevelGenerator : MonoBehaviour
    {
        // ── Template System ────────────────────────────────────────────────────
        [Header("Template System")]
        [Tooltip("When true the generator uses LevelGenerationTemplate entries and the difficulty-wave pattern. " +
                 "StructuredLevelProvider will also disable the legacy interval-based locked-bolt scheduler.")]
        [SerializeField] private bool useTemplateSystem = true;

        [Tooltip("Maximum number of bolt positions that may ever appear on the board.")]
        [SerializeField, Min(2)] private int maximumBoardPositions = 8;

        [Tooltip("Difficulty-wave cycle. The generator cycles through this list indefinitely to pick the " +
                 "target difficulty for each procedural level.")]
        [SerializeField]
        private List<DifficultyRating> difficultyWavePattern = new List<DifficultyRating>
        {
            DifficultyRating.Easy,
            DifficultyRating.Medium,
            DifficultyRating.Medium,
            DifficultyRating.Hard,
            DifficultyRating.Easy,
            DifficultyRating.Medium,
            DifficultyRating.Hard,
            DifficultyRating.Medium,
            DifficultyRating.Challenge,
            DifficultyRating.Recovery
        };

        [Tooltip("Curated template library. Each entry defines the board composition and quality thresholds " +
                 "for a family of procedural levels. Ordered by minimum level for readability.")]
        [SerializeField]
        private List<LevelGenerationTemplate> levelTemplates = new List<LevelGenerationTemplate>
        {
            // Level 6 is deliberately not selected by weight or recent-history rules.
            new LevelGenerationTemplate {
                templateId = "level_6_safe_standard", description = "Fixed procedural introduction: 4 filled + 2 empty",
                minimumLevel = 6, maximumLevel = 6, difficulty = DifficultyRating.Recovery,
                activeColorCount = 4, filledBoltCount = 4, normalEmptyBoltCount = 2,
                lockedBoltCount = 0, expandableBoltCount = 0,
                targetInverseSteps = 16, minimumAcceptedSteps = 8, minimumMixedBolts = 2, minimumColorTransitions = 3,
                minimumGuaranteedSolutionLength = 8, maximumCompletedBoltsAtStart = 0, selectionWeight = 1 },
            // ── Levels 6–10: recovery / standard easy ────────────────────────────
            new LevelGenerationTemplate {
                templateId = "recovery_standard", description = "4 filled + 2 empty – easy recovery",
                minimumLevel = 7, maximumLevel = 30, difficulty = DifficultyRating.Recovery,
                activeColorCount = 4, filledBoltCount = 4, normalEmptyBoltCount = 2,
                lockedBoltCount = 0, expandableBoltCount = 0,
                targetInverseSteps = 18, minimumAcceptedSteps = 10, minimumMixedBolts = 2, minimumColorTransitions = 3,
                minimumGuaranteedSolutionLength = 10, maximumCompletedBoltsAtStart = 1, selectionWeight = 3 },

            new LevelGenerationTemplate {
                templateId = "standard_medium", description = "4 filled + 2 empty – moderate mixing",
                minimumLevel = 6, maximumLevel = 50, difficulty = DifficultyRating.Medium,
                activeColorCount = 4, filledBoltCount = 4, normalEmptyBoltCount = 2,
                lockedBoltCount = 0, expandableBoltCount = 0,
                targetInverseSteps = 32, minimumAcceptedSteps = 18, minimumMixedBolts = 3, minimumColorTransitions = 5,
                minimumGuaranteedSolutionLength = 18, maximumCompletedBoltsAtStart = 0, selectionWeight = 4 },

            // ── Optional locked bolt (appears every 3–5 levels via wave) ─────────
            new LevelGenerationTemplate {
                templateId = "optional_locked", description = "4 filled + 2 empty + 1 locked (optional) – medium",
                minimumLevel = 8, maximumLevel = 100, difficulty = DifficultyRating.Medium,
                activeColorCount = 4, filledBoltCount = 4, normalEmptyBoltCount = 2,
                lockedBoltCount = 1, expandableBoltCount = 0, lockedBoltOptional = true,
                targetInverseSteps = 28, minimumAcceptedSteps = 16, minimumMixedBolts = 2, minimumColorTransitions = 4,
                minimumGuaranteedSolutionLength = 16, maximumCompletedBoltsAtStart = 0, selectionWeight = 2 },

            // ── Locked challenge ──────────────────────────────────────────────────
            new LevelGenerationTemplate {
                templateId = "locked_challenge", description = "4 filled + 1 empty + 1 locked – hard",
                minimumLevel = 10, maximumLevel = int.MaxValue, difficulty = DifficultyRating.Hard,
                activeColorCount = 4, filledBoltCount = 4, normalEmptyBoltCount = 1,
                lockedBoltCount = 1, expandableBoltCount = 0, lockedBoltOptional = true,
                targetInverseSteps = 40, minimumAcceptedSteps = 22, minimumMixedBolts = 3, minimumColorTransitions = 6,
                minimumGuaranteedSolutionLength = 22, maximumCompletedBoltsAtStart = 0, selectionWeight = 2 },

            // ── Expandable bolt ───────────────────────────────────────────────────
            new LevelGenerationTemplate {
                templateId = "expandable_medium", description = "4 filled + 1 empty + 1 expandable – medium",
                minimumLevel = 11, maximumLevel = int.MaxValue, difficulty = DifficultyRating.Medium,
                activeColorCount = 4, filledBoltCount = 4, normalEmptyBoltCount = 2,
                lockedBoltCount = 0, expandableBoltCount = 1, expandableStartingCapacity = 0,
                targetInverseSteps = 28, minimumAcceptedSteps = 16, minimumMixedBolts = 2, minimumColorTransitions = 4,
                minimumGuaranteedSolutionLength = 16, maximumCompletedBoltsAtStart = 0, selectionWeight = 2 },

            // ── 5 filled (bigger board) ───────────────────────────────────────────
            new LevelGenerationTemplate {
                templateId = "five_filled_medium", description = "5 filled + 2 empty – medium (7 total)",
                minimumLevel = 11, maximumLevel = int.MaxValue, difficulty = DifficultyRating.Medium,
                activeColorCount = 5, filledBoltCount = 5, normalEmptyBoltCount = 2,
                lockedBoltCount = 0, expandableBoltCount = 0,
                targetInverseSteps = 40, minimumAcceptedSteps = 22, minimumMixedBolts = 3, minimumColorTransitions = 6,
                minimumGuaranteedSolutionLength = 22, maximumCompletedBoltsAtStart = 0, selectionWeight = 3 },

            new LevelGenerationTemplate {
                templateId = "five_filled_hard", description = "5 filled + 2 empty – hard (7 total)",
                minimumLevel = 21, maximumLevel = int.MaxValue, difficulty = DifficultyRating.Hard,
                activeColorCount = 5, filledBoltCount = 5, normalEmptyBoltCount = 2,
                lockedBoltCount = 0, expandableBoltCount = 0,
                targetInverseSteps = 52, minimumAcceptedSteps = 30, minimumMixedBolts = 4, minimumColorTransitions = 8,
                minimumGuaranteedSolutionLength = 30, maximumCompletedBoltsAtStart = 0, selectionWeight = 2 },

            // ── 5 filled + locked (max 8 total) ──────────────────────────────────
            new LevelGenerationTemplate {
                templateId = "five_locked_hard", description = "5 filled + 2 empty + 1 locked – hard (8 total)",
                minimumLevel = 21, maximumLevel = int.MaxValue, difficulty = DifficultyRating.Hard,
                activeColorCount = 5, filledBoltCount = 5, normalEmptyBoltCount = 2,
                lockedBoltCount = 1, expandableBoltCount = 0, lockedBoltOptional = true,
                targetInverseSteps = 56, minimumAcceptedSteps = 30, minimumMixedBolts = 4, minimumColorTransitions = 8,
                minimumGuaranteedSolutionLength = 30, maximumCompletedBoltsAtStart = 0, selectionWeight = 2 },

            // ── High mixing (5 colours) ───────────────────────────────────────────
            new LevelGenerationTemplate {
                templateId = "high_mixing_5col", description = "4 filled + 2 empty – 5 colours, hard",
                minimumLevel = 21, maximumLevel = int.MaxValue, difficulty = DifficultyRating.Hard,
                activeColorCount = 5, filledBoltCount = 5, normalEmptyBoltCount = 2,
                lockedBoltCount = 0, expandableBoltCount = 0,
                targetInverseSteps = 48, minimumAcceptedSteps = 28, minimumMixedBolts = 3, minimumColorTransitions = 8,
                minimumGuaranteedSolutionLength = 28, maximumCompletedBoltsAtStart = 0, selectionWeight = 2 },

            // ── Challenge (expandable + locked) ──────────────────────────────────
            new LevelGenerationTemplate {
                templateId = "challenge_combined_disabled", description = "Disabled until the logical solver supports special actions",
                minimumLevel = 51, maximumLevel = 50, difficulty = DifficultyRating.Challenge,
                activeColorCount = 5, filledBoltCount = 5, normalEmptyBoltCount = 1,
                lockedBoltCount = 1, expandableBoltCount = 1, lockedBoltOptional = true, expandableStartingCapacity = 0,
                targetInverseSteps = 60, minimumAcceptedSteps = 34, minimumMixedBolts = 4, minimumColorTransitions = 10,
                minimumGuaranteedSolutionLength = 34, maximumCompletedBoltsAtStart = 0, selectionWeight = 1 },

            // ── Expert standard ───────────────────────────────────────────────────
            new LevelGenerationTemplate {
                templateId = "expert_standard", description = "5 filled + 2 empty – 5 colours, expert",
                minimumLevel = 51, maximumLevel = int.MaxValue, difficulty = DifficultyRating.Hard,
                activeColorCount = 5, filledBoltCount = 5, normalEmptyBoltCount = 2,
                lockedBoltCount = 0, expandableBoltCount = 0,
                targetInverseSteps = 64, minimumAcceptedSteps = 36, minimumMixedBolts = 4, minimumColorTransitions = 10,
                minimumGuaranteedSolutionLength = 36, maximumCompletedBoltsAtStart = 0, selectionWeight = 2 },

            // ── Endless safe fallback (covers every level) ────────────────────────
            new LevelGenerationTemplate {
                templateId = "safe_standard_fallback", description = "4 filled + 2 empty – safe fallback for any level",
                minimumLevel = 6, maximumLevel = int.MaxValue, difficulty = DifficultyRating.Recovery,
                activeColorCount = 4, filledBoltCount = 4, normalEmptyBoltCount = 2,
                lockedBoltCount = 0, expandableBoltCount = 0,
                targetInverseSteps = 18, minimumAcceptedSteps = 8, minimumMixedBolts = 1, minimumColorTransitions = 2,
                minimumGuaranteedSolutionLength = 8, maximumCompletedBoltsAtStart = 1, selectionWeight = 1 },
        };

        [Tooltip("How many recently used template IDs to remember (prevents the same template repeating too often).")]
        [SerializeField, Range(1, 20)] private int recentTemplateHistorySize = 5;

        // ── Legacy tiers (kept for useTemplateSystem = false) ─────────────────
        [Header("Legacy Difficulty Tiers (used when useTemplateSystem = false)")]
        [SerializeField] private List<DifficultyTier> difficultyTiers = new List<DifficultyTier>
        {
            new DifficultyTier { name = "Levels 1-5",  minLevel = 1, maxLevel = 5,      activeColorCount = 3, targetInverseSteps = 24, minimumAcceptedSteps = 12, minimumMixedBolts = 2, minimumColorTransitions = 3, minimumGuaranteedSolutionLength = 12 },
            new DifficultyTier { name = "Levels 6+",   minLevel = 6, maxLevel = 999999, activeColorCount = 4, targetInverseSteps = 64, minimumAcceptedSteps = 28, minimumMixedBolts = 3, minimumColorTransitions = 6, maximumCompletedBoltsAtStart = 0, minimumGuaranteedSolutionLength = 28 }
        };

        // ── Generator core settings ────────────────────────────────────────────
        [Header("Generator")]
        [SerializeField, Min(1)] private int generatorVersion = 2;
        [SerializeField, Min(1)] private int startingLevelIndex = 1;

        [Tooltip("Full pool of colours the generator may use. Templates restrict to a subset.")]
        [SerializeField] private List<NutColor> supportedColors = new List<NutColor>
            { NutColor.Red, NutColor.Blue, NutColor.Green, NutColor.Yellow, NutColor.Purple };

        [SerializeField, Min(1)] private int maximumGenerationAttempts = 32;
        [SerializeField, Min(1)] private int maximumInverseMoveAttemptsPerStep = 24;
        [SerializeField, Min(100)] private int maximumSolverNodes = 50000;
        [SerializeField, Min(1)] private int maximumSolverDepth = 96;
        [SerializeField, Min(1)] private int maximumSolverMilliseconds = 150;
        [SerializeField, Range(1, 50)] private int recentSignatureHistorySize = 20;
        [SerializeField] private bool enableDebugLogging = true;
        [SerializeField] private bool useDeterministicTestSeed;
        [SerializeField] private int testSeed = 12345;

        // ── Runtime state ──────────────────────────────────────────────────────
        private readonly Queue<string> recentSignatures = new Queue<string>();
        private readonly Queue<string> recentTemplateIds = new Queue<string>();
        private LevelDataSO currentSnapshot;
        private List<LogicalMove> currentSolution = new List<LogicalMove>();
        private int currentLevelIndex = -1;
        private int currentSeed;

        // ── Public API ─────────────────────────────────────────────────────────
        public int    CurrentSeed              => currentSeed;
        public string CurrentSignature         => currentSnapshot != null ? currentSnapshot.puzzleSignature : string.Empty;
        public IReadOnlyList<LogicalMove> CurrentGuaranteedSolution => currentSolution;

        /// <summary>
        /// True when the template system is active.
        /// <see cref="StructuredLevelProvider"/> uses this to disable the legacy interval scheduler.
        /// </summary>
        public bool UsesTemplateSystem => useTemplateSystem;

        private const int CurrentTemplateSchemaVersion = 4;
        [SerializeField, HideInInspector] private int serializedTemplateSchemaVersion;

        private void Awake() { MigrateTemplateConfigurationIfRequired(); ValidateTemplateConfiguration(); }
        private void OnValidate() { MigrateTemplateConfigurationIfRequired(); ValidateTemplateConfiguration(); }

        // ─────────────────────────────────────────────────────────────────────
        // Core public generation API
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Generates the exact displayed (one-based) procedural level number.</summary>
        public LevelDataSO GetOrGenerateLevelByNumber(int levelNumber, int boltCapacity)
        {
            if (levelNumber < 6)
            {
                Debug.LogError("[ProceduralLevelGenerator] Level " + levelNumber + " is authored; use StructuredLevelProvider.");
                return null;
            }
            if (currentSnapshot != null && currentLevelIndex == levelNumber)
                return currentSnapshot.DeepCopy(); // Restart source of truth.
            return GenerateAndSave(levelNumber, boltCapacity, useDeterministicTestSeed ? testSeed : NewSeed());
        }

        /// <summary>Compatibility wrapper for old callers that passed a zero-based index.</summary>
        [Obsolete("Use GetOrGenerateLevelByNumber with the displayed one-based number.")]
        public LevelDataSO GetOrGenerateCurrentLevel(int requestedIndex, int capacity) =>
            GetOrGenerateLevelByNumber(requestedIndex + 1, capacity);

        public void AdvanceToNextLevel(int requestedIndex)
        {
            currentSnapshot = null;
            currentSolution.Clear();
            currentLevelIndex = -1;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Context-menu developer tools
        // ─────────────────────────────────────────────────────────────────────

        [ContextMenu("Generate New Level")]
        public void GenerateNewLevel()
        {
            int capacity = BoltView.Capacity;
            int level    = currentLevelIndex > 0 ? currentLevelIndex + 1 : startingLevelIndex;
            GenerateAndSave(level, capacity, useDeterministicTestSeed ? testSeed : NewSeed());
        }

        [ContextMenu("Generate Next Level")]
        public void GenerateNextLevel()
        {
            int capacity = BoltView.Capacity;
            int level    = currentLevelIndex > 0 ? currentLevelIndex + 1 : startingLevelIndex;
            GenerateAndSave(level, capacity, useDeterministicTestSeed ? testSeed : NewSeed());
            Debug.Log($"[ProceduralLevelGenerator] Generated next level ({level}): template={currentSnapshot?.difficultyTier}, bolts={currentSnapshot?.bolts.Count}");
        }

        [ContextMenu("Reload Saved Current Level")]
        public void ReloadSavedCurrentLevel()
        {
            var manager = FindAnyObjectByType<LevelManager>();
            if (manager != null && currentSnapshot != null) manager.BuildLevel(currentSnapshot.DeepCopy(), out _);
        }

        [ContextMenu("Generate Using Seed")]
        public void GenerateUsingSeed() => GenerateAndSave(currentLevelIndex > 0 ? currentLevelIndex : startingLevelIndex, BoltView.Capacity, testSeed);

        [ContextMenu("Print Current Seed")]
        public void PrintCurrentSeed() => Debug.Log("[ProceduralLevelGenerator] Seed: " + currentSeed);

        [ContextMenu("Print Puzzle Signature")]
        public void PrintPuzzleSignature() => Debug.Log("[ProceduralLevelGenerator] Signature: " + CurrentSignature);

        [ContextMenu("Validate Current Puzzle")]
        public void ValidateCurrentPuzzle() => Debug.Log("[ProceduralLevelGenerator] " + ValidateSavedCurrent(BoltView.Capacity));

        [ContextMenu("Replay Guaranteed Solution Logically")]
        public void ReplayGuaranteedSolutionLogically() => Debug.Log("[ProceduralLevelGenerator] Replay " + (ReplaySaved(BoltView.Capacity) ? "passed" : "failed"));

        [ContextMenu("Print Board Composition")]
        public void PrintBoardComposition()
        {
            if (currentSnapshot == null) { Debug.Log("[ProceduralLevelGenerator] No current snapshot."); return; }
            var sb = new StringBuilder();
            sb.AppendLine($"[ProceduralLevelGenerator] Board: level={currentSnapshot.levelNumber} tier={currentSnapshot.difficultyTier} bolts={currentSnapshot.bolts.Count}");
            for (int i = 0; i < currentSnapshot.bolts.Count; i++)
            {
                var b = currentSnapshot.bolts[i];
                string nuts = (b.nutColors != null && b.nutColors.Length > 0) ? string.Join(",", b.nutColors) : "(empty)";
                sb.AppendLine($"  [{i}] {b.boltType} | {nuts}");
            }
            Debug.Log(sb.ToString());
        }

        [ContextMenu("Validate Level 5 Composition")]
        public void ValidateLevel5Composition()
        {
            // This tool verifies the batch-generation contract for Level 5 (fixed, not procedural),
            // but exercises the same logical validation path used in batch testing.
            Debug.Log("[ProceduralLevelGenerator] Level 5 is a fixed introductory level managed by " +
                      "StructuredLevelProvider. Its validator logs 'LEVEL 5 VALIDATION' to the Console " +
                      "each time Level 5 loads. Check the Console after loading Level 5 to confirm:\n" +
                      "  Total positions: 7 | Filled: 4 | Normal empty: 2 | Locked: 1 | Expandable: 0\n" +
                      "  Locked index: 6 | Locked is last: True | Total nuts: 16 | Validation: Passed");
        }

        [ContextMenu("Batch Generate 100 Levels")]
        public void BatchGenerate100Levels() => BatchGenerate(100, BoltView.Capacity);

        [ContextMenu("Batch Generate 1000 Levels")]
        public void BatchGenerate1000Levels() => BatchGenerate(1000, BoltView.Capacity);

        [ContextMenu("Validate Generator Configuration")]
        public void ValidateGeneratorConfigurationTool()
        {
            MigrateTemplateConfigurationIfRequired();
            ValidateTemplateConfiguration();
            bool endless = levelTemplates.Exists(t => IsTemplateValid(t, false) && t.maximumLevel == int.MaxValue);
            bool level6 = levelTemplates.Exists(t => IsTemplateValid(t, false) && t.minimumLevel <= 6 && t.maximumLevel >= 6);
            Debug.Log($"[ProceduralLevelGenerator] Configuration: level6Covered={level6}, endlessCovered={endless}, maxBoard={maximumBoardPositions}, supportedColors={supportedColors.Count}, fallback={(GetFallbackTemplate() != null)}");
        }

        [ContextMenu("Print Template Usage Stats")]
        public void PrintTemplateUsageStats()
        {
            if (!useTemplateSystem) { Debug.Log("[ProceduralLevelGenerator] Template system is disabled."); return; }
            var sb = new StringBuilder("[ProceduralLevelGenerator] Available templates:\n");
            foreach (var t in levelTemplates)
            {
                int max = t.maximumLevel == int.MaxValue ? 9999999 : t.maximumLevel;
                sb.AppendLine($"  {t.templateId}: levels {t.minimumLevel}–{max}, difficulty={t.difficulty}, total={t.TotalBoltCount}, weight={t.selectionWeight}");
            }
            Debug.Log(sb.ToString());
        }

        public void BatchGenerate(int count, int capacity)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int ok = 0, fail = 0, duplicates = 0, fallbackUses = 0;
            long slowest = 0;
            int accepted = 0, mixedTotal = 0, transitionTotal = 0, solverNodes = 0, solverTimeouts = 0, invalidPaths = 0, compositionErrors = 0, colorErrors = 0;
            var signatures = new HashSet<string>();
            var failures   = new Dictionary<GenerationFailure, int>();
            var templateUsage = new Dictionary<string, int>();
            var diffDist  = new Dictionary<DifficultyRating, int>();
            var orderErrors = 0;
            var capacityErrors = 0;

            for (int i = 0; i < count; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int targetLevel = i + 6;
                LevelDataSO data; List<LogicalMove> path; GenerationFailure reason;
                bool success = TryGenerateForLevel(targetLevel, capacity, NewSeed(), out data, out path, out reason);
                sw.Stop();
                slowest = Math.Max(slowest, sw.ElapsedMilliseconds);

                if (success)
                {
                    ok++;
                    accepted += path.Count;
                    solverNodes += data.validatedSolverNodes;
                    if (!ReplaySavedPath(data, path, capacity)) invalidPaths++;
                    if (!ValidateBatchComposition(data, capacity)) compositionErrors++;
                    if (!ValidateBatchColorCounts(data, capacity)) colorErrors++;
                    if (!signatures.Add(data.puzzleSignature)) duplicates++;
                    int mixed, transitions; GetMetrics(data, out mixed, out transitions);
                    mixedTotal += mixed; transitionTotal += transitions;

                    // Template tracking
                    string tier = data.proceduralTemplateId ?? data.difficultyTier ?? "unknown";
                    templateUsage[tier] = templateUsage.ContainsKey(tier) ? templateUsage[tier] + 1 : 1;

                    // Difficulty tracking
                    DifficultyRating dr = data.difficultyBand;
                    diffDist[dr] = diffDist.ContainsKey(dr) ? diffDist[dr] + 1 : 1;

                    // Locked-bolt ordering: locked bolts must be at the end
                    bool orderOk = ValidateLockedBoltOrdering(data);
                    if (!orderOk) orderErrors++;

                    // Board capacity check
                    if (data.bolts.Count > maximumBoardPositions) capacityErrors++;
                }
                else
                {
                    fail++;
                    if (reason == GenerationFailure.SolverSearchLimitReached) solverTimeouts++;
                    failures[reason] = failures.ContainsKey(reason) ? failures[reason] + 1 : 1;
                }
            }

            watch.Stop();
            var sb = new StringBuilder();
            sb.AppendLine($"[ProceduralLevelGenerator] Batch {count}:");
            sb.AppendLine($"  Success={ok}  Failures={fail}  Duplicates={duplicates}  FallbackUses={fallbackUses}");
            sb.AppendLine($"  AvgMs={watch.ElapsedMilliseconds / Math.Max(1.0, count):F2}  SlowestMs={slowest}");
            sb.AppendLine($"  AvgSolutionLength={(double)accepted / Math.Max(1, ok):F1}  AvgSolverNodes={(double)solverNodes / Math.Max(1, ok):F1}  SolverTimeouts={solverTimeouts}");
            sb.AppendLine($"  AvgMixedBolts={(double)mixedTotal / Math.Max(1, ok):F1}  AvgTransitions={(double)transitionTotal / Math.Max(1, ok):F1}");
            sb.AppendLine($"  CompositionErrors={compositionErrors} ColorCountErrors={colorErrors} InvalidSolutionPaths={invalidPaths} LockedOrderErrors={orderErrors} CapacityExceededErrors={capacityErrors}");
            sb.Append("  Failures: "); foreach (var kv in failures) sb.Append($"{kv.Key}={kv.Value} "); sb.AppendLine();
            sb.Append("  Templates: "); foreach (var kv in templateUsage) sb.Append($"{kv.Key}={kv.Value} "); sb.AppendLine();
            sb.Append("  Difficulty: "); foreach (var kv in diffDist) sb.Append($"{kv.Key}={kv.Value} ");
            Debug.Log(sb.ToString());
        }

        // ─────────────────────────────────────────────────────────────────────
        // Internal generation pipeline
        // ─────────────────────────────────────────────────────────────────────

        private LevelDataSO GenerateAndSave(int level, int capacity, int seed)
        {
            LevelDataSO data; List<LogicalMove> path; GenerationFailure reason = GenerationFailure.None;
            int candidateSeed = seed;
            for (int retry = 0; retry < maximumGenerationAttempts; retry++, candidateSeed = NextSeed(candidateSeed))
            {
                if (TryGenerateForLevel(level, capacity, candidateSeed, out data, out path, out reason))
                {
                    SaveAccepted(level, candidateSeed, data, path);
                    return currentSnapshot.DeepCopy();
                }
            }

            Debug.LogError($"[ProceduralLevelGenerator] Generation failed at level {level}: {reason}. Using safe fallback template.");

            // Fallback: use the solver-validated safe standard template.
            var fallback = GetFallbackTemplate();
            if (TryGenerateWithTemplate(level, capacity, candidateSeed, fallback, out data, out path, out reason))
            {
                SaveAccepted(level, candidateSeed, data, path);
                return currentSnapshot.DeepCopy();
            }

            // Last resort: legacy fallback tier
            var legacyFallback = new DifficultyTier {
                name = "Safe Fallback", minLevel = level, maxLevel = level,
                activeColorCount = Math.Min(4, supportedColors.Count), emptyBoltCount = 2,
                targetInverseSteps = 18, minimumAcceptedSteps = 6, minimumMixedBolts = 1, minimumColorTransitions = 2,
                maximumCompletedBoltsAtStart = 1, minimumGuaranteedSolutionLength = 6, maxGenerationAttempts = 80
            };
            if (TryGenerate(level, capacity, candidateSeed, out data, out path, out reason, legacyFallback))
            {
                SaveAccepted(level, candidateSeed, data, path);
                return currentSnapshot.DeepCopy();
            }

            // This deterministic final fallback has the same 4+2 composition and
            // is kept wholly logical. It prevents a null board reaching LevelManager.
            data = BuildEmergencyFallback(level, candidateSeed, capacity, out path);
            if (data != null)
            {
                Debug.LogError("[ProceduralLevelGenerator] Generated emergency safe fallback after bounded retries: " + reason);
                SaveAccepted(level, candidateSeed, data, path);
                return currentSnapshot.DeepCopy();
            }
            Debug.LogError("[ProceduralLevelGenerator] Emergency fallback failed structural/solver validation. Board build remains blocked.");
            return null;
        }

        /// <summary>Dispatches to template or legacy path based on <see cref="useTemplateSystem"/>.</summary>
        private bool TryGenerateForLevel(int level, int capacity, int seed, out LevelDataSO data, out List<LogicalMove> path, out GenerationFailure failure)
        {
            if (useTemplateSystem)
            {
                LevelGenerationTemplate template = SelectTemplate(level, new System.Random(seed));
                if (template == null)
                {
                    data    = null;
                    path    = new List<LogicalMove>();
                    failure = GenerationFailure.NoTemplateAvailable;
                    return false;
                }
                return TryGenerateWithTemplate(level, capacity, seed, template, out data, out path, out failure);
            }
            return TryGenerate(level, capacity, seed, out data, out path, out failure);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Template selection
        // ─────────────────────────────────────────────────────────────────────

        private LevelGenerationTemplate SelectTemplate(int levelNumber, System.Random random)
        {
            if (levelNumber == 6)
                return levelTemplates.Find(t => t != null && t.templateId == "level_6_safe_standard");
            // Determine target difficulty from wave pattern
            DifficultyRating targetDifficulty = GetWaveDifficulty(levelNumber);

            // Gather eligible templates
            var eligible = new List<LevelGenerationTemplate>();
            foreach (var t in levelTemplates)
            {
                if (t == null) continue;
                if (levelNumber < t.minimumLevel || levelNumber > t.maximumLevel) continue;
                if (!IsTemplateValid(t, false)) continue;
                if (recentTemplateIds.Contains(t.templateId)) continue;
                eligible.Add(t);
            }

            if (eligible.Count == 0)
            {
                // Relax recency constraint and try again
                var relaxed = new List<LevelGenerationTemplate>();
                foreach (var t in levelTemplates)
                {
                    if (t == null) continue;
                    if (levelNumber < t.minimumLevel || levelNumber > t.maximumLevel) continue;
                    if (!IsTemplateValid(t, false)) continue;
                    relaxed.Add(t);
                }
                if (relaxed.Count == 0) return GetFallbackTemplate();
                eligible = relaxed;
            }

            // Prefer matching difficulty; fall back to any eligible template
            var preferred = new List<LevelGenerationTemplate>();
            foreach (var t in eligible) if (t.difficulty == targetDifficulty) preferred.Add(t);
            var pool = preferred.Count > 0 ? preferred : eligible;

            // Weighted random pick
            int totalWeight = 0;
            foreach (var t in pool) totalWeight += Mathf.Max(1, t.selectionWeight);
            int roll = random.Next(0, totalWeight);
            int accumulated = 0;
            foreach (var t in pool)
            {
                accumulated += Mathf.Max(1, t.selectionWeight);
                if (roll < accumulated) return t;
            }
            return pool[pool.Count - 1];
        }

        private DifficultyRating GetWaveDifficulty(int levelNumber)
        {
            if (difficultyWavePattern == null || difficultyWavePattern.Count == 0)
                return DifficultyRating.Medium;
            // Wave is 0-indexed from the first procedural level
            int proceduralIndex = Mathf.Max(0, levelNumber - 6);
            int wavePos = proceduralIndex % difficultyWavePattern.Count;
            return difficultyWavePattern[wavePos];
        }

        private LevelGenerationTemplate GetFallbackTemplate()
        {
            foreach (var t in levelTemplates)
                if (t != null && t.templateId == "safe_standard_fallback") return t;
            // Last resort: minimal inline fallback
            return new LevelGenerationTemplate {
                templateId = "emergency_fallback", minimumLevel = 6, maximumLevel = int.MaxValue,
                difficulty = DifficultyRating.Easy, activeColorCount = 4,
                filledBoltCount = 4, normalEmptyBoltCount = 2, lockedBoltCount = 0, expandableBoltCount = 0,
                targetInverseSteps = 18, minimumAcceptedSteps = 6, minimumMixedBolts = 1, minimumColorTransitions = 2,
                minimumGuaranteedSolutionLength = 6, maximumCompletedBoltsAtStart = 2, selectionWeight = 1
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // Template-driven generation
        // ─────────────────────────────────────────────────────────────────────

        private bool TryGenerateWithTemplate(int level, int capacity, int seed,
            LevelGenerationTemplate template, out LevelDataSO data, out List<LogicalMove> forwardPath, out GenerationFailure failure)
        {
            data = null; forwardPath = new List<LogicalMove>(); failure = GenerationFailure.None;

            if (capacity < 1 || !IsTemplateValid(template, false))
            { failure = GenerationFailure.InvalidConfiguration; return false; }

            var random = new System.Random(seed);

            for (int attempt = 0; attempt < Math.Max(1, maximumGenerationAttempts); attempt++)
            {
                var colors = PickColors(random, template.activeColorCount);
                var board  = CreateSolvedBoard(colors, template.filledBoltCount, template.normalEmptyBoltCount, capacity, random);

                // ── Apply inverse scramble ────────────────────────────────────
                var inverse  = new List<LogicalMove>();
                LogicalMove previous = new LogicalMove { sourceBoltIndex = -1, destinationBoltIndex = -1 };

                for (int step = 0; step < template.targetInverseSteps; step++)
                {
                    LogicalMove accepted;
                    if (!TryInverseStep(board, capacity, template.normalEmptyBoltCount, random, previous, out accepted)) continue;
                    inverse.Add(accepted); previous = accepted;
                }

                if (inverse.Count < template.minimumAcceptedSteps)
                { failure = GenerationFailure.InsufficientAcceptedSteps; continue; }

                forwardPath = ReverseToForward(inverse);

                if (!Replay(board, forwardPath, capacity) ||
                    !IsSolved(ReplayState(board, forwardPath, capacity), capacity))
                { failure = GenerationFailure.SolutionReplayFailed; continue; }

                // ── Quality validation ────────────────────────────────────────
                GenerationFailure quality = ValidateQualityFromTemplate(board, colors, template, capacity, forwardPath.Count);
                if (quality != GenerationFailure.None) { failure = quality; continue; }

                // Assemble order is part of the level contract. Remap the recorded
                // normal-board solution before it is ever saved.
                List<List<NutColor>> orderedBoard; List<LogicalMove> remappedPath;
                ReorderNormalBoard(board, forwardPath, out orderedBoard, out remappedPath);
                if (!Replay(orderedBoard, remappedPath, capacity) || !IsSolved(ReplayState(orderedBoard, remappedPath, capacity), capacity))
                { failure = GenerationFailure.SolutionReplayFailed; continue; }

                SolverResult solver = SolveNormalBoard(orderedBoard, capacity);
                if (solver.status != SolverStatus.Solved) { failure = solver.status == SolverStatus.SearchLimitReached ? GenerationFailure.SolverSearchLimitReached : GenerationFailure.SolverUnsolved; continue; }
                float difficultyScore = CalculateDifficultyScore(orderedBoard, capacity, solver);
                if ((template.minimumDifficultyScore > 0f && difficultyScore < template.minimumDifficultyScore) ||
                    (template.maximumDifficultyScore > 0f && difficultyScore > template.maximumDifficultyScore))
                { failure = difficultyScore < template.minimumDifficultyScore ? GenerationFailure.DifficultyTooLow : GenerationFailure.DifficultyTooHigh; continue; }

                data = AssembleLevelData(level, seed, template, colors, orderedBoard, capacity);
                if (data == null || !ValidateFinalComposition(data, template, capacity)) { failure = GenerationFailure.InvalidConfiguration; continue; }
                data.proceduralTemplateId = template.templateId;
                data.difficultyBand = template.difficulty;
                data.validatedSolutionLength = solver.path.Count;
                data.validatedSolverNodes = solver.nodes;
                data.validatedSolverMilliseconds = solver.milliseconds;
                data.puzzleSignature = Signature(data, template.templateId);
                if (recentSignatures.Contains(data.puzzleSignature)) { failure = GenerationFailure.DuplicateSignature; continue; }
                // The independently found path honours completed-bolt locking. The
                // remapped reverse path above remains an explicit index-remap check.
                forwardPath = RemapNormalPathToFinalOrder(solver.path, template);

                // Remember this template to avoid overuse
                RememberTemplateId(template.templateId);
                if (enableDebugLogging)
                    Debug.Log($"[ProceduralLevelGenerator] level={level} seed={seed} template={template.templateId} difficulty={template.difficulty} " +
                              $"colors={colors.Count} filled={template.filledBoltCount} empty={template.normalEmptyBoltCount} locked={template.lockedBoltCount} expandable={template.expandableBoltCount} " +
                              $"positions={data.bolts.Count} solver=Solved path={solver.path.Count} nodes={solver.nodes} ms={solver.milliseconds} score={difficultyScore:F1} signature={data.puzzleSignature} validation=Passed");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Assembles the final <see cref="LevelDataSO"/> from the solved+scrambled board,
        /// appending expandable and locked bolt entries in the required order.
        /// </summary>
        private LevelDataSO AssembleLevelData(int level, int seed, LevelGenerationTemplate template,
            List<NutColor> colors, List<List<NutColor>> board, int capacity)
        {
            var d = ScriptableObject.CreateInstance<LevelDataSO>();
            d.levelNumber     = level;
            d.isProcedural    = true;
            d.seed            = seed;
            d.generatorVersion = generatorVersion;
            d.difficultyTier  = template.templateId;
            d.activeColors    = colors.ToArray();
            d.puzzleSignature = string.Empty;
            d.bolts           = new List<BoltNutStackData>();

            // 1. Separate filled and empty bolts from the board
            var filledStacks = new List<List<NutColor>>();
            var emptyStacks  = new List<List<NutColor>>();
            foreach (var stack in board)
            {
                if (stack.Count > 0) filledStacks.Add(stack);
                else                 emptyStacks.Add(stack);
            }

            // 2. Filled bolts first (BoltType.Normal with nuts)
            foreach (var stack in filledStacks)
                d.bolts.Add(new BoltNutStackData { boltType = BoltType.Normal, nutColors = stack.ToArray() });

            // 3. Normal empty bolts always precede expandable bolts.
            foreach (var stack in emptyStacks)
                d.bolts.Add(new BoltNutStackData { boltType = BoltType.Normal, nutColors = Array.Empty<NutColor>() });

            // 4. Expandable bolts are always the final logical positions.
            for (int i = 0; i < template.expandableBoltCount; i++)
                d.bolts.Add(new BoltNutStackData { boltType = BoltType.Expandable, expandableStartCapacity = template.expandableStartingCapacity, nutColors = Array.Empty<NutColor>() });

            // ── Structural assertion ───────────────────────────────────────────
            if (d.bolts.Count > maximumBoardPositions)
            {
                Debug.LogError($"[ProceduralLevelGenerator] Assembled board exceeds maximum ({d.bolts.Count} > {maximumBoardPositions}). Template: {template.templateId}");
                return null;
            }

            if (template.lockedBoltCount != 0 || (template.expandableBoltCount > 0 && d.bolts[d.bolts.Count - 1].boltType != BoltType.Expandable))
            {
                Debug.LogError($"[ProceduralLevelGenerator] Invalid special-bolt ordering. Template: {template.templateId}");
                return null;
            }

            if (enableDebugLogging)
                Debug.Log($"[ProceduralLevelGenerator] level={level} seed={seed} template={template.templateId} " +
                          $"diff={template.difficulty} bolts={d.bolts.Count}");

            return d;
        }

        private LevelDataSO BuildEmergencyFallback(int level, int seed, int capacity, out List<LogicalMove> path)
        {
            path = new List<LogicalMove>();
            if (capacity != 4 || supportedColors.Count < 4) return null;
            var t = GetFallbackTemplate(); var colors = new List<NutColor>(supportedColors.GetRange(0, 4));
            var board = new List<List<NutColor>>();
            for (int row = 0; row < 4; row++)
            {
                var stack = new List<NutColor>();
                for (int col = 0; col < 4; col++) stack.Add(colors[(row + col) % 4]);
                board.Add(stack);
            }
            board.Add(new List<NutColor>()); board.Add(new List<NutColor>());
            SolverResult solved = SolveNormalBoard(board, capacity);
            if (solved.status != SolverStatus.Solved || ValidateQualityFromTemplate(board, colors, t, capacity, solved.path.Count) != GenerationFailure.None) return null;
            var data = AssembleLevelData(level, seed, t, colors, board, capacity);
            if (data == null || !ValidateFinalComposition(data, t, capacity)) return null;
            data.puzzleSignature = Signature(data, t.templateId); path = solved.path; return data;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Legacy tier-based generation (unchanged from v1, for backward compat)
        // ─────────────────────────────────────────────────────────────────────

        private bool TryGenerate(int level, int capacity, int seed,
            out LevelDataSO data, out List<LogicalMove> forwardPath, out GenerationFailure failure,
            DifficultyTier overrideTier = null)
        {
            data = null; forwardPath = new List<LogicalMove>(); failure = GenerationFailure.None;
            DifficultyTier tier = overrideTier ?? SelectTier(level);
            if (tier == null || capacity < 1 || tier.emptyBoltCount < 1 || tier.activeColorCount > supportedColors.Count)
            { failure = GenerationFailure.InvalidConfiguration; return false; }

            var random = new System.Random(seed);
            for (int attempt = 0; attempt < Math.Min(maximumGenerationAttempts, Math.Max(1, tier.maxGenerationAttempts)); attempt++)
            {
                var colors = PickColors(random, tier.activeColorCount);
                var board  = CreateSolvedBoard(colors, tier.activeColorCount, tier.emptyBoltCount, capacity, random);
                var inverse = new List<LogicalMove>();
                LogicalMove previous = new LogicalMove { sourceBoltIndex = -1, destinationBoltIndex = -1 };
                for (int step = 0; step < tier.targetInverseSteps; step++)
                {
                    LogicalMove accepted;
                    if (!TryInverseStep(board, capacity, tier.emptyBoltCount, random, previous, out accepted)) continue;
                    inverse.Add(accepted); previous = accepted;
                }
                if (inverse.Count < tier.minimumAcceptedSteps) { failure = GenerationFailure.InsufficientAcceptedSteps; continue; }
                forwardPath = ReverseToForward(inverse);
                if (!Replay(board, forwardPath, capacity) || !IsSolved(ReplayState(board, forwardPath, capacity), capacity)) { failure = GenerationFailure.SolutionReplayFailed; continue; }
                GenerationFailure quality = ValidateQuality(board, colors, tier, capacity, forwardPath.Count);
                if (quality != GenerationFailure.None) { failure = quality; continue; }
                string signature = Signature(board, colors, "legacy");
                if (recentSignatures.Contains(signature)) { failure = GenerationFailure.DuplicateSignature; continue; }
                data = ToLevelDataLegacy(level, seed, tier, colors, board, signature);
                return true;
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Inverse scramble step
        // ─────────────────────────────────────────────────────────────────────

        private bool TryInverseStep(List<List<NutColor>> board, int capacity, int expectedEmptyBolts,
            System.Random random, LogicalMove previous, out LogicalMove accepted)
        {
            accepted = new LogicalMove();
            for (int tries = 0; tries < maximumInverseMoveAttemptsPerStep; tries++)
            {
                int from = random.Next(board.Count), to = random.Next(board.Count);
                if (from == to || board[from].Count == 0 || board[to].Count >= capacity) continue;
                int group = TopGroupCount(board[from]);
                int count = random.Next(1, Math.Min(group, capacity - board[to].Count) + 1);
                NutColor color = Top(board[from]);
                if (previous.sourceBoltIndex == to && previous.destinationBoltIndex == from && previous.color == color) continue;
                var before = Clone(board);
                InverseTransfer(board, from, to, count);
                if (CountEmpty(board) > expectedEmptyBolts) { Copy(before, board); continue; }
                var undo = new LogicalMove { sourceBoltIndex = to, destinationBoltIndex = from, color = color, count = count };
                var check = Clone(board);
                if (ApplyForward(check, undo, capacity) == count && Equal(check, before))
                {
                    accepted = new LogicalMove { sourceBoltIndex = from, destinationBoltIndex = to, color = color, count = count };
                    return true;
                }
                Copy(before, board);
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Quality validation
        // ─────────────────────────────────────────────────────────────────────

        private GenerationFailure ValidateQualityFromTemplate(List<List<NutColor>> b, List<NutColor> colors,
            LevelGenerationTemplate template, int cap, int pathLength)
        {
            if (!ValidateStructure(b, colors, template.normalEmptyBoltCount, cap)) return GenerationFailure.InvalidColorCount;
            if (IsSolved(b, cap)) return GenerationFailure.PuzzleAlreadySolved;
            int mixed = 0, transitions = 0, completed = 0, legal = CountLegalMoves(b, cap);
            foreach (var bolt in b)
            {
                if (bolt.Count == cap && IsUniform(bolt)) completed++;
                bool isMixed = false;
                for (int i = 1; i < bolt.Count; i++) if (bolt[i] != bolt[i - 1]) { transitions++; isMixed = true; }
                if (isMixed) mixed++;
            }
            if (mixed      < template.minimumMixedBolts)            return GenerationFailure.InsufficientMixedBolts;
            if (transitions < template.minimumColorTransitions)       return GenerationFailure.InsufficientTransitions;
            if (completed  > template.maximumCompletedBoltsAtStart)  return GenerationFailure.TooManyCompletedBolts;
            if (pathLength < template.minimumGuaranteedSolutionLength) return GenerationFailure.InsufficientAcceptedSteps;
            if (legal < template.minimumStartingLegalMoves || (template.maximumStartingLegalMoves > 0 && legal > template.maximumStartingLegalMoves))
                return GenerationFailure.NoStartingLegalMoves;
            if (OneMoveFromSolved(b, cap)) return GenerationFailure.PuzzleOneMoveFromSolved;
            return GenerationFailure.None;
        }

        private GenerationFailure ValidateQuality(List<List<NutColor>> b, List<NutColor> colors, DifficultyTier tier, int cap, int pathLength)
        {
            if (!ValidateStructure(b, colors, tier.emptyBoltCount, cap)) return GenerationFailure.InvalidColorCount;
            if (IsSolved(b, cap)) return GenerationFailure.PuzzleAlreadySolved;
            int mixed = 0, transitions = 0, completed = 0, legal = CountLegalMoves(b, cap);
            foreach (var bolt in b)
            {
                if (bolt.Count == cap && IsUniform(bolt)) completed++;
                bool isMixed = false;
                for (int i = 1; i < bolt.Count; i++) if (bolt[i] != bolt[i - 1]) { transitions++; isMixed = true; }
                if (isMixed) mixed++;
            }
            if (mixed < tier.minimumMixedBolts) return GenerationFailure.InsufficientMixedBolts;
            if (transitions < tier.minimumColorTransitions) return GenerationFailure.InsufficientTransitions;
            if (completed > tier.maximumCompletedBoltsAtStart) return GenerationFailure.TooManyCompletedBolts;
            if (pathLength < tier.minimumGuaranteedSolutionLength) return GenerationFailure.InsufficientAcceptedSteps;
            if (legal < tier.minimumStartingLegalMoves || (tier.maximumStartingLegalMoves > 0 && legal > tier.maximumStartingLegalMoves)) return GenerationFailure.NoStartingLegalMoves;
            if (OneMoveFromSolved(b, cap)) return GenerationFailure.PuzzleOneMoveFromSolved;
            return GenerationFailure.None;
        }

        private bool ValidateStructure(List<List<NutColor>> b, List<NutColor> colors, int empty, int cap)
        {
            int actualEmpty = 0;
            var counts = new Dictionary<NutColor, int>();
            foreach (var c in colors) counts[c] = 0;
            foreach (var bolt in b)
            {
                if (bolt.Count > cap) return false;
                if (bolt.Count == 0) actualEmpty++;
                foreach (var c in bolt) { if (!counts.ContainsKey(c)) return false; counts[c]++; }
            }
            if (actualEmpty != empty) return false;
            foreach (var c in colors) if (counts[c] != cap) return false;
            return true;
        }

        private bool ValidateLockedBoltOrdering(LevelDataSO data)
        {
            if (data == null || data.bolts == null) return false;
            bool seenLocked = false;
            foreach (var b in data.bolts)
            {
                if (b == null) return false;
                if (seenLocked && b.boltType != BoltType.Locked) return false;
                if (b.boltType == BoltType.Locked) seenLocked = true;
            }
            return true;
        }

        private bool IsTemplateValid(LevelGenerationTemplate t, bool log)
        {
            bool valid = t != null && t.minimumLevel >= 6 && t.maximumLevel >= t.minimumLevel &&
                         t.activeColorCount == t.filledBoltCount && t.activeColorCount <= supportedColors.Count &&
                         t.normalEmptyBoltCount >= 1 && t.TotalBoltCount <= maximumBoardPositions &&
                         t.lockedBoltCount == 0 && t.expandableBoltCount >= 1 && t.expandableBoltCount <= 3;
            if (log && t != null)
                Debug.Log($"[ProceduralLevelGenerator] Template {t.templateId}: colors={t.activeColorCount}, filled={t.filledBoltCount}, valid={valid}");
            return valid;
        }

        private void ValidateTemplateConfiguration()
        {
            if (levelTemplates == null) return;
            foreach (var t in levelTemplates) IsTemplateValid(t, true);
            if (levelTemplates.Find(t => t != null && t.templateId == "level_6_safe_standard") == null ||
                levelTemplates.Find(t => t != null && t.templateId == "safe_standard_fallback") == null)
                Debug.LogError("[ProceduralLevelGenerator] Missing required Level 6 or safe fallback template.");
        }

        private void MigrateTemplateConfigurationIfRequired()
        {
            if (serializedTemplateSchemaVersion >= CurrentTemplateSchemaVersion && levelTemplates != null && levelTemplates.Count > 0) return;
            RebuildDefaultTemplates();
            serializedTemplateSchemaVersion = CurrentTemplateSchemaVersion;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
            Debug.Log($"[ProceduralLevelGenerator] Template configuration migrated to schema version {CurrentTemplateSchemaVersion}. Templates rebuilt: {levelTemplates.Count}");
        }

        [ContextMenu("Rebuild Procedural Templates")]
        private void RebuildDefaultTemplatesFromContextMenu()
        {
            RebuildDefaultTemplates(); serializedTemplateSchemaVersion = CurrentTemplateSchemaVersion;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
            ValidateTemplateConfiguration();
            Debug.Log($"[ProceduralLevelGenerator] Procedural templates were manually rebuilt. Count={levelTemplates.Count}");
        }

        private void RebuildDefaultTemplates()
        {
            levelTemplates = new List<LevelGenerationTemplate>
            {
                Template("level_6_safe_standard", "Level 6: fixed 4-color procedural introduction", 6, 6, DifficultyRating.Medium, 4, 2, 28, 14, 4, 7, 1, 0, 12, 1),
                Template("recovery_standard", "4 colors, lower complexity recovery puzzle", 7, int.MaxValue, DifficultyRating.Recovery, 4, 2, 18, 8, 2, 3, 2, 1, 8, 3),
                Template("standard_4_medium", "4 colors, standard medium puzzle", 7, int.MaxValue, DifficultyRating.Medium, 4, 2, 28, 14, 3, 5, 1, 0, 12, 5),
                Template("standard_4_hard", "4 colors, strongly mixed hard puzzle", 10, int.MaxValue, DifficultyRating.Hard, 4, 2, 38, 18, 3, 6, 1, 0, 15, 4),
                Template("standard_5_medium", "5 colors, 5 filled and 2 empty", 21, int.MaxValue, DifficultyRating.Medium, 5, 2, 38, 18, 4, 7, 1, 0, 15, 4),
                Template("standard_5_hard", "5 colors, strongly mixed hard puzzle", 31, int.MaxValue, DifficultyRating.Hard, 5, 2, 48, 22, 4, 8, 1, 0, 18, 4),
                Template("standard_5_challenge", "5 colors, high-mixing challenge", 41, int.MaxValue, DifficultyRating.Challenge, 5, 2, 56, 25, 4, 9, 1, 0, 20, 2),
                Template("safe_standard_fallback", "Guaranteed safe fallback", 6, int.MaxValue, DifficultyRating.Recovery, 4, 2, 14, 6, 1, 2, 1, 1, 6, 1)
            };
        }

        private static LevelGenerationTemplate Template(string id, string description, int min, int max, DifficultyRating difficulty,
            int colors, int empty, int inverse, int accepted, int mixed, int transitions, int minLegal, int maxCompleted, int minPath, int weight)
        {
            return new LevelGenerationTemplate
            {
                templateId = id, description = description, minimumLevel = min, maximumLevel = max, difficulty = difficulty,
                activeColorCount = colors, filledBoltCount = colors, normalEmptyBoltCount = empty,
                lockedBoltCount = 0, expandableBoltCount = 1, expandableStartingCapacity = 0, lockedBoltOptional = true,
                targetInverseSteps = inverse, minimumAcceptedSteps = accepted, minimumMixedBolts = mixed, minimumColorTransitions = transitions,
                minimumStartingLegalMoves = minLegal, maximumStartingLegalMoves = 0, maximumCompletedBoltsAtStart = maxCompleted,
                minimumGuaranteedSolutionLength = minPath, selectionWeight = weight
            };
        }

        private static void ReorderNormalBoard(List<List<NutColor>> source, List<LogicalMove> path,
            out List<List<NutColor>> ordered, out List<LogicalMove> remapped)
        {
            var indices = new List<int>(source.Count);
            for (int i = 0; i < source.Count; i++) if (source[i].Count > 0) indices.Add(i);
            for (int i = 0; i < source.Count; i++) if (source[i].Count == 0) indices.Add(i);
            var oldToNew = new int[source.Count]; ordered = new List<List<NutColor>>(source.Count);
            for (int n = 0; n < indices.Count; n++) { oldToNew[indices[n]] = n; ordered.Add(new List<NutColor>(source[indices[n]])); }
            remapped = new List<LogicalMove>(path.Count);
            foreach (var originalMove in path)
            {
                var move = originalMove;
                move.sourceBoltIndex = oldToNew[move.sourceBoltIndex];
                move.destinationBoltIndex = oldToNew[move.destinationBoltIndex];
                remapped.Add(move);
            }
        }

        private bool ValidateFinalComposition(LevelDataSO data, LevelGenerationTemplate t, int capacity)
        {
            if (data == null || data.bolts == null || data.bolts.Count != t.TotalBoltCount) return false;
            int filled = 0, empty = 0, locked = 0, expandable = 0;
            var counts = new Dictionary<NutColor, int>(); foreach (var c in data.activeColors) counts[c] = 0;
            for (int i = 0; i < data.bolts.Count; i++)
            {
                var b = data.bolts[i]; if (b == null || b.nutColors == null || b.nutColors.Length > capacity) return false;
                if (b.boltType == BoltType.Locked) return false;
                if (b.boltType == BoltType.Expandable) { if (b.nutColors.Length != 0) return false; expandable++; if (i < data.bolts.Count - t.expandableBoltCount) return false; continue; }
                if (b.nutColors.Length == 0) empty++; else filled++;
                foreach (var c in b.nutColors) { if (!counts.ContainsKey(c)) return false; counts[c]++; }
            }
            if (filled != t.filledBoltCount || empty != t.normalEmptyBoltCount || locked != t.lockedBoltCount || expandable != t.expandableBoltCount) return false;
            foreach (var pair in counts) if (pair.Value != capacity) return false;
            return locked == 0;
        }

        private static List<LogicalMove> RemapNormalPathToFinalOrder(List<LogicalMove> normalPath, LevelGenerationTemplate template)
        {
            // Final order is filled + normal empty + expandable, so normal-board
            // indices are preserved exactly and never point at special bolts.
            return new List<LogicalMove>(normalPath);
        }

        private static float CalculateDifficultyScore(List<List<NutColor>> board, int capacity, SolverResult solver)
        {
            int mixed = 0, transitions = 0;
            foreach (var bolt in board)
            {
                bool boltMixed = false;
                for (int i = 1; i < bolt.Count; i++) if (bolt[i] != bolt[i - 1]) { transitions++; boltMixed = true; }
                if (boltMixed) mixed++;
            }
            int legal = CountLegalMoves(board, capacity);
            // More structure and a longer validated path raise the score; more
            // obvious openings lower it. Values are intentionally Inspector-tunable.
            return mixed * 4f + transitions * 1.5f + solver.path.Count * .75f + Mathf.Min(20f, solver.nodes / 250f) - legal * .5f;
        }

        private static bool ReplaySavedPath(LevelDataSO data, List<LogicalMove> path, int capacity)
        {
            var board = new List<List<NutColor>>();
            foreach (var bolt in data.bolts) board.Add(new List<NutColor>(bolt.nutColors));
            return Replay(board, path, capacity) && IsSolved(ReplayState(board, path, capacity), capacity);
        }

        private bool ValidateBatchComposition(LevelDataSO data, int capacity)
        {
            var template = levelTemplates.Find(t => t != null && t.templateId == data.proceduralTemplateId);
            return template != null && ValidateFinalComposition(data, template, capacity);
        }

        private static bool ValidateBatchColorCounts(LevelDataSO data, int capacity)
        {
            if (data.activeColors == null) return false;
            var counts = new Dictionary<NutColor, int>(); foreach (var color in data.activeColors) counts[color] = 0;
            foreach (var bolt in data.bolts) foreach (var color in bolt.nutColors) { if (!counts.ContainsKey(color)) return false; counts[color]++; }
            foreach (var pair in counts) if (pair.Value != capacity) return false;
            return true;
        }

        private enum SolverStatus { Solved, Unsolvable, SearchLimitReached }
        private struct SolverResult { public SolverStatus status; public List<LogicalMove> path; public int nodes; public long milliseconds; }

        private SolverResult SolveNormalBoard(List<List<NutColor>> board, int capacity)
        {
            var result = new SolverResult { status = SolverStatus.Unsolvable, path = new List<LogicalMove>() };
            var timer = System.Diagnostics.Stopwatch.StartNew(); var visited = new HashSet<string>(); var path = new List<LogicalMove>();
            bool limit = false;
            Func<List<List<NutColor>>, int, LogicalMove?, bool> search = null;
            search = (state, depth, previous) =>
            {
                if (IsSolved(state, capacity)) return true;
                if (depth >= maximumSolverDepth || result.nodes++ >= maximumSolverNodes || timer.ElapsedMilliseconds >= maximumSolverMilliseconds) { limit = true; return false; }
                string key = StateKey(state); if (!visited.Add(key)) return false;
                var moves = new List<LogicalMove>(); var nextStates = new HashSet<string>();
                for (int s = 0; s < state.Count; s++) for (int d = 0; d < state.Count; d++) if (s != d)
                {
                    // GameManager locks a completed normal bolt, so it can no
                    // longer be used as a source on a later move.
                    if (state[s].Count == capacity && IsUniform(state[s])) continue;
                    if (previous.HasValue && previous.Value.sourceBoltIndex == d && previous.Value.destinationBoltIndex == s) continue;
                    var next = Clone(state); var m = new LogicalMove { sourceBoltIndex = s, destinationBoltIndex = d, color = next[s].Count > 0 ? Top(next[s]) : NutColor.Red };
                    m.count = ApplyForward(next, m, capacity); if (m.count == 0 || !nextStates.Add(StateKey(next))) continue;
                    moves.Add(m);
                }
                moves.Sort((a, b) => b.count.CompareTo(a.count));
                foreach (var m in moves) { var next = Clone(state); ApplyForward(next, m, capacity); path.Add(m); if (search(next, depth + 1, m)) return true; path.RemoveAt(path.Count - 1); }
                return false;
            };
            if (search(Clone(board), 0, null)) { result.status = SolverStatus.Solved; result.path = new List<LogicalMove>(path); }
            else if (limit) result.status = SolverStatus.SearchLimitReached;
            result.milliseconds = timer.ElapsedMilliseconds;
            return result;
        }

        private static string StateKey(List<List<NutColor>> board)
        {
            var s = new StringBuilder(); foreach (var b in board) { foreach (var c in b) s.Append((int)c); s.Append('|'); } return s.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Pure logical board helpers
        // ─────────────────────────────────────────────────────────────────────

        private static void InverseTransfer(List<List<NutColor>> b, int from, int to, int count)
        {
            var source = b[from]; var dest = b[to]; int first = source.Count - count;
            for (int i = first; i < source.Count; i++) dest.Add(source[i]);
            source.RemoveRange(first, count);
        }

        private static int ApplyForward(List<List<NutColor>> b, LogicalMove move, int capacity)
        {
            var source = b[move.sourceBoltIndex]; var destination = b[move.destinationBoltIndex];
            if (source.Count == 0 || destination.Count >= capacity) return 0;
            NutColor color = Top(source);
            if (destination.Count > 0 && Top(destination) != color) return 0;
            int count = Math.Min(TopGroupCount(source), capacity - destination.Count);
            if (count <= 0) return 0;
            InverseTransfer(b, move.sourceBoltIndex, move.destinationBoltIndex, count);
            return count;
        }

        private static bool OneMoveFromSolved(List<List<NutColor>> b, int cap)
        {
            for (int i = 0; i < b.Count; i++) for (int j = 0; j < b.Count; j++) if (i != j)
            {
                var copy = Clone(b);
                if (ApplyForward(copy, new LogicalMove { sourceBoltIndex = i, destinationBoltIndex = j }, cap) > 0 && IsSolved(copy, cap)) return true;
            }
            return false;
        }

        private static int CountLegalMoves(List<List<NutColor>> b, int cap)
        {
            int n = 0;
            for (int i = 0; i < b.Count; i++) for (int j = 0; j < b.Count; j++)
                if (i != j && ApplyForward(Clone(b), new LogicalMove { sourceBoltIndex = i, destinationBoltIndex = j }, cap) > 0) n++;
            return n;
        }

        private static bool Replay(List<List<NutColor>> b, List<LogicalMove> moves, int cap)
            => ReplayState(b, moves, cap) != null;

        private static List<List<NutColor>> ReplayState(List<List<NutColor>> b, List<LogicalMove> moves, int cap)
        {
            var state = Clone(b);
            foreach (var m in moves)
            {
                if (m.sourceBoltIndex < 0 || m.sourceBoltIndex >= state.Count || state[m.sourceBoltIndex].Count == 0 ||
                    Top(state[m.sourceBoltIndex]) != m.color || ApplyForward(state, m, cap) != m.count)
                    return null;
            }
            return state;
        }

        private static bool IsSolved(List<List<NutColor>> b, int cap)
        {
            if (b == null) return false;
            foreach (var bolt in b) if (bolt.Count != 0 && (bolt.Count != cap || !IsUniform(bolt))) return false;
            return true;
        }

        private static bool IsUniform(List<NutColor> bolt) { for (int i = 1; i < bolt.Count; i++) if (bolt[i] != bolt[0]) return false; return true; }
        private static NutColor Top(List<NutColor> b) => b[b.Count - 1];
        private static int TopGroupCount(List<NutColor> b) { if (b.Count == 0) return 0; NutColor c = Top(b); int n = 0; for (int i = b.Count - 1; i >= 0 && b[i] == c; i--) n++; return n; }
        private static List<List<NutColor>> Clone(List<List<NutColor>> b) { var c = new List<List<NutColor>>(b.Count); foreach (var x in b) c.Add(new List<NutColor>(x)); return c; }
        private static void Copy(List<List<NutColor>> from, List<List<NutColor>> to) { for (int i = 0; i < from.Count; i++) { to[i].Clear(); to[i].AddRange(from[i]); } }
        private static bool Equal(List<List<NutColor>> a, List<List<NutColor>> b) { if (a.Count != b.Count) return false; for (int i = 0; i < a.Count; i++) { if (a[i].Count != b[i].Count) return false; for (int j = 0; j < a[i].Count; j++) if (a[i][j] != b[i][j]) return false; } return true; }
        private static List<LogicalMove> ReverseToForward(List<LogicalMove> inverse) { var result = new List<LogicalMove>(inverse.Count); for (int i = inverse.Count - 1; i >= 0; i--) { var m = inverse[i]; result.Add(new LogicalMove { sourceBoltIndex = m.destinationBoltIndex, destinationBoltIndex = m.sourceBoltIndex, color = m.color, count = m.count }); } return result; }
        private static int CountEmpty(List<List<NutColor>> b) { int n = 0; foreach (var x in b) if (x.Count == 0) n++; return n; }

        // ─────────────────────────────────────────────────────────────────────
        // Board construction
        // ─────────────────────────────────────────────────────────────────────

        private List<List<NutColor>> CreateSolvedBoard(List<NutColor> colors, int filled, int empty, int cap, System.Random random)
        {
            if (colors == null || colors.Count != filled)
                throw new ArgumentException("Solved board requires exactly one filled bolt per active color.");
            var board = new List<List<NutColor>>();
            foreach (var c in colors) { var bolt = new List<NutColor>(); for (int i = 0; i < cap; i++) bolt.Add(c); board.Add(bolt); }
            for (int i = 0; i < empty; i++) board.Add(new List<NutColor>());
            // Shuffle bolt order for variety
            for (int i = board.Count - 1; i > 0; i--) { int j = random.Next(i + 1); var temp = board[i]; board[i] = board[j]; board[j] = temp; }
            return board;
        }

        private List<NutColor> PickColors(System.Random random, int count)
        {
            count = Math.Min(count, supportedColors.Count);
            var pool = new List<NutColor>(supportedColors);
            for (int i = pool.Count - 1; i > 0; i--) { int j = random.Next(i + 1); var t = pool[i]; pool[i] = pool[j]; pool[j] = t; }
            return pool.GetRange(0, count);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Snapshot management
        // ─────────────────────────────────────────────────────────────────────

        private void SaveAccepted(int level, int seed, LevelDataSO data, List<LogicalMove> path)
        {
            currentLevelIndex = level;
            currentSeed       = seed;
            currentSnapshot   = data.DeepCopy();
            currentSolution   = new List<LogicalMove>(path);
            RememberSignature(data.puzzleSignature);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Legacy tier helpers
        // ─────────────────────────────────────────────────────────────────────

        private DifficultyTier SelectTier(int level)
        {
            foreach (var tier in difficultyTiers)
                if (tier != null && level >= tier.minLevel && level <= tier.maxLevel) return tier;
            return difficultyTiers.Count > 0 ? difficultyTiers[difficultyTiers.Count - 1] : null;
        }

        private LevelDataSO ToLevelDataLegacy(int level, int seed, DifficultyTier tier, List<NutColor> colors, List<List<NutColor>> board, string signature)
        {
            var d = ScriptableObject.CreateInstance<LevelDataSO>();
            d.levelNumber = level; d.isProcedural = true; d.seed = seed; d.generatorVersion = generatorVersion;
            d.difficultyTier = tier.name; d.activeColors = colors.ToArray(); d.puzzleSignature = signature;
            d.bolts = new List<BoltNutStackData>();
            foreach (var bolt in board) d.bolts.Add(new BoltNutStackData { nutColors = bolt.ToArray() });
            return d;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Signature and memory
        // ─────────────────────────────────────────────────────────────────────

        private string Signature(List<List<NutColor>> board, List<NutColor> colors, string templateId)
        {
            var sb = new StringBuilder("V").Append(generatorVersion).Append('|').Append(templateId).Append('|');
            foreach (var c in colors) sb.Append((int)c).Append(',');
            sb.Append('|');
            foreach (var b in board)
            {
                if (b.Count == 0) sb.Append('_');
                else for (int i = 0; i < b.Count; i++) { if (i > 0) sb.Append(','); sb.Append((int)b[i]); }
                sb.Append(';');
            }
            return sb.ToString();
        }

        private string Signature(LevelDataSO data, string templateId)
        {
            var sb = new StringBuilder("V").Append(generatorVersion).Append('|').Append(templateId).Append('|');
            if (data.activeColors != null) foreach (var color in data.activeColors) sb.Append((int)color).Append(',');
            sb.Append('|');
            foreach (var bolt in data.bolts)
            {
                sb.Append((int)bolt.boltType).Append(':').Append(bolt.expandableStartCapacity).Append(':');
                if (bolt.nutColors != null) foreach (var color in bolt.nutColors) sb.Append((int)color).Append(',');
                sb.Append(';');
            }
            return sb.ToString();
        }

        private void RememberSignature(string signature)
        {
            recentSignatures.Enqueue(signature);
            while (recentSignatures.Count > recentSignatureHistorySize) recentSignatures.Dequeue();
        }

        private void RememberTemplateId(string templateId)
        {
            recentTemplateIds.Enqueue(templateId);
            while (recentTemplateIds.Count > recentTemplateHistorySize) recentTemplateIds.Dequeue();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Inspector replay / validation helpers
        // ─────────────────────────────────────────────────────────────────────

        private bool ReplaySaved(int cap)
        {
            if (currentSnapshot == null) return false;
            return ReplaySavedPath(currentSnapshot, currentSolution, cap);
        }

        private string ValidateSavedCurrent(int cap)
        {
            if (currentSnapshot == null) return "No current snapshot.";
            var b = new List<List<NutColor>>();
            int empty = 0;
            foreach (var x in currentSnapshot.bolts)
                if (x.boltType == BoltType.Normal) { b.Add(new List<NutColor>(x.nutColors)); if (x.nutColors == null || x.nutColors.Length == 0) empty++; }
            return ValidateStructure(b, new List<NutColor>(currentSnapshot.activeColors), empty, cap) && ReplaySaved(cap)
                ? "Current puzzle is valid."
                : "Current puzzle failed validation.";
        }

        private static void GetMetrics(LevelDataSO data, out int mixed, out int transitions)
        {
            mixed = 0; transitions = 0;
            foreach (var stack in data.bolts)
            {
                if (stack?.nutColors == null || stack.nutColors.Length < 2) continue;
                bool isMixed = false;
                var nuts = stack.nutColors;
                for (int i = 1; i < nuts.Length; i++) if (nuts[i] != nuts[i - 1]) { transitions++; isMixed = true; }
                if (isMixed) mixed++;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Seed helpers
        // ─────────────────────────────────────────────────────────────────────

        private static int NewSeed()     => unchecked((int)DateTime.UtcNow.Ticks ^ Environment.TickCount);
        private static int NextSeed(int seed) => unchecked(seed * 1103515245 + 12345);

        private static string FormatFailures(Dictionary<GenerationFailure, int> f)
        {
            var s = new StringBuilder();
            foreach (var x in f) s.Append(x.Key).Append('=').Append(x.Value).Append(' ');
            return s.ToString();
        }
    }
}
