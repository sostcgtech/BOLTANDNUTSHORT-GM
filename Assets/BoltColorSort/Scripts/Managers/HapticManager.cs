using UnityEngine;

namespace NutBoltSort
{
    public enum HapticType { Light, Medium, Heavy, Success }

    /// <summary>Cached native Android haptics with a safe legacy fallback for pre-API-26 devices.</summary>
    [DisallowMultipleComponent]
    public sealed class HapticManager : MonoBehaviour
    {
        private const string HapticsEnabledKey = "BoltShift.HapticsEnabled";
        private const int ApiVibrationEffect = 26;
        private const int ApiPredefinedEffects = 29;
        private const int EffectClick = 0;
        private const int EffectTick = 2;
        private const int EffectHeavyClick = 5;

        public static HapticManager Instance { get; private set; }
        [Header("Haptics")]
        [SerializeField] private bool enableHaptics = true;
        [SerializeField] private bool useNativeAndroidHaptics = true;
        [SerializeField] private bool useLegacyFallback = true;
        [Tooltip("1 uses Android's native predefined effects. Other values use scaled native VibrationEffects.")]
        [SerializeField, Range(.25f, 2f)] private float hapticIntensity = 1f;
        [SerializeField, Min(0f)] private float minimumRepeatDelay = .05f;
        [SerializeField] private bool debugHaptics;
        private float lastVibrationTime;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject activity;
        private AndroidJavaObject vibrator;
        private AndroidJavaClass vibrationEffectClass;
        private AndroidJavaObject lightEffect;
        private AndroidJavaObject mediumEffect;
        private AndroidJavaObject heavyEffect;
        private AndroidJavaObject successEffect;
        private int androidApiLevel;
        private float cachedHapticIntensity = -1f;
#endif

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
#if UNITY_ANDROID && !UNITY_EDITOR
            InitializeNativeBridge();
#endif
        }

        public static void Play(HapticType type) => Instance?.PlayInternal(type);

        public static void EnableHaptics(bool enabled)
        {
            if (Instance == null) return;
            Instance.enableHaptics = enabled;
            PlayerPrefs.SetInt(HapticsEnabledKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void PlayInternal(HapticType type)
        {
            if (!enableHaptics || Time.unscaledTime - lastVibrationTime < minimumRepeatDelay) return;
            lastVibrationTime = Time.unscaledTime;
#if UNITY_ANDROID && !UNITY_EDITOR
            PlayAndroid(type);
#endif
            if (debugHaptics) Debug.Log($"[HapticManager] {type}", this);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void InitializeNativeBridge()
        {
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    androidApiLevel = version.GetStatic<int>("SDK_INT");
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                vibrator = activity?.Call<AndroidJavaObject>("getSystemService", "vibrator");
                if (vibrator == null || !vibrator.Call<bool>("hasVibrator")) return;
                if (androidApiLevel >= ApiVibrationEffect)
                {
                    vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect");
                    CacheEffects();
                }
            }
            catch (System.Exception exception)
            {
                if (debugHaptics) Debug.LogWarning("[HapticManager] Native Android haptics unavailable: " + exception.Message, this);
                DisposeNativeBridge();
            }
        }

        private void CacheEffects()
        {
            DisposeEffects();
            cachedHapticIntensity = hapticIntensity;
            // Predefined effects are device-tuned. Use them at the neutral setting;
            // an adjusted intensity deliberately uses amplitude-controlled effects.
            if (androidApiLevel >= ApiPredefinedEffects && Mathf.Approximately(hapticIntensity, 1f))
            {
                lightEffect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createPredefined", EffectTick);
                mediumEffect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createPredefined", EffectClick);
                heavyEffect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createPredefined", EffectHeavyClick);
            }
            if (lightEffect == null) lightEffect = CreateOneShot(ScaleDuration(12), ScaleAmplitude(80));
            if (mediumEffect == null) mediumEffect = CreateOneShot(ScaleDuration(25), ScaleAmplitude(145));
            if (heavyEffect == null) heavyEffect = CreateOneShot(ScaleDuration(42), ScaleAmplitude(220));
            successEffect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createWaveform",
                new long[] { 0, ScaleDuration(12), ScaleDuration(35), ScaleDuration(18) },
                new int[] { 0, ScaleAmplitude(90), 0, ScaleAmplitude(150) }, -1);
        }

        private AndroidJavaObject CreateOneShot(long milliseconds, int amplitude) =>
            vibrationEffectClass.CallStatic<AndroidJavaObject>("createOneShot", milliseconds, amplitude);

        private long ScaleDuration(long milliseconds) => Mathf.RoundToInt(milliseconds * Mathf.Lerp(.7f, 1.35f, hapticIntensity * .5f));
        private int ScaleAmplitude(int amplitude) => Mathf.Clamp(Mathf.RoundToInt(amplitude * hapticIntensity), 1, 255);

        private void PlayAndroid(HapticType type)
        {
            if (vibrator == null) return;
            try
            {
                if (!Mathf.Approximately(cachedHapticIntensity, hapticIntensity)) CacheEffects();
                AndroidJavaObject effect = type == HapticType.Light ? lightEffect :
                                           type == HapticType.Medium ? mediumEffect :
                                           type == HapticType.Heavy ? heavyEffect : successEffect;
                if (useNativeAndroidHaptics && effect != null)
                    vibrator.Call("vibrate", effect);
                else if (useLegacyFallback)
                    vibrator.Call("vibrate", GetLegacyDuration(type));
            }
            catch (System.Exception exception)
            {
                if (debugHaptics) Debug.LogWarning("[HapticManager] Haptic request failed: " + exception.Message, this);
            }
        }

        private static long GetLegacyDuration(HapticType type)
        {
            switch (type)
            {
                case HapticType.Light: return 12L;
                case HapticType.Medium: return 25L;
                case HapticType.Heavy: return 42L;
                default: return 35L;
            }
        }

        private void OnDestroy() => DisposeNativeBridge();
        private void DisposeEffects()
        {
            lightEffect?.Dispose(); mediumEffect?.Dispose(); heavyEffect?.Dispose(); successEffect?.Dispose();
            lightEffect = mediumEffect = heavyEffect = successEffect = null;
        }
        private void DisposeNativeBridge()
        {
            DisposeEffects();
            vibrationEffectClass?.Dispose(); vibrator?.Dispose(); activity?.Dispose();
            vibrationEffectClass = null; vibrator = null; activity = null;
        }
#endif

        [ContextMenu("Play Light")]
        private void DebugLight() => PlayInternal(HapticType.Light);
        [ContextMenu("Play Medium")]
        private void DebugMedium() => PlayInternal(HapticType.Medium);
        [ContextMenu("Play Heavy")]
        private void DebugHeavy() => PlayInternal(HapticType.Heavy);
        [ContextMenu("Play Success")]
        private void DebugSuccess() => PlayInternal(HapticType.Success);
    }
}
