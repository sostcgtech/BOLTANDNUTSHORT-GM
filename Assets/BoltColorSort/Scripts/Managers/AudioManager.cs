using System.Collections.Generic;
using UnityEngine;

namespace NutBoltSort
{
    public enum SfxType
    {
        ButtonClick, ButtonBack, PopupOpen, PopupClose,
        NutPickup, NutReturn, NutTravel, NutLand,
        InvalidMove, SelectionSwitch, ExpandBolt, ExpandComplete,
        HiddenNutReveal, BoltComplete, CompletionCap, Undo,
        LevelStart, LevelComplete, Reward, Victory
    }

    /// <summary>Single 2D, one-shot SFX channel. Clips are assigned in the inspector; no clips are hard-coded.</summary>
    [DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        private const string SfxEnabledKey = "BoltShift.SfxEnabled";
        private const string SfxVolumeKey = "BoltShift.SfxVolume";
        public static AudioManager Instance { get; private set; }

        [Header("SFX Audio Source")]
        [SerializeField] private AudioSource sfxAudioSource;
        [SerializeField, Range(0f, 1f)] private float masterSfxVolume = 1f;
        [SerializeField, Min(0f)] private float minimumRepeatDelay = .06f;
        [SerializeField] private bool debugLogging;
        [Header("UI")]
        [SerializeField] private AudioClip buttonClick;
        [SerializeField] private AudioClip buttonBack;
        [SerializeField] private AudioClip popupOpen;
        [SerializeField] private AudioClip popupClose;
        [Header("Nuts")]
        [SerializeField] private AudioClip nutPickup;
        [SerializeField] private AudioClip nutReturn;
        [SerializeField] private AudioClip nutTravel;
        [SerializeField] private AudioClip nutLand;
        [SerializeField] private AudioClip invalidMove;
        [SerializeField] private AudioClip selectionSwitch;
        [Header("Progression")]
        [SerializeField] private AudioClip expandBolt;
        [SerializeField] private AudioClip expandComplete;
        [SerializeField] private AudioClip hiddenNutReveal;
        [SerializeField] private AudioClip boltComplete;
        [SerializeField] private AudioClip completionCap;
        [SerializeField] private AudioClip undo;
        [SerializeField] private AudioClip levelStart;
        [SerializeField] private AudioClip levelComplete;
        [SerializeField] private AudioClip reward;
        [SerializeField] private AudioClip victory;

        private readonly Dictionary<SfxType, float> lastPlayTimes = new Dictionary<SfxType, float>();
        private bool sfxEnabled = true;

        public static AudioManager EnsureInstance(GameObject host)
        {
            if (Instance != null) return Instance;
            Instance = host.GetComponent<AudioManager>();
            if (Instance == null) Instance = host.AddComponent<AudioManager>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            sfxEnabled = PlayerPrefs.GetInt(SfxEnabledKey, 1) == 1;
            masterSfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, masterSfxVolume));
            EnsureAudioSource();
            ApplyVolume();
        }

        private void EnsureAudioSource()
        {
            if (sfxAudioSource == null) sfxAudioSource = GetComponent<AudioSource>();
            if (sfxAudioSource == null) sfxAudioSource = gameObject.AddComponent<AudioSource>();
            sfxAudioSource.playOnAwake = false;
            sfxAudioSource.loop = false;
            sfxAudioSource.spatialBlend = 0f;
        }

        public static void Play(SfxType type) => Instance?.PlayInternal(type);
        public static void PlayOneShot(AudioClip clip) => Instance?.PlayOneShotInternal(clip);
        public static void StopAll() { if (Instance != null && Instance.sfxAudioSource != null) Instance.sfxAudioSource.Stop(); }
        public static void SetSfxVolume(float volume) => Instance?.SetVolumeInternal(volume);
        public static void EnableSfx(bool enabled) => Instance?.SetEnabledInternal(enabled);

        private void PlayInternal(SfxType type)
        {
            if (!sfxEnabled) return;
            AudioClip clip = GetClip(type);
            if (clip == null) return;
            float now = Time.unscaledTime;
            float delay = type == SfxType.InvalidMove ? Mathf.Max(.15f, minimumRepeatDelay) : minimumRepeatDelay;
            if (lastPlayTimes.TryGetValue(type, out float previous) && now - previous < delay) return;
            lastPlayTimes[type] = now;
            sfxAudioSource.PlayOneShot(clip, masterSfxVolume);
            if (debugLogging) Debug.Log($"[AudioManager] {type}", this);
        }

        private void PlayOneShotInternal(AudioClip clip)
        {
            if (sfxEnabled && clip != null) sfxAudioSource.PlayOneShot(clip, masterSfxVolume);
        }

        private void SetVolumeInternal(float volume)
        {
            masterSfxVolume = Mathf.Clamp01(volume);
            ApplyVolume();
            PlayerPrefs.SetFloat(SfxVolumeKey, masterSfxVolume);
            PlayerPrefs.Save();
        }

        private void SetEnabledInternal(bool enabled)
        {
            sfxEnabled = enabled;
            PlayerPrefs.SetInt(SfxEnabledKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
            if (!enabled) StopAll();
        }

        private void ApplyVolume() { if (sfxAudioSource != null) sfxAudioSource.volume = masterSfxVolume; }
        private AudioClip GetClip(SfxType type)
        {
            switch (type)
            {
                case SfxType.ButtonClick: return buttonClick; case SfxType.ButtonBack: return buttonBack;
                case SfxType.PopupOpen: return popupOpen; case SfxType.PopupClose: return popupClose;
                case SfxType.NutPickup: return nutPickup; case SfxType.NutReturn: return nutReturn;
                case SfxType.NutTravel: return nutTravel; case SfxType.NutLand: return nutLand;
                case SfxType.InvalidMove: return invalidMove; case SfxType.SelectionSwitch: return selectionSwitch;
                case SfxType.ExpandBolt: return expandBolt; case SfxType.ExpandComplete: return expandComplete;
                case SfxType.HiddenNutReveal: return hiddenNutReveal; case SfxType.BoltComplete: return boltComplete;
                case SfxType.CompletionCap: return completionCap; case SfxType.Undo: return undo;
                case SfxType.LevelStart: return levelStart; case SfxType.LevelComplete: return levelComplete;
                case SfxType.Reward: return reward; default: return victory;
            }
        }
    }
}
