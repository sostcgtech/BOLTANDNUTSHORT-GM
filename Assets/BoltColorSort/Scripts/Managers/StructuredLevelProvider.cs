using System.Collections.Generic;
using UnityEngine;

namespace NutBoltSort
{
    /// <summary>
    /// Single source of truth for level data throughout the game.
    ///
    /// Levels 1–5 are hand-authored fixed introductory levels.
    /// Levels 6+ are delegated to the existing ProceduralLevelGenerator
    ///   (Assets/BoltColorSort/Scripts/Generation/ProceduralLevelGenerator.cs)
    ///   via its GetOrGenerateCurrentLevel() API.
    ///
    /// Also owns the deep-copy snapshot used by Restart to restore the exact
    /// starting board without regenerating.
    /// </summary>
    public class StructuredLevelProvider : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Configuration (auto-loaded from Resources if null)")]
        [SerializeField] private LevelProgressionConfig config;

        [Header("Procedural Generator (auto-found if null)")]
        [SerializeField] private ProceduralLevelGenerator generator;

        // ── Runtime State ──────────────────────────────────────────────────────
        private LevelDataSO _currentSnapshot;

        public LevelDataSO CurrentSnapshot => _currentSnapshot;

        // ─────────────────────────────────────────────────────────────────────
        // Unity Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (config    == null) config    = Resources.Load<LevelProgressionConfig>("LevelProgressionConfig");
            if (generator == null) generator = GetComponent<ProceduralLevelGenerator>()
                                               ?? FindObjectOfType<ProceduralLevelGenerator>()
                                               ?? gameObject.AddComponent<ProceduralLevelGenerator>();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the LevelDataSO for the given 1-based level number.
        /// Stores a deep-copy snapshot for Restart use.
        /// Falls back to Level 1 if generation fails.
        /// </summary>
        public LevelDataSO GetLevelData(int levelNumber)
        {
            int introCount = config != null ? config.introductoryLevelCount : 5;

            LevelDataSO data = levelNumber <= introCount
                ? BuildIntroductoryLevel(levelNumber)
                : GenerateProcedural(levelNumber);

            if (data == null)
            {
                Debug.LogError($"[StructuredLevelProvider] GetLevelData({levelNumber}) returned null. Using Level 1 fallback.");
                data = BuildIntroductoryLevel(1);
            }

            _currentSnapshot = data.DeepCopy();
            return data;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Introductory Levels (1–5)
        // ─────────────────────────────────────────────────────────────────────

        private LevelDataSO BuildIntroductoryLevel(int levelNumber)
        {
            switch (levelNumber)
            {
                case 1:  return BuildLevel1();
                case 2:  return BuildLevel2();
                case 3:  return BuildLevel3();
                case 4:  return BuildLevel4();
                case 5:  return BuildLevel5();
                default: return BuildLevel1();
            }
        }

        // ── Level 1 — Tutorial ─────────────────────────────────────────────────
        // Two bolts, 2 Yellow nuts each. Tutorial teaches selection and transfer.
        private LevelDataSO BuildLevel1()
        {
            NutColor color      = config != null ? config.level1TutorialColor : NutColor.Yellow;
            int      nutsPerBolt = config != null ? config.level1NutsPerBolt  : 2;

            var data = MakeLevelData(1);
            data.activeColors = new[] { color };
            data.bolts.Add(MakeBolt(FillArray(color, nutsPerBolt)));
            data.bolts.Add(MakeBolt(FillArray(color, nutsPerBolt)));
            return ValidateOrFallback(data, 1);
        }

        // ── Level 2 — First Real Puzzle ────────────────────────────────────────
        // 3 bolts: 2 mixed + 1 empty (last). 2 colors × 4 nuts.
        private LevelDataSO BuildLevel2()
        {
            var data = MakeLevelData(2);
            data.activeColors = new[] { NutColor.Blue, NutColor.Yellow };

            data.bolts.Add(MakeBolt(NutColor.Blue, NutColor.Blue, NutColor.Yellow, NutColor.Yellow));
            data.bolts.Add(MakeBolt(NutColor.Yellow, NutColor.Yellow, NutColor.Blue, NutColor.Blue));
            data.bolts.Add(MakeBolt()); // empty — last
            return ValidateOrFallback(data, 2);
        }

        // ── Level 3 — Expandable Bolt Introduction ─────────────────────────────
        // 3 filled (2+2 stripe arrangement) + 1 expandable (cap=0) + 1 empty (last).
        private LevelDataSO BuildLevel3()
        {
            var data = MakeLevelData(3);
            data.activeColors = new[] { NutColor.Red, NutColor.Green, NutColor.Blue };

            data.bolts.Add(MakeBolt(NutColor.Red,   NutColor.Red,   NutColor.Green, NutColor.Green));
            data.bolts.Add(MakeBolt(NutColor.Green,  NutColor.Green, NutColor.Blue,  NutColor.Blue));
            data.bolts.Add(MakeBolt(NutColor.Blue,   NutColor.Blue,  NutColor.Red,   NutColor.Red));
            data.bolts.Add(MakeExpandableBolt(0));  // expandable, starts locked
            data.bolts.Add(MakeBolt());              // empty, last
            return ValidateOrFallback(data, 3);
        }

        // ── Level 4 — More Working Space ──────────────────────────────────────
        // 3 filled (fully interleaved) + 1 expandable (cap=0) + 2 empty (last).
        private LevelDataSO BuildLevel4()
        {
            var data = MakeLevelData(4);
            data.activeColors = new[] { NutColor.Red, NutColor.Green, NutColor.Blue };

            data.bolts.Add(MakeBolt(NutColor.Red,   NutColor.Green, NutColor.Blue,  NutColor.Red));
            data.bolts.Add(MakeBolt(NutColor.Green,  NutColor.Blue,  NutColor.Red,   NutColor.Green));
            data.bolts.Add(MakeBolt(NutColor.Blue,   NutColor.Red,   NutColor.Green, NutColor.Blue));
            data.bolts.Add(MakeExpandableBolt(0));
            data.bolts.Add(MakeBolt()); // empty
            data.bolts.Add(MakeBolt()); // empty
            return ValidateOrFallback(data, 4);
        }

        // ── Level 5 — Locked Bolt Introduction ────────────────────────────────
        // 4 filled (Latin-square: one of each colour per bolt) + 1 expandable
        // (cap=0) + 1 locked + 2 empty (last).
        private LevelDataSO BuildLevel5()
        {
            var data = MakeLevelData(5);
            data.activeColors = new[] { NutColor.Red, NutColor.Green, NutColor.Blue, NutColor.Yellow };

            data.bolts.Add(MakeBolt(NutColor.Red,    NutColor.Green,  NutColor.Blue,   NutColor.Yellow));
            data.bolts.Add(MakeBolt(NutColor.Green,  NutColor.Blue,   NutColor.Yellow, NutColor.Red));
            data.bolts.Add(MakeBolt(NutColor.Blue,   NutColor.Yellow, NutColor.Red,    NutColor.Green));
            data.bolts.Add(MakeBolt(NutColor.Yellow, NutColor.Red,    NutColor.Green,  NutColor.Blue));
            data.bolts.Add(MakeExpandableBolt(0));
            data.bolts.Add(MakeBolt()); // normal empty
            data.bolts.Add(MakeBolt()); // normal empty
            data.bolts.Add(MakeLockedBolt()); // locked is always the final logical/grid item
            return ValidateLevel5(data);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Procedural Dispatch — delegates to the existing ProceduralLevelGenerator
        // ─────────────────────────────────────────────────────────────────────

        private LevelDataSO GenerateProcedural(int levelNumber)
        {
            if (generator == null)
            {
                Debug.LogError("[StructuredLevelProvider] ProceduralLevelGenerator not found in scene.");
                return null;
            }

            // The existing generator uses a 0-based index internally; pass 0-based.
            LevelDataSO data = generator.GetOrGenerateCurrentLevel(levelNumber - 1, BoltView.Capacity);
            if (data == null)
            {
                Debug.LogError($"[StructuredLevelProvider] ProceduralLevelGenerator returned null for level {levelNumber}.");
            }
            else
            {
                ApplyProceduralSpecialBoltSchedule(data, levelNumber);
                int empty = 0;
                foreach (var bolt in data.bolts) if (bolt.nutColors == null || bolt.nutColors.Length == 0) empty++;
                Debug.Log($"[StructuredLevelProvider] Level {levelNumber}: tier={data.difficultyTier}, seed={data.seed}, bolts={data.bolts.Count}, colors={data.activeColors.Length}, empty={empty}, validation=ready");
            }
            return data;
        }

        private void ApplyProceduralSpecialBoltSchedule(LevelDataSO data, int levelNumber)
        {
            if (config == null || !config.addOptionalLockedBoltToProceduralLevels || data == null || data.bolts == null) return;
            bool scheduled = levelNumber >= config.firstOptionalLockedBoltLevel &&
                             (levelNumber - config.firstOptionalLockedBoltLevel) % config.optionalLockedBoltInterval == 0;
            if (!scheduled || data.bolts.Count >= config.maximumBoardPositions) return;
            // Appending makes the locked bolt the final logical item, hence final grid position.
            // It is optional: the verified normal-board solution remains valid without unlocking it.
            data.bolts.Add(MakeLockedBolt());
        }

        // ─────────────────────────────────────────────────────────────────────
        // Validation
        // ─────────────────────────────────────────────────────────────────────

        private LevelDataSO ValidateOrFallback(LevelDataSO data, int levelNumber)
        {
            if (data == null || data.bolts == null || data.bolts.Count == 0)
            {
                Debug.LogError($"[StructuredLevelProvider] Level {levelNumber} data is empty. Using Level 1 fallback.");
                return BuildLevel1();
            }

            // No bolt should exceed normal capacity.
            foreach (var b in data.bolts)
            {
                if (b.nutColors != null && b.nutColors.Length > BoltView.Capacity)
                {
                    Debug.LogError($"[StructuredLevelProvider] Level {levelNumber}: bolt has {b.nutColors.Length} nuts (max {BoltView.Capacity}).");
                    return BuildLevel1();
                }
            }

            return data;
        }

        private LevelDataSO ValidateLevel5(LevelDataSO data)
        {
            if (data == null || data.bolts == null || data.bolts.Count != 8)
            {
                Debug.LogError("[StructuredLevelProvider] Level 5 must contain exactly eight board positions.");
                return BuildLevel1();
            }
            int normalFilled = 0, normalEmpty = 0, expandable = 0, locked = 0;
            for (int i = 0; i < data.bolts.Count; i++)
            {
                BoltNutStackData bolt = data.bolts[i];
                if (bolt.boltType == BoltType.Expandable) expandable++;
                else if (bolt.boltType == BoltType.Locked) locked++;
                else if (bolt.nutColors == null || bolt.nutColors.Length == 0) normalEmpty++;
                else normalFilled++;
            }
            bool valid = normalFilled == 4 && expandable == 1 && normalEmpty == 2 && locked == 1 && data.bolts[7].boltType == BoltType.Locked;
            if (!valid)
            {
                Debug.LogError($"[StructuredLevelProvider] Invalid Level 5: filled={normalFilled}, expandable={expandable}, empty={normalEmpty}, locked={locked}, lockedLast={data.bolts[7].boltType == BoltType.Locked}.");
                return BuildLevel1();
            }
            return ValidateOrFallback(data, 5);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Factory Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static LevelDataSO MakeLevelData(int levelNumber)
        {
            var data = ScriptableObject.CreateInstance<LevelDataSO>();
            data.levelNumber  = levelNumber;
            data.isProcedural = false;
            data.bolts        = new List<BoltNutStackData>();
            return data;
        }

        private static BoltNutStackData MakeBolt(params NutColor[] colors)
        {
            return new BoltNutStackData
            {
                nutColors = colors ?? System.Array.Empty<NutColor>(),
                boltType  = BoltType.Normal
            };
        }

        private static BoltNutStackData MakeExpandableBolt(int startCapacity)
        {
            return new BoltNutStackData
            {
                nutColors               = System.Array.Empty<NutColor>(),
                boltType                = BoltType.Expandable,
                expandableStartCapacity = startCapacity
            };
        }

        private static BoltNutStackData MakeLockedBolt()
        {
            return new BoltNutStackData
            {
                nutColors = System.Array.Empty<NutColor>(),
                boltType  = BoltType.Locked
            };
        }

        private static NutColor[] FillArray(NutColor color, int count)
        {
            var arr = new NutColor[count];
            for (int i = 0; i < count; i++) arr[i] = color;
            return arr;
        }
    }
}
