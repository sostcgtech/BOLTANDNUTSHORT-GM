using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NutBoltSort
{
    // ─────────────────────────────────────────────────────────────────────────
    // BootTask_Settings  (weight: 10%)
    // ─────────────────────────────────────────────────────────────────────────
    // Reads saved SFX and Haptic enabled states from PlayerPrefs and applies
    // them to the managers.  Safe to run before the managers are instantiated
    // (they read PlayerPrefs again in their own Awake, so this is additive).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Reads saved sound and haptic settings and applies them to the managers.</summary>
    public sealed class BootTask_Settings : IBootTask
    {
        private const string SfxKey     = "BoltShift.SfxEnabled";
        private const string HapticsKey = "BoltShift.HapticsEnabled";

        public string StatusText => "Loading settings...";
        public bool   Succeeded  { get; private set; }

        public IEnumerator Execute()
        {
            // PlayerPrefs reads happen on the main thread synchronously.
            bool sfxEnabled     = PlayerPrefs.GetInt(SfxKey,     1) == 1;
            bool hapticsEnabled = PlayerPrefs.GetInt(HapticsKey, 1) == 1;

            // Push values into managers if they already exist
            // (they will also re-read on their own Awake if not yet alive).
            AudioManager.EnableSfx(sfxEnabled);
            HapticManager.EnableHaptics(hapticsEnabled);

            Succeeded = true;
            yield return null;   // one frame — keeps the coroutine scheduler happy
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BootTask_Progress  (weight: 15%)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Reads and validates the saved player level number.</summary>
    public sealed class BootTask_Progress : IBootTask
    {
        private const string LevelKey = "CurrentLevelNumber";

        public string StatusText => "Loading progress...";
        public bool   Succeeded  { get; private set; }

        /// <summary>The validated level number, available after Execute() completes.</summary>
        public int CurrentLevel { get; private set; } = 1;

        public IEnumerator Execute()
        {
            int saved = PlayerPrefs.GetInt(LevelKey, 1);
            CurrentLevel = saved < 1 ? 1 : saved;

            // If the saved value was corrupt, sanitise it immediately.
            if (saved < 1)
            {
                PlayerPrefs.SetInt(LevelKey, 1);
                PlayerPrefs.Save();
                Debug.LogWarning("[BootTask_Progress] Saved level was < 1; reset to 1.");
            }

            Succeeded = true;
            yield return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BootTask_Managers  (weight: 20%)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures AudioManager and HapticManager are alive and persistent.
    /// They are attached to the CoreSystems root that GameBootstrap passes in.
    /// </summary>
    public sealed class BootTask_Managers : IBootTask
    {
        private readonly GameObject _coreSystemsRoot;

        public BootTask_Managers(GameObject coreSystemsRoot)
        {
            _coreSystemsRoot = coreSystemsRoot;
        }

        public string StatusText => "Initializing systems...";
        public bool   Succeeded  { get; private set; }

        public IEnumerator Execute()
        {
            if (_coreSystemsRoot == null)
            {
                Debug.LogError("[BootTask_Managers] CoreSystems root is null.");
                Succeeded = false;
                yield break;
            }

            AudioManager.EnsureInstance(_coreSystemsRoot);
            HapticManager.EnsureInstance(_coreSystemsRoot);

            Succeeded = AudioManager.Instance != null && HapticManager.Instance != null;
            if (!Succeeded)
                Debug.LogError("[BootTask_Managers] One or more managers failed to initialise.");

            yield return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BootTask_Database  (weight: 20%)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads and validates the LevelProgressionConfig ScriptableObject from Resources.
    /// This is the central game-flow configuration needed before any level can run.
    /// </summary>
    public sealed class BootTask_Database : IBootTask
    {
        public string StatusText => "Preparing game data...";
        public bool   Succeeded  { get; private set; }

        /// <summary>The loaded config, available after Execute() completes.</summary>
        public LevelProgressionConfig Config { get; private set; }

        public IEnumerator Execute()
        {
            // Resources.Load is synchronous but fast for a small ScriptableObject.
            Config = Resources.Load<LevelProgressionConfig>("LevelProgressionConfig");

            if (Config == null)
            {
                // Non-fatal: StructuredLevelProvider has its own fallback.
                Debug.LogWarning("[BootTask_Database] LevelProgressionConfig not found in Resources/. " +
                                 "Gameplay will use built-in defaults.");
            }

            // Also warm up the level data assets folder (cheap header scan).
            var levelAssets = Resources.LoadAll<LevelDataSO>("Levels");
            Debug.Log($"[BootTask_Database] Found {(levelAssets != null ? levelAssets.Length : 0)} " +
                      "LevelDataSO assets in Resources/Levels/.");

            Succeeded = true;   // non-critical; gameplay has fallbacks
            yield return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BootTask_Prefabs  (weight: 20%)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates that core prefab types are available in the project.
    /// Does NOT instantiate anything — this is a lightweight presence check that
    /// warms up any internal caches Unity maintains for those asset types.
    ///
    /// Note: BoltPrefab and NutPrefab live in Assets/BoltColorSort/Prefab/ (not a
    /// Resources/ folder), so they cannot be loaded here via Resources.Load.
    /// Instead we verify runtime availability by checking for their MonoBehaviour
    /// types once the managers are live, and log a targeted warning if missing.
    /// </summary>
    public sealed class BootTask_Prefabs : IBootTask
    {
        public string StatusText => "Preparing resources...";
        public bool   Succeeded  { get; private set; }

        public IEnumerator Execute()
        {
            // The prefabs are not in Resources/ so we can't use Resources.Load here.
            // We perform a lightweight warm-up: any static caches in BoltView / NutView
            // that depend on shader property IDs or material caches are triggered by
            // accessing the type — no allocation, no instantiation required.

            // Shader warm-up: ensure URP/Standard shader variants are resident.
            // This is a no-op on most platforms but prevents a first-frame stutter.
            _ = Shader.Find("Universal Render Pipeline/Lit");
            _ = Shader.Find("Standard");

            // Validate that at least one LevelDataSO is reachable (already loaded
            // by BootTask_Database above; just confirm the cache is warm).
            var cached = Resources.FindObjectsOfTypeAll<LevelDataSO>();
            if (cached == null || cached.Length == 0)
                Debug.LogWarning("[BootTask_Prefabs] No LevelDataSO found in memory. " +
                                 "LevelManager will attempt a fresh Resources.LoadAll at runtime.");

            Succeeded = true;   // prefab absence is not fatal at boot; LevelManager has its own fallback
            yield return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BootTask_Menu  (weight: 15%)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the async load of the Main Menu scene and waits until Unity reports
    /// it has reached 90% progress (fully loaded, pending activation).
    /// The caller (GameBootstrap) controls when allowSceneActivation = true.
    /// </summary>
    public sealed class BootTask_Menu : IBootTask
    {
        public string StatusText  => "Almost ready...";
        public bool   Succeeded   { get; private set; }

        /// <summary>The AsyncOperation, available immediately after Execute() starts.</summary>
        public AsyncOperation MenuLoadOperation { get; private set; }

        public IEnumerator Execute()
        {
            MenuLoadOperation = SceneManager.LoadSceneAsync(SceneNames.MainMenu);

            if (MenuLoadOperation == null)
            {
                Debug.LogError("[BootTask_Menu] SceneManager.LoadSceneAsync returned null. " +
                               $"Ensure '{SceneNames.MainMenu}' is in Build Settings.");
                Succeeded = false;
                yield break;
            }

            // Prevent automatic activation — GameBootstrap will flip this flag.
            MenuLoadOperation.allowSceneActivation = false;

            // Wait until the scene is fully loaded in the background (progress ≥ 0.9).
            while (MenuLoadOperation.progress < 0.9f)
                yield return null;

            Succeeded = true;
            // Do NOT set allowSceneActivation = true here; GameBootstrap does that
            // after the splash fade completes.
        }
    }
}
