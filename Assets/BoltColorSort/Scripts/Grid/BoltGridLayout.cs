using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace NutBoltSort
{
    [Serializable]
    public class BoardLayoutPreset
    {
        public string presetId;
        public int totalPositions;
        public int[] boltsPerRow;
        public float horizontalSpacing = 2.2f;
        public float rowDepthSpacing = 2.8f;
        public float rowHeightOffset;
        public Vector3 centerOffset = new Vector3(0f, 0f, -.25f);
        public float cameraSizeMultiplier = 1f;
    }

    [Serializable]
    public class CameraFramingPreset
    {
        public string presetId;
        public int minimumPositions;
        public int maximumPositions;
        public float orthographicSizeMultiplier = 1f;
        public Vector3 boardCenterOffset;
    }

    /// <summary>Curated, centered row distributions used by procedural levels.</summary>
    public static class BoardLayoutPresets
    {
        public static BoardLayoutPreset Find(string id)
        {
            foreach (var preset in Defaults)
                if (preset.presetId == id) return preset;
            return null;
        }

        public static readonly BoardLayoutPreset[] Defaults =
        {
            P("six_3_3", 6, new[] { 3, 3 }, 1f), P("six_4_2", 6, new[] { 4, 2 }, 1f),
            P("seven_4_3", 7, new[] { 4, 3 }, 1.03f), P("seven_3_4", 7, new[] { 3, 4 }, 1.03f),
            P("eight_4_4", 8, new[] { 4, 4 }, 1.06f), P("eight_3_3_2", 8, new[] { 3, 3, 2 }, 1.04f),
            P("nine_5_4", 9, new[] { 5, 4 }, 1.10f), P("nine_3_3_3", 9, new[] { 3, 3, 3 }, 1.08f),
            P("ten_5_5", 10, new[] { 5, 5 }, 1.14f), P("ten_4_3_3", 10, new[] { 4, 3, 3 }, 1.12f),
            P("eleven_4_4_3", 11, new[] { 4, 4, 3 }, 1.17f), P("eleven_5_3_3", 11, new[] { 5, 3, 3 }, 1.17f),
            P("twelve_4_4_4", 12, new[] { 4, 4, 4 }, 1.20f), P("twelve_5_4_3", 12, new[] { 5, 4, 3 }, 1.20f)
        };

        private static BoardLayoutPreset P(string id, int total, int[] rows, float camera) => new BoardLayoutPreset
        { presetId = id, totalPositions = total, boltsPerRow = rows, cameraSizeMultiplier = camera };
    }

    /// <summary>
    /// Inspector settings for the local-space staggered perspective bolt grid.
    /// </summary>
    [Serializable]
    public sealed class BoltGridLayoutSettings
    {
        [Header("Perspective Grid")]
        [Min(1)] public int MaximumColumns = 5;
        [Min(0.01f)] public float HorizontalSpacing = 2.2f;
        [FormerlySerializedAs("VerticalSpacing"), Min(0.01f)] public float RowDepthSpacing = 2.8f;
        public float AlternateRowHorizontalOffset = 0f;
        public Vector3 GridCenterOffset = new Vector3(0f, 0f, -0.25f);
        public bool FillBackRowFirst = true;
        public float OptionalRowHeightOffset = 0f;
    }

    /// <summary>
    /// Reusable local-space staggered perspective placement for ordered bolt root transforms.
    /// </summary>
    public static class BoltGridLayout
    {
        public static void Apply(IList<Transform> orderedBoltRoots, BoltGridLayoutSettings settings)
        {
            if (orderedBoltRoots == null || settings == null) return;

            var activeRoots = new List<Transform>(orderedBoltRoots.Count);
            foreach (var root in orderedBoltRoots)
                if (root != null && root.gameObject.activeInHierarchy)
                    activeRoots.Add(root);

            if (activeRoots.Count == 0) return;

            int maximumColumns = Mathf.Max(1, settings.MaximumColumns);
            int rowCount = activeRoots.Count <= maximumColumns && activeRoots.Count <= 4
                ? 1
                : Mathf.CeilToInt(activeRoots.Count / (float)maximumColumns);

            // Five through ten bolts intentionally use two balanced rows (3+2 through 5+5).
            // Larger boards use the fewest rows allowed by Maximum Columns, with no isolated tail.
            if (activeRoots.Count > 4) rowCount = Mathf.Max(2, rowCount);

            int baseBoltsPerRow = activeRoots.Count / rowCount;
            int rowsWithOneExtraBolt = activeRoots.Count % rowCount;
            float rawStaggerAverage = 0f;
            for (int row = 0; row < rowCount; row++)
                rawStaggerAverage += (row & 1) == 0 ? 0f : settings.AlternateRowHorizontalOffset;
            rawStaggerAverage /= rowCount;

            int nextRoot = 0;
            for (int fillRow = 0; fillRow < rowCount; fillRow++)
            {
                int boltsInRow = baseBoltsPerRow + (fillRow < rowsWithOneExtraBolt ? 1 : 0);
                int visualRow = settings.FillBackRowFirst ? fillRow : rowCount - 1 - fillRow;
                float rowWidth = (boltsInRow - 1) * settings.HorizontalSpacing;
                float rawStagger = (visualRow & 1) == 0 ? 0f : settings.AlternateRowHorizontalOffset;
                float rowX = settings.GridCenterOffset.x + rawStagger - rawStaggerAverage;
                float rowZ = settings.GridCenterOffset.z + (rowCount - 1) * settings.RowDepthSpacing * .5f - visualRow * settings.RowDepthSpacing;
                float rowY = settings.GridCenterOffset.y + (rowCount - 1) * settings.OptionalRowHeightOffset * .5f - visualRow * settings.OptionalRowHeightOffset;

                for (int column = 0; column < boltsInRow; column++)
                {
                    // Set only local position. Rotation, scale, child transforms, and hierarchy remain untouched.
                    activeRoots[nextRoot++].localPosition = new Vector3(
                        rowX - rowWidth * .5f + column * settings.HorizontalSpacing,
                        rowY,
                        rowZ);
                }
            }
        }

        /// <summary>Places each explicit row around its own centre; no row inherits another row's width.</summary>
        public static void Apply(IList<Transform> orderedBoltRoots, BoardLayoutPreset preset)
        {
            if (orderedBoltRoots == null || preset == null || preset.boltsPerRow == null) return;
            var active = new List<Transform>();
            foreach (var root in orderedBoltRoots)
                if (root != null && root.gameObject.activeInHierarchy) active.Add(root);
            if (active.Count != preset.totalPositions) return;

            int next = 0;
            for (int row = 0; row < preset.boltsPerRow.Length; row++)
            {
                int count = preset.boltsPerRow[row];
                float width = (count - 1) * preset.horizontalSpacing;
                float z = preset.centerOffset.z + (preset.boltsPerRow.Length - 1) * preset.rowDepthSpacing * .5f - row * preset.rowDepthSpacing;
                float y = preset.centerOffset.y + (preset.boltsPerRow.Length - 1) * preset.rowHeightOffset * .5f - row * preset.rowHeightOffset;
                for (int column = 0; column < count; column++)
                    active[next++].localPosition = new Vector3(preset.centerOffset.x - width * .5f + column * preset.horizontalSpacing, y, z);
            }
        }
    }
}
