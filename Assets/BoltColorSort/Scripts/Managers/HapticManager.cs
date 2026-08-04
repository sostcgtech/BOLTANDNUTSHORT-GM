using UnityEngine;

namespace NutBoltSort
{
    public enum HapticType { Light, Medium, Heavy, Success }

    [DisallowMultipleComponent]
    public sealed class HapticManager : MonoBehaviour
    {
        private const string HapticsEnabledKey = "BoltShift.HapticsEnabled";
        public static HapticManager Instance { get; private set; }
        [Header("Haptics")]
        [SerializeField] private bool enableHaptics = true;
        [SerializeField, Min(0f)] private float minimumRepeatDelay = .05f;
        [SerializeField] private bool debugLogging;
        private float lastVibrationTime;

        public static HapticManager EnsureInstance(GameObject host)
        {
            if (Instance != null) return Instance;
            Instance = host.GetComponent<HapticManager>();
            if (Instance == null) Instance = host.AddComponent<HapticManager>();
            return Instance;
        }
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            enableHaptics = PlayerPrefs.GetInt(HapticsEnabledKey, 1) == 1;
        }
        public static void Vibrate(HapticType type) => Instance?.VibrateInternal(type);
        public static void EnableHaptics(bool enabled)
        {
            if (Instance == null) return;
            Instance.enableHaptics = enabled;
            PlayerPrefs.SetInt(HapticsEnabledKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }
        private void VibrateInternal(HapticType type)
        {
            if (!enableHaptics || Time.unscaledTime - lastVibrationTime < minimumRepeatDelay) return;
            lastVibrationTime = Time.unscaledTime;
#if UNITY_ANDROID && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
            if (debugLogging) Debug.Log($"[HapticManager] {type}", this);
        }
    }
}
