using System;
using UnityEngine;

namespace NutBoltSort
{
    /// <summary>
    /// Centralized Daily Reward Manager.
    ///
    /// Responsibilities:
    ///   - Owns the 7-day reward cycle state (CurrentDay, LastClaimDate, LastKnownDate).
    ///   - Evaluates local calendar date (DateTime.Now.Date) for claim availability.
    ///   - Streak retention: missing days do NOT reset progress back to Day 1.
    ///   - Anti-clock-tampering guard against backward system clock changes.
    ///   - Grants rewards directly via PlayerEconomy (coins, undo, expand).
    ///   - Resets cycle back to Day 1 on the next calendar day after Day 7 is claimed.
    ///   - Persists state in PlayerPrefs.
    /// </summary>
    [AddComponentMenu("BoltShift/DailyReward/Daily Reward Manager")]
    [DisallowMultipleComponent]
    public sealed class DailyRewardManager : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Singleton
        // ─────────────────────────────────────────────────────────────────────

        public static DailyRewardManager Instance { get; private set; }

        public static DailyRewardManager EnsureInstance(GameObject host)
        {
            if (Instance != null) return Instance;
            Instance = host.GetComponent<DailyRewardManager>();
            if (Instance == null) Instance = host.AddComponent<DailyRewardManager>();
            return Instance;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        [Header("Configuration")]
        [Tooltip("Optional custom DailyRewardConfigSO. If null, built-in V1 defaults are used.")]
        [SerializeField] private DailyRewardConfigSO rewardConfig;

        // ─────────────────────────────────────────────────────────────────────
        // PlayerPrefs Keys
        // ─────────────────────────────────────────────────────────────────────

        private const string KeyCurrentDay    = "BoltShift.DailyReward.CurrentDay";
        private const string KeyLastClaimDate = "BoltShift.DailyReward.LastClaimDate";
        private const string KeyLastKnownDate = "BoltShift.DailyReward.LastKnownDate";
        private const string KeyDay7Claimed   = "BoltShift.DailyReward.Day7Claimed";

        private const string DateFormat = "yyyy-MM-dd";

        // ─────────────────────────────────────────────────────────────────────
        // State
        // ─────────────────────────────────────────────────────────────────────

        public int  CurrentDay             { get; private set; } = 1;
        public bool IsRewardAvailableToday { get; private set; } = false;
        public bool IsClockTampered        { get; private set; } = false;

        public event Action OnStatusChanged;
        public event Action<int, DailyRewardItem> OnRewardClaimed;

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            RefreshRewardStatus();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Status Evaluation & Date Logic
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Evaluates current local date against saved claim dates and updates
        /// CurrentDay, IsRewardAvailableToday, and IsClockTampered flags.
        /// </summary>
        public void RefreshRewardStatus()
        {
            DateTime today = DateTime.Now.Date;
            string todayStr = today.ToString(DateFormat);

            CurrentDay = Mathf.Clamp(PlayerPrefs.GetInt(KeyCurrentDay, 1), 1, 7);
            string lastClaimStr = PlayerPrefs.GetString(KeyLastClaimDate, "");
            string lastKnownStr = PlayerPrefs.GetString(KeyLastKnownDate, "");
            bool day7Claimed = PlayerPrefs.GetInt(KeyDay7Claimed, 0) == 1;

            IsClockTampered = false;

            // 1. Anti-Clock-Tampering Guard
            if (!string.IsNullOrEmpty(lastKnownStr) && DateTime.TryParse(lastKnownStr, out DateTime lastKnown))
            {
                if (today < lastKnown)
                {
                    IsClockTampered = true;
                    IsRewardAvailableToday = false;
                    Debug.LogWarning($"[DailyRewardManager] Backward clock change detected! " +
                                     $"Current date ({todayStr}) is earlier than last recorded ({lastKnownStr}). Rewards disabled.");
                    OnStatusChanged?.Invoke();
                    return;
                }
            }

            // Update last known date to today if today is later
            PlayerPrefs.SetString(KeyLastKnownDate, todayStr);
            PlayerPrefs.Save();

            // 2. First Launch (Never claimed before)
            if (string.IsNullOrEmpty(lastClaimStr))
            {
                CurrentDay = 1;
                IsRewardAvailableToday = true;
                SaveCurrentDay(1);
                OnStatusChanged?.Invoke();
                return;
            }

            // 3. Compare last claim date with today
            if (DateTime.TryParse(lastClaimStr, out DateTime lastClaimDate))
            {
                if (today == lastClaimDate)
                {
                    // Already claimed today
                    IsRewardAvailableToday = false;
                }
                else if (today > lastClaimDate)
                {
                    // New day available!
                    IsRewardAvailableToday = true;

                    if (day7Claimed)
                    {
                        // Cycle complete on previous day — reset back to Day 1 for new claim
                        CurrentDay = 1;
                        SaveCurrentDay(1);
                        PlayerPrefs.SetInt(KeyDay7Claimed, 0);
                        PlayerPrefs.Save();
                    }
                    else
                    {
                        // Advance to next day in cycle if the previous day was claimed
                        int savedDay = Mathf.Clamp(PlayerPrefs.GetInt(KeyCurrentDay, 1), 1, 7);
                        if (savedDay < 7)
                        {
                            CurrentDay = savedDay + 1;
                            SaveCurrentDay(CurrentDay);
                        }
                    }
                }
            }
            else
            {
                // Fallback for corrupt date string
                CurrentDay = 1;
                IsRewardAvailableToday = true;
            }

            OnStatusChanged?.Invoke();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Claim Logic
        // ─────────────────────────────────────────────────────────────────────

        public bool CanClaimReward => IsRewardAvailableToday && !IsClockTampered;

        /// <summary>
        /// Attempts to claim today's reward.
        /// Grants rewards via PlayerEconomy, saves state, and fires callbacks.
        /// Pass multiplier = 2 when claiming via the 2x Rewarded Ad button!
        /// </summary>
        public bool ClaimTodayReward(int multiplier = 1)
        {
            if (!CanClaimReward)
            {
                Debug.LogWarning("[DailyRewardManager] Claim ignored — reward is not available today or clock was tampered.");
                return false;
            }

            multiplier = Mathf.Max(1, multiplier);
            DailyRewardItem baseReward = GetRewardForDay(CurrentDay);
            if (baseReward == null) return false;

            int grantedCoins  = baseReward.coins * multiplier;
            int grantedUndo   = baseReward.undo * multiplier;
            int grantedExpand = baseReward.expand * multiplier;

            // 1. Grant Rewards via PlayerEconomy
            if (PlayerEconomy.Instance != null)
            {
                if (grantedCoins > 0)  PlayerEconomy.Instance.AddCoins(grantedCoins);
                if (grantedUndo > 0)   PlayerEconomy.Instance.AddUndo(grantedUndo);
                if (grantedExpand > 0) PlayerEconomy.Instance.AddExpand(grantedExpand);
            }
            else
            {
                if (grantedCoins > 0) PlayerWallet.AddCoins(grantedCoins);
            }

            // 2. Update and Persist Saved State
            DateTime today = DateTime.Now.Date;
            string todayStr = today.ToString(DateFormat);

            PlayerPrefs.SetString(KeyLastClaimDate, todayStr);
            PlayerPrefs.SetString(KeyLastKnownDate, todayStr);

            if (CurrentDay >= 7)
            {
                PlayerPrefs.SetInt(KeyDay7Claimed, 1);
            }

            IsRewardAvailableToday = false;
            PlayerPrefs.Save();

            Debug.Log($"[DailyRewardManager] Claimed Day {CurrentDay} Reward (x{multiplier})! Coins: +{grantedCoins}, Undo: +{grantedUndo}, Expand: +{grantedExpand}");

            // 3. Fire Events
            OnRewardClaimed?.Invoke(CurrentDay, baseReward);
            OnStatusChanged?.Invoke();

            return true;
        }

        public DailyRewardItem GetRewardForDay(int dayNumber)
        {
            return rewardConfig != null ? rewardConfig.GetReward(dayNumber) : DailyRewardConfigSO.GetDefaultReward(dayNumber);
        }

        private static void SaveCurrentDay(int day)
        {
            PlayerPrefs.SetInt(KeyCurrentDay, Mathf.Clamp(day, 1, 7));
            PlayerPrefs.Save();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Development Debug Tools
        // ─────────────────────────────────────────────────────────────────────

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        [ContextMenu("Debug: Simulate Next Day")]
        public void DebugSimulateNextDay()
        {
            DateTime fakeNextDay = DateTime.Now.Date.AddDays(1);
            string fakeLastClaim = DateTime.Now.Date.AddDays(-1).ToString(DateFormat);

            PlayerPrefs.SetString(KeyLastClaimDate, fakeLastClaim);
            PlayerPrefs.SetString(KeyLastKnownDate, fakeLastClaim);
            PlayerPrefs.Save();

            RefreshRewardStatus();
            Debug.Log($"[DailyRewardManager Debug] Simulated next day. CurrentDay: {CurrentDay}, Available: {IsRewardAvailableToday}");
        }

        [ContextMenu("Debug: Reset Reward Data")]
        public void DebugResetRewardData()
        {
            PlayerPrefs.DeleteKey(KeyCurrentDay);
            PlayerPrefs.DeleteKey(KeyLastClaimDate);
            PlayerPrefs.DeleteKey(KeyLastKnownDate);
            PlayerPrefs.DeleteKey(KeyDay7Claimed);
            PlayerPrefs.Save();

            RefreshRewardStatus();
            Debug.Log("[DailyRewardManager Debug] Reset daily reward data to first-launch state.");
        }

        public void DebugSetRewardDay(int day)
        {
            day = Mathf.Clamp(day, 1, 7);
            SaveCurrentDay(day);
            PlayerPrefs.SetString(KeyLastClaimDate, DateTime.Now.Date.AddDays(-1).ToString(DateFormat));
            PlayerPrefs.SetInt(KeyDay7Claimed, 0);
            PlayerPrefs.Save();

            RefreshRewardStatus();
            Debug.Log($"[DailyRewardManager Debug] Forced current reward day to Day {day}.");
        }
#endif
    }
}
