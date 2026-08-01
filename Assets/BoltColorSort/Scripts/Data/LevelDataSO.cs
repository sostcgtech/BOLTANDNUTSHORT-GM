using System;
using System.Collections.Generic;
using UnityEngine;

namespace NutBoltSort
{
    [Serializable]
    public class BoltNutStackData
    {
        [Tooltip("Nut colors ordered from Bottom (index 0) to Top (index 3). Empty array indicates an empty bolt.")]
        public NutColor[] nutColors;

        [Tooltip("Per-nut hidden flags, ordered with nutColors from Bottom to Top. Real colours always remain in nutColors.")]
        public bool[] startsHidden;

        [Tooltip("Bolt classification. Normal bolts behave as standard 4-slot targets.")]
        public BoltType boltType = BoltType.Normal;

        [Tooltip("Starting capacity for Expandable bolts (0 = fully covered, 4 = same as Normal).")]
        [Range(0, 4)]
        public int expandableStartCapacity = 0;
    }

    [CreateAssetMenu(fileName = "Level_01", menuName = "NutBoltSort/Level Data", order = 1)]
    public class LevelDataSO : ScriptableObject
    {
        [Min(1)]
        public int levelNumber = 1;

        [Tooltip("List of bolt stack configurations for this level (ordered bottom to top).")]
        public List<BoltNutStackData> bolts = new List<BoltNutStackData>();

        // Runtime procedural metadata. These fields are deliberately logical only:
        // layout, transforms and materials remain the responsibility of existing systems.
        [Header("Procedural Metadata")]
        public bool isProcedural;
        public int seed;
        public int generatorVersion;
        public string proceduralTemplateId;
        public DifficultyRating difficultyBand;
        public int validatedSolutionLength;
        public int validatedSolverNodes;
        public long validatedSolverMilliseconds;
        public string difficultyTier;
        [Tooltip("Selected visual layout. Placement remains owned by LevelManager/BoltGridLayout.")]
        public string layoutPresetId;
        public NutColor[] activeColors;
        [TextArea] public string puzzleSignature;

        public int BoltCount => bolts != null ? bolts.Count : 0;

        public LevelDataSO DeepCopy()
        {
            var copy = CreateInstance<LevelDataSO>();
            copy.levelNumber = levelNumber;
            copy.isProcedural = isProcedural;
            copy.seed = seed;
            copy.generatorVersion = generatorVersion;
            copy.proceduralTemplateId = proceduralTemplateId;
            copy.difficultyBand = difficultyBand;
            copy.validatedSolutionLength = validatedSolutionLength;
            copy.validatedSolverNodes = validatedSolverNodes;
            copy.validatedSolverMilliseconds = validatedSolverMilliseconds;
            copy.difficultyTier = difficultyTier;
            copy.layoutPresetId = layoutPresetId;
            copy.puzzleSignature = puzzleSignature;
            copy.activeColors = activeColors != null ? (NutColor[])activeColors.Clone() : Array.Empty<NutColor>();
            copy.bolts = new List<BoltNutStackData>(bolts != null ? bolts.Count : 0);
            if (bolts != null)
                foreach (var bolt in bolts)
                    copy.bolts.Add(new BoltNutStackData
                    {
                        nutColors               = bolt != null && bolt.nutColors != null ? (NutColor[])bolt.nutColors.Clone() : Array.Empty<NutColor>(),
                        startsHidden             = bolt != null && bolt.startsHidden != null ? (bool[])bolt.startsHidden.Clone() : Array.Empty<bool>(),
                        boltType                = bolt != null ? bolt.boltType : BoltType.Normal,
                        expandableStartCapacity = bolt != null ? bolt.expandableStartCapacity : 0
                    });
            return copy;
        }
    }
}
