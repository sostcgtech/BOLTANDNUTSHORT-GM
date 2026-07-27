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
    }

    [CreateAssetMenu(fileName = "Level_01", menuName = "NutBoltSort/Level Data", order = 1)]
    public class LevelDataSO : ScriptableObject
    {
        [Min(1)]
        public int levelNumber = 1;

        [Tooltip("List of bolt stack configurations for this level (ordered bottom to top).")]
        public List<BoltNutStackData> bolts = new List<BoltNutStackData>();

        public int BoltCount => bolts != null ? bolts.Count : 0;
    }
}
