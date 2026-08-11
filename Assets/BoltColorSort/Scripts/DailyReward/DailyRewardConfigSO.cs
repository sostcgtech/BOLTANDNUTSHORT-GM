using System;
using System.Collections.Generic;
using UnityEngine;

namespace NutBoltSort
{
    [Serializable]
    public class DailyRewardItem
    {
        [Tooltip("Day number in the 7-day cycle (1 to 7).")]
        public int dayNumber = 1;

        [Tooltip("Coin reward for this day.")]
        public int coins = 100;

        [Tooltip("Undo booster reward for this day (Day 7 default: 2).")]
        public int undo = 0;

        [Tooltip("Expand booster reward for this day (Day 7 default: 1).")]
        public int expand = 0;

        [Tooltip("Optional custom icon sprite for the reward (e.g. coin pile, booster bundle).")]
        public Sprite rewardIcon;
    }

    /// <summary>
    /// Configurable ScriptableObject for the 7-day Daily Reward schedule.
    ///
    /// Place in Resources/ or assign via Inspector on DailyRewardManager.
    /// Includes built-in fallbacks matching the V1 economy design.
    /// </summary>
    [CreateAssetMenu(fileName = "DailyRewardConfig", menuName = "BoltShift/Economy/Daily Reward Config")]
    public class DailyRewardConfigSO : ScriptableObject
    {
        [Header("7-Day Reward Schedule")]
        [SerializeField] private List<DailyRewardItem> rewards = new List<DailyRewardItem>();

        public IReadOnlyList<DailyRewardItem> Rewards => rewards;

        /// <summary>
        /// Returns the reward descriptor for the specified day (1-7 1-based index).
        /// Falls back to default V1 values if unassigned or out of range.
        /// </summary>
        public DailyRewardItem GetReward(int dayNumber)
        {
            int index = dayNumber - 1;
            if (rewards != null && index >= 0 && index < rewards.Count)
            {
                return rewards[index];
            }

            return GetDefaultReward(dayNumber);
        }

        /// <summary>Default V1 reward schedule fallback.</summary>
        public static DailyRewardItem GetDefaultReward(int dayNumber)
        {
            int day = Mathf.Clamp(dayNumber, 1, 7);
            return day switch
            {
                1 => new DailyRewardItem { dayNumber = 1, coins = 100, undo = 0, expand = 0 },
                2 => new DailyRewardItem { dayNumber = 2, coins = 150, undo = 0, expand = 0 },
                3 => new DailyRewardItem { dayNumber = 3, coins = 200, undo = 0, expand = 0 },
                4 => new DailyRewardItem { dayNumber = 4, coins = 250, undo = 0, expand = 0 },
                5 => new DailyRewardItem { dayNumber = 5, coins = 300, undo = 0, expand = 0 },
                6 => new DailyRewardItem { dayNumber = 6, coins = 400, undo = 0, expand = 0 },
                7 => new DailyRewardItem { dayNumber = 7, coins = 750, undo = 2, expand = 1 },
                _ => new DailyRewardItem { dayNumber = day, coins = 100, undo = 0, expand = 0 }
            };
        }

        private void Reset()
        {
            rewards.Clear();
            for (int i = 1; i <= 7; i++)
            {
                rewards.Add(GetDefaultReward(i));
            }
        }
    }
}
