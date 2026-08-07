using UnityEngine;

namespace NutBoltSort
{
    /// <summary>
    /// Editor-only developer settings for the boot/splash system.
    ///
    /// Create via: Assets → Create → NutBoltSort → Boot Debug Settings
    ///
    /// Assign to the GameBootstrap component in the 00_Boot scene inspector.
    /// These values are only read in UNITY_EDITOR builds; shipping builds
    /// ignore this asset entirely.
    /// </summary>
    [CreateAssetMenu(fileName = "BootDebugSettings",
                     menuName  = "NutBoltSort/Boot Debug Settings",
                     order     = 10)]
    public sealed class BootDebugSettings : ScriptableObject
    {
        [Header("Editor Developer Options")]
        [Tooltip("Adds a simulated 0.3 s delay after each boot task so the loading bar " +
                 "is visible long enough to inspect. Has no effect in release builds.")]
        public bool simulateSlowLoading = false;

        [Tooltip("Clears all PlayerPrefs on Play so startup behaves as a first launch. " +
                 "Has no effect in release builds.")]
        public bool forceFirstLaunch = false;

        [Tooltip("Skips all startup tasks and loads Main Menu immediately. " +
                 "Managers are still initialised. Has no effect in release builds.")]
        public bool skipSplash = false;

        [Tooltip("Prints per-task timing and total boot duration to the Console.")]
        public bool printStartupTiming = true;

#if UNITY_EDITOR
        /// <summary>
        /// Called by GameBootstrap at the very start of the boot sequence (editor only).
        /// Applies one-time developer overrides such as clearing PlayerPrefs.
        /// </summary>
        public void ApplyEditorOverrides()
        {
            if (forceFirstLaunch)
            {
                PlayerPrefs.DeleteAll();
                PlayerPrefs.Save();
                Debug.LogWarning("[BootDebugSettings] Force First Launch — PlayerPrefs cleared.");
            }
        }
#endif
    }
}
