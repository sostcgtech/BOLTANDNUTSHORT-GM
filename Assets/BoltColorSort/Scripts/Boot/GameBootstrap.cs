using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NutBoltSort
{
    /// <summary>
    /// Central startup coordinator for the 00_Boot scene.
    ///
    /// Responsibilities:
    ///   - Owns the ordered list of IBootTask objects and their weight fractions.
    ///   - Executes tasks sequentially, reporting weighted actual progress to SplashScreenController.
    ///   - Creates the persistent CoreSystems root (DontDestroyOnLoad) that hosts
    ///     AudioManager and HapticManager.
    ///   - Starts the MainMenu async load early and keeps it held until everything is ready.
    ///   - Signals SplashScreenController to fade out and activate the MainMenu.
    ///
    /// Add this component to the GameBootstrap GameObject inside the 00_Boot scene.
    /// Assign the SplashScreenController reference in the Inspector.
    /// </summary>
    [AddComponentMenu("BoltShift/Boot/Game Bootstrap")]
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        [Header("References")]
        [Tooltip("The SplashScreenController that drives the loading bar UI.")]
        [SerializeField] private SplashScreenController splashScreen;

        [Header("Timing")]
        [Tooltip("Minimum time (seconds) the splash screen must remain visible, even if loading is instant.")]
        [SerializeField, Range(0.5f, 3f)] private float minimumSplashDuration = 1.2f;

        [Header("Debug")]
        [Tooltip("Enables verbose startup timing logs to the Console. Disable in release builds.")]
        [SerializeField] private bool enableDebugLogs = false;

        [Tooltip("Editor only: adds a simulated delay per task to test the loading bar.")]
        [SerializeField] private bool simulateSlowLoading = false;

        [Tooltip("Editor only: force-skips the splash and loads Main Menu immediately. Never ships.")]
        [SerializeField] private bool skipSplashInEditor = false;

        // ─────────────────────────────────────────────────────────────────────
        // Private state
        // ─────────────────────────────────────────────────────────────────────

        // Weighted task descriptors — (task, weight).
        // Weights do not need to sum to exactly 1; they are normalised at runtime.
        private readonly (float weight, Func<IBootTask> factory)[] _taskDefs = null;

        private float _totalWeight;
        private float _actualProgress;

        private BootTask_Menu    _menuTask;     // held for final activation
        private GameObject       _coreRoot;     // DontDestroyOnLoad host

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;

            // Create the persistent CoreSystems root up-front so managers that
            // are created during boot tasks are immediately persistent.
            _coreRoot = new GameObject("CoreSystems");
            DontDestroyOnLoad(_coreRoot);
        }

        private void Start()
        {
#if UNITY_EDITOR
            if (skipSplashInEditor)
            {
                Debug.LogWarning("[GameBootstrap] Skip Splash is enabled — do not ship with this active!");
                StartCoroutine(SkipToMainMenuImmediate());
                return;
            }
#endif
            StartCoroutine(RunBootSequence());
        }

        // ─────────────────────────────────────────────────────────────────────
        // Boot Sequence
        // ─────────────────────────────────────────────────────────────────────

        private IEnumerator RunBootSequence()
        {
            float bootStartTime = Time.realtimeSinceStartup;

            // ── Build the ordered task list ──────────────────────────────────
            // Instantiated here (not in a field initialiser) so _coreRoot is ready.
            _menuTask = new BootTask_Menu();

            var tasks = new List<(float weight, IBootTask task)>
            {
                (0.10f, new BootTask_Settings()),
                (0.15f, new BootTask_Progress()),
                (0.20f, new BootTask_Managers(_coreRoot)),
                (0.20f, new BootTask_Database()),
                (0.20f, new BootTask_Prefabs()),
                (0.15f, _menuTask),
            };

            // Normalise weights.
            _totalWeight = 0f;
            foreach (var (w, _) in tasks) _totalWeight += w;
            if (_totalWeight <= 0f) _totalWeight = 1f;

            _actualProgress = 0f;
            float completedWeight = 0f;

            // ── Execute tasks ────────────────────────────────────────────────
            foreach (var (weight, task) in tasks)
            {
                if (enableDebugLogs)
                    Debug.Log($"[GameBootstrap] Starting: {task.GetType().Name}  ({task.StatusText})");

                float taskStart = Time.realtimeSinceStartup;

                splashScreen?.SetStatus(task.StatusText);

                yield return StartCoroutine(task.Execute());

#if UNITY_EDITOR
                if (simulateSlowLoading)
                    yield return new WaitForSecondsRealtime(0.3f);
#endif

                if (!task.Succeeded)
                {
                    Debug.LogWarning($"[GameBootstrap] Task {task.GetType().Name} did not succeed — " +
                                     "continuing with defaults.");
                }

                completedWeight += weight;
                _actualProgress  = completedWeight / _totalWeight;
                splashScreen?.SetActualProgress(_actualProgress);

                if (enableDebugLogs)
                {
                    float elapsed = Time.realtimeSinceStartup - taskStart;
                    Debug.Log($"[GameBootstrap] Completed: {task.GetType().Name}  " +
                              $"({elapsed * 1000f:F0} ms)  progress={_actualProgress:P0}");
                }
            }

            // ── Wait for minimum splash duration ─────────────────────────────
            float elapsed2 = Time.realtimeSinceStartup - bootStartTime;
            float remaining = minimumSplashDuration - elapsed2;
            if (remaining > 0f)
                yield return new WaitForSecondsRealtime(remaining);

            // ── Verify menu task succeeded before activating ──────────────────
            if (_menuTask.MenuLoadOperation == null || !_menuTask.Succeeded)
            {
                Debug.LogError("[GameBootstrap] MainMenu failed to load. Cannot proceed.");
                yield break;
            }

            float totalTime = Time.realtimeSinceStartup - bootStartTime;
            if (enableDebugLogs)
                Debug.Log($"[GameBootstrap] Boot complete in {totalTime * 1000f:F0} ms. Activating MainMenu.");

            // ── Signal splash to fade out, then activate the scene ───────────
            if (splashScreen != null)
                yield return StartCoroutine(splashScreen.FadeOutAndActivate(_menuTask.MenuLoadOperation));
            else
                _menuTask.MenuLoadOperation.allowSceneActivation = true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Editor skip path
        // ─────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
        private IEnumerator SkipToMainMenuImmediate()
        {
            // Still initialise the core managers even when skipping, so the
            // rest of the game functions normally.
            AudioManager.EnsureInstance(_coreRoot);
            HapticManager.EnsureInstance(_coreRoot);

            var op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(SceneNames.MainMenu);
            if (op != null) op.allowSceneActivation = true;
            yield return op;
        }
#endif
    }
}
