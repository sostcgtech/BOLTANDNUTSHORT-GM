using UnityEngine;

namespace NutBoltSort
{
    // ─────────────────────────────────────────────────────────────────────────
    // LevelProgressionConfig — ScriptableObject
    // ─────────────────────────────────────────────────────────────────────────
    // The existing ProceduralLevelGenerator (Assets/Generation/) owns its own
    // DifficultyTier list and all generation settings.
    // This ScriptableObject only holds the high-level game-flow constants that
    // StructuredLevelProvider and TutorialController need at runtime.
    // ─────────────────────────────────────────────────────────────────────────

    [CreateAssetMenu(fileName = "LevelProgressionConfig", menuName = "NutBoltSort/Level Progression Config", order = 2)]
    public class LevelProgressionConfig : ScriptableObject
    {
        // ── Global Limits ─────────────────────────────────────────────────────
        [Header("Global Limits")]
        [Tooltip("The generator will never create more bolt positions than this value.")]
        [Min(2)] public int maximumBoardPositions = 8;

        [Tooltip("Capacity of a fully normal or fully expanded bolt (must match BoltView.Capacity).")]
        [Min(2)] public int normalBoltCapacity = 4;

        [Tooltip("Number of fixed introductory levels before procedural generation begins.")]
        [Min(1)] public int introductoryLevelCount = 5;

        // ── Level 1 ───────────────────────────────────────────────────────────
        [Header("Level 1 — Tutorial")]
        [Tooltip("Nut color used in the Level 1 tutorial.")]
        public NutColor level1TutorialColor = NutColor.Yellow;

        [Tooltip("Number of nuts per bolt in the Level 1 tutorial.")]
        [Range(1, 4)] public int level1NutsPerBolt = 2;

        // ── Expandable Bolt ───────────────────────────────────────────────────
        [Header("Expandable Bolt")]
        [Tooltip("Maximum number of capacity stages for the expandable bolt.")]
        [Min(1)] public int expandableMaxCapacity = 4;

        // ── Generation Retries ────────────────────────────────────────────────
        [Header("Generation")]
        [Tooltip("Maximum times StructuredLevelProvider retries a failed procedural level before loading Level 1.")]
        [Min(1)] public int maxGenerationAttempts = 8;

        [Header("Procedural Special-Bolt Schedule")]
        [Tooltip("Adds one optional locked bolt after the generated normal board. It is always the final grid item. Ignored when ProceduralLevelGenerator.useTemplateSystem is true.")]
        public bool addOptionalLockedBoltToProceduralLevels = true;

        [Min(6)] public int firstOptionalLockedBoltLevel = 6;
        [Min(1)] public int optionalLockedBoltInterval = 4;

        // ── Endless Tier ──────────────────────────────────────────────────────
        [Header("Endless Tier")]
        [Tooltip("The 1-based level number at which the final endless difficulty tier begins. Levels at or above this number use capped expert templates indefinitely.")]
        [Min(51)] public int finalEndlessTierStartLevel = 101;

        // ── Debug ─────────────────────────────────────────────────────────────
        [Header("Debug")]
        [Tooltip("When enabled, Level 5 emits a detailed validation log to the Console at load time.")]
        public bool enableLevel5DetailedLog = true;
    }
}
