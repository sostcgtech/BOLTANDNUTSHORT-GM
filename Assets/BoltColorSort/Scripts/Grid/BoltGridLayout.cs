using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace NutBoltSort
{
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
    }
}
