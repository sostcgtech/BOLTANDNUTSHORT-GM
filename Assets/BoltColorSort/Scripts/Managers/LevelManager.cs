using System.Collections.Generic;
using UnityEngine;

namespace NutBoltSort
{
    /// <summary>
    /// LevelManager handles level lifecycle, prefab instantiation under BoardRoot, level data validation, and grid positioning.
    /// Includes procedural mesh fallbacks if prefabs are not yet assigned in the Inspector.
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        [Header("Prefabs & Hierarchy")]
        [SerializeField] private BoltView boltPrefab;
        [SerializeField] private NutView nutPrefab;
        [Tooltip("Shared baked URP/Lit material used by the procedural fallback when NutPrefab is unavailable.")]
        [SerializeField] private Material nutMasterMaterial;
        [SerializeField] private Transform boardRoot;

        [Header("Grid Layout Settings")]
        [SerializeField] private BoltGridLayoutSettings gridLayoutSettings = new BoltGridLayoutSettings();

        [Header("Level Data Assets")]
        [SerializeField] private List<LevelDataSO> levelDataList = new List<LevelDataSO>();

        private readonly Dictionary<string, Material> customMaterials = new Dictionary<string, Material>();
        private Material metalMaterial;
        private readonly List<BoltView> activeBolts = new List<BoltView>();

        public IReadOnlyList<BoltView> ActiveBolts => activeBolts;
        /// <summary>Returns the logical board index without exposing the mutable bolt list.</summary>
        public int IndexOfBolt(BoltView bolt) => bolt != null ? activeBolts.IndexOf(bolt) : -1;
        public BoltGridLayoutSettings GridLayoutSettings => gridLayoutSettings;
        public int TotalLevels
        {
            get
            {
                EnsureLevelDataList();
                return levelDataList != null ? levelDataList.Count : 0;
            }
        }

        private void Awake()
        {
            EnsureBoardRoot();
            EnsureLevelDataList();
        }

        private void EnsureLevelDataList()
        {
            if (levelDataList == null || levelDataList.Count == 0)
            {
                var loadedLevels = Resources.LoadAll<LevelDataSO>("Levels");
                if (loadedLevels != null && loadedLevels.Length > 0)
                {
                    levelDataList = new List<LevelDataSO>(loadedLevels);
                    levelDataList.Sort((a, b) => a.levelNumber.CompareTo(b.levelNumber));
                }
            }

            if (levelDataList == null || levelDataList.Count == 0)
            {
                CreateFallbackLevelData();
            }
        }

        private void EnsureBoardRoot()
        {
            if (boardRoot != null) return;

            var board = GameObject.Find("BoardRoot");
            if (board != null)
            {
                boardRoot = board.transform;
            }
            else
            {
                var group = GameObject.Find("01_Board");
                if (group != null)
                {
                    var child = group.transform.Find("BoardRoot");
                    if (child != null)
                    {
                        boardRoot = child.transform;
                    }
                    else
                    {
                        boardRoot = new GameObject("BoardRoot").transform;
                        boardRoot.SetParent(group.transform);
                    }
                }
                else
                {
                    var legacy = GameObject.Find("01_Bolts — Bottom (move bolt roots to arrange)");
                    if (legacy != null)
                    {
                        boardRoot = legacy.transform;
                    }
                    else
                    {
                        boardRoot = new GameObject("BoardRoot").transform;
                    }
                }
            }

            // Disable old legacy scene bolts container if running side-by-side
            var legacyGroup = GameObject.Find("01_Bolts — Bottom (move bolt roots to arrange)");
            if (legacyGroup != null && legacyGroup.transform != boardRoot)
            {
                legacyGroup.SetActive(false);
            }
        }

        public bool BuildLevel(int levelIndex, out LevelDataSO loadedData)
        {
            loadedData = null;
            EnsureBoardRoot();
            EnsureLevelDataList();

            if (levelDataList == null || levelDataList.Count == 0)
            {
                Debug.LogError("[LevelManager] Error: No LevelDataSO assets configured.");
                return false;
            }

            int index = Mathf.Abs(levelIndex) % levelDataList.Count;
            LevelDataSO data = levelDataList[index];

            return BuildLevel(data, out loadedData);
        }

        /// <summary>Builds supplied logical data unchanged; procedural generation never assigns positions.</summary>
        public bool BuildLevel(LevelDataSO data, out LevelDataSO loadedData)
        {
            loadedData = null;
            EnsureBoardRoot();
            if (!ValidateLevelData(data))
            {
                Debug.LogError("[LevelManager] Level loading aborted: LevelDataSO validation failed.");
                return false;
            }

            // Clear previous board root
            ClearBoard();

            loadedData = data;

            // Spawn Bolts and Nuts from Level Data
            for (int i = 0; i < data.bolts.Count; i++)
            {
                var boltData = data.bolts[i];
                BoltView bolt = SpawnBolt(i);
                if (bolt == null) continue;

                activeBolts.Add(bolt);

                // ── Attach special-bolt controller based on bolt type ────────
                if (boltData != null)
                {
                    switch (boltData.boltType)
                    {
                        case BoltType.Expandable:
                            var exp = bolt.gameObject.AddComponent<ExpandableBoltController>();
                            exp.Initialize(boltData.expandableStartCapacity);
                            break;
                    }
                }

                if (boltData != null && boltData.nutColors != null)
                {
                    for (int slot = 0; slot < boltData.nutColors.Length; slot++)
                    {
                        NutColor nutColor = boltData.nutColors[slot];
                        bool startsHidden = boltData.startsHidden != null && slot < boltData.startsHidden.Length && boltData.startsHidden[slot];
                        NutView nut = SpawnNut(bolt, nutColor, slot, startsHidden);
                        if (nut != null)
                        {
                            bolt.Nuts.Add(nut);
                        }
                    }
                }

                // Starting top nuts are always visible, even if malformed data flagged one hidden.
                if (bolt.Nuts.Count > 0) bolt.Nuts[bolt.Nuts.Count - 1].RevealSilently();
            }

            // Apply staggered perspective layout
            ApplyGridLayout(data);

            // Silently lock pre-completed bolts on load
            foreach (var bolt in activeBolts)
            {
                if (bolt != null && bolt.IsComplete())
                {
                    bolt.LockBoltSilently();
                }
            }

            return true;
        }

        public void ClearBoard()
        {
            EnsureBoardRoot();
            if (boardRoot != null)
            {
                for (int i = boardRoot.childCount - 1; i >= 0; i--)
                {
                    var child = boardRoot.GetChild(i);
                    if (child != null)
                    {
                        Destroy(child.gameObject);
                    }
                }
            }
            activeBolts.Clear();
        }

        public void ApplyGridLayout() => ApplyGridLayout(null);

        public void ApplyGridLayout(LevelDataSO data)
        {
            var boltTransforms = new List<Transform>(activeBolts.Count);
            foreach (var bolt in activeBolts)
            {
                if (bolt != null) boltTransforms.Add(bolt.transform);
            }

            BoardLayoutPreset preset = data != null && data.isProcedural
                ? BoardLayoutPresets.Find(data.layoutPresetId) : null;
            if (preset != null)
            {
                // Procedural boards keep their explicit row shape, but deliberately inherit
                // the same spacing and centering controls used by Levels 1–5.
                var resolvedPreset = new BoardLayoutPreset
                {
                    presetId = preset.presetId,
                    totalPositions = preset.totalPositions,
                    boltsPerRow = preset.boltsPerRow,
                    horizontalSpacing = gridLayoutSettings.HorizontalSpacing,
                    rowDepthSpacing = gridLayoutSettings.RowDepthSpacing,
                    rowHeightOffset = gridLayoutSettings.OptionalRowHeightOffset,
                    centerOffset = gridLayoutSettings.GridCenterOffset,
                    cameraSizeMultiplier = 1f
                };
                BoltGridLayout.Apply(boltTransforms, resolvedPreset);
            }
            else BoltGridLayout.Apply(boltTransforms, gridLayoutSettings);

            foreach (var bolt in activeBolts)
            {
                if (bolt != null)
                {
                    bolt.HomePosition = bolt.transform.position;
                }
            }

        }

        private BoltView SpawnBolt(int boltIndex)
        {
            EnsureBoardRoot();

            if (boltPrefab != null)
            {
                BoltView boltInstance = Instantiate(boltPrefab, boardRoot);
                boltInstance.gameObject.name = $"Bolt_{boltIndex + 1}";
                boltInstance.transform.localPosition = Vector3.zero;
                boltInstance.transform.localRotation = Quaternion.identity;
                boltInstance.transform.localScale = Vector3.one;
                boltInstance.BoltIndex = boltIndex;
                boltInstance.ResetState();
                return boltInstance;
            }

            // Procedural Fallback Bolt when boltPrefab is unassigned
            var root = new GameObject($"Bolt_{boltIndex + 1}");
            root.transform.SetParent(boardRoot);
            root.transform.localPosition = Vector3.zero;

            var hit = root.AddComponent<BoxCollider>();
            hit.center = new Vector3(0, 1.25f, 0);
            hit.size = new Vector3(1.35f, 3.3f, 1.35f);

            Material metal = GetOrCreateMetalMaterial();
            CreatePrimitive(PrimitiveType.Cylinder, "Bolt shaft", root.transform, new Vector3(0, 1.2f, 0), new Vector3(.26f, 1.25f, .26f), metal);
            CreatePrimitive(PrimitiveType.Cylinder, "Bolt base", root.transform, new Vector3(0, .12f, 0), new Vector3(.68f, .12f, .68f), metal);

            var nutContainer = new GameObject("NutContainer");
            nutContainer.transform.SetParent(root.transform);
            nutContainer.transform.localPosition = Vector3.zero;

            var boltView = root.AddComponent<BoltView>();
            boltView.BoltIndex = boltIndex;
            boltView.ResetState();
            return boltView;
        }

        private NutView SpawnNut(BoltView bolt, NutColor color, int slotIndex, bool startsHidden)
        {
            Material mat = GetNutMasterMaterial();
            Vector3 localPos = bolt.GetStackPosition(slotIndex);
            Transform container = bolt.NutContainer;

            if (nutPrefab != null)
            {
                NutView nutInstance = Instantiate(nutPrefab, container);
                nutInstance.gameObject.name = $"{color}_Nut";
                nutInstance.transform.localPosition = localPos;
                nutInstance.transform.localRotation = Quaternion.identity;
                // Keep the prefab's authored root scale. NutPrefab uses this scale to
                // size its imported mesh; forcing Vector3.one made nuts appear tiny
                // until the first landing animation restored their cached prefab scale.
                nutInstance.CaptureRestingTransform();
                nutInstance.Initialize(color, startsHidden);
                return nutInstance;
            }

            // Procedural Fallback Nut when nutPrefab is unassigned
            var n = new GameObject($"{color}_Nut");
            n.transform.SetParent(container);
            n.transform.localPosition = localPos;

            var body = CreatePrimitive(PrimitiveType.Cylinder, "Nut ring", n.transform, Vector3.zero, new Vector3(.62f, .19f, .62f), mat);
            if (body.TryGetComponent<Collider>(out var c1)) c1.enabled = false;

            var bevel = CreatePrimitive(PrimitiveType.Cylinder, "Nut bevel", n.transform, new Vector3(0, .13f, 0), new Vector3(.51f, .07f, .51f), mat);
            if (bevel.TryGetComponent<Collider>(out var c2)) c2.enabled = false;

            var nutView = n.AddComponent<NutView>();
            nutView.Initialize(color, startsHidden);
            return nutView;
        }

        private GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var o = GameObject.CreatePrimitive(type);
            o.name = name;
            o.transform.SetParent(parent);
            o.transform.localPosition = pos;
            o.transform.localScale = scale;
            if (o.TryGetComponent<Renderer>(out var r))
            {
                r.sharedMaterial = mat;
            }
            return o;
        }

        private Material GetOrCreateMetalMaterial()
        {
            if (metalMaterial != null) return metalMaterial;
            metalMaterial = GetOrCreateCustomMaterial("BoltMetal", new Color(.30f, .36f, .46f), .78f);
            return metalMaterial;
        }

        private Material GetOrCreateCustomMaterial(string key, Color color, float metallic)
        {
            if (customMaterials.TryGetValue(key, out var mat) && mat != null) return mat;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { color = color };
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", .63f);
            customMaterials[key] = mat;
            return mat;
        }

        private Material GetNutMasterMaterial()
        {
            if (nutMasterMaterial != null) return nutMasterMaterial;

            Renderer prefabRenderer = nutPrefab != null ? nutPrefab.GetComponentInChildren<Renderer>() : null;
            if (prefabRenderer != null) return prefabRenderer.sharedMaterial;

            Debug.LogError("[LevelManager] NutPrefab or M_Nut_Master must be assigned before spawning procedural nuts.", this);
            return null;
        }

        public bool ValidateLevelData(LevelDataSO data)
        {
            if (data == null)
            {
                Debug.LogError("[LevelManager] Validation Failed: LevelDataSO asset is null.");
                return false;
            }

            if (data.bolts == null || data.bolts.Count == 0)
            {
                Debug.LogError($"[LevelManager] Validation Failed: Level {data.levelNumber} contains no bolt stacks.");
                return false;
            }

            foreach (var boltData in data.bolts)
            {
                if (boltData == null)
                {
                    Debug.LogError($"[LevelManager] Validation Failed: Level {data.levelNumber} contains a null bolt stack.");
                    return false;
                }
                if (boltData.nutColors != null && boltData.nutColors.Length > BoltView.Capacity)
                {
                    Debug.LogError($"[LevelManager] Validation Failed: Bolt stack exceeds capacity ({boltData.nutColors.Length} > {BoltView.Capacity}) in Level {data.levelNumber}.");
                    return false;
                }
                if (boltData.startsHidden != null && boltData.startsHidden.Length > 0 &&
                    (boltData.nutColors == null || boltData.startsHidden.Length != boltData.nutColors.Length))
                {
                    Debug.LogError($"[LevelManager] Validation Failed: hidden-state count does not match nut count in Level {data.levelNumber}.");
                    return false;
                }
            }

            return true;
        }

        private void CreateFallbackLevelData()
        {
            // Level 1
            var l1 = ScriptableObject.CreateInstance<LevelDataSO>();
            l1.levelNumber = 1;
            l1.bolts = new List<BoltNutStackData>
            {
                new BoltNutStackData { nutColors = new[] { NutColor.Red, NutColor.Green, NutColor.Blue, NutColor.Yellow } },
                new BoltNutStackData { nutColors = new[] { NutColor.Green, NutColor.Blue, NutColor.Yellow, NutColor.Red } },
                new BoltNutStackData { nutColors = new[] { NutColor.Blue, NutColor.Yellow, NutColor.Red, NutColor.Green } },
                new BoltNutStackData { nutColors = new[] { NutColor.Yellow, NutColor.Red, NutColor.Green, NutColor.Blue } },
                new BoltNutStackData { nutColors = System.Array.Empty<NutColor>() },
                new BoltNutStackData { nutColors = System.Array.Empty<NutColor>() }
            };

            // Level 2
            var l2 = ScriptableObject.CreateInstance<LevelDataSO>();
            l2.levelNumber = 2;
            l2.bolts = new List<BoltNutStackData>
            {
                new BoltNutStackData { nutColors = new[] { NutColor.Red, NutColor.Blue, NutColor.Green, NutColor.Yellow } },
                new BoltNutStackData { nutColors = new[] { NutColor.Yellow, NutColor.Green, NutColor.Blue, NutColor.Red } },
                new BoltNutStackData { nutColors = new[] { NutColor.Green, NutColor.Yellow, NutColor.Red, NutColor.Blue } },
                new BoltNutStackData { nutColors = new[] { NutColor.Blue, NutColor.Red, NutColor.Yellow, NutColor.Green } },
                new BoltNutStackData { nutColors = System.Array.Empty<NutColor>() },
                new BoltNutStackData { nutColors = System.Array.Empty<NutColor>() }
            };

            // Level 3
            var l3 = ScriptableObject.CreateInstance<LevelDataSO>();
            l3.levelNumber = 3;
            l3.bolts = new List<BoltNutStackData>
            {
                new BoltNutStackData { nutColors = new[] { NutColor.Red, NutColor.Green, NutColor.Yellow, NutColor.Blue } },
                new BoltNutStackData { nutColors = new[] { NutColor.Blue, NutColor.Yellow, NutColor.Green, NutColor.Red } },
                new BoltNutStackData { nutColors = new[] { NutColor.Green, NutColor.Red, NutColor.Blue, NutColor.Yellow } },
                new BoltNutStackData { nutColors = new[] { NutColor.Yellow, NutColor.Blue, NutColor.Red, NutColor.Green } },
                new BoltNutStackData { nutColors = System.Array.Empty<NutColor>() },
                new BoltNutStackData { nutColors = System.Array.Empty<NutColor>() }
            };

            levelDataList = new List<LevelDataSO> { l1, l2, l3 };
        }
    }
}
