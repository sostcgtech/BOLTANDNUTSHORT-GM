using System;
using UnityEngine;

namespace NutBoltSort
{
    /// <summary>
    /// Minimal static coin-balance store backed by PlayerPrefs.
    ///
    /// No MonoBehaviour — no scene dependency. Every UI system reads
    /// and writes through these methods so the balance is always in sync.
    ///
    /// Wire-up points for future economy systems:
    ///   - Replace the PlayerPrefs body of GetCoins / SetCoins with
    ///     your server/cloud-save calls.
    ///   - AddCoins / SpendCoins already guard against negative totals.
    ///
    /// Do NOT create a second "coin manager" — extend this class instead.
    /// </summary>
    public static class PlayerWallet
    {
        // ─────────────────────────────────────────────────────────────────────
        // Prefs Key
        // ─────────────────────────────────────────────────────────────────────

        private const string CoinsKey = "BoltShift.Coins";

        // ─────────────────────────────────────────────────────────────────────
        // Events
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired whenever the coin balance changes. Passes the new balance.
        /// Subscribe in UI Awake, unsubscribe in OnDestroy.
        /// </summary>
        public static event Action<int> OnCoinsChanged;

        // ─────────────────────────────────────────────────────────────────────
        // Read / Write
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Returns the current saved coin balance (never negative).</summary>
        public static int GetCoins()
        {
            return Mathf.Max(0, PlayerPrefs.GetInt(CoinsKey, 0));
        }

        /// <summary>
        /// Overwrites the coin balance.
        /// Clamps to zero. Saves immediately and fires OnCoinsChanged.
        /// </summary>
        public static void SetCoins(int amount)
        {
            int clamped = Mathf.Max(0, amount);
            PlayerPrefs.SetInt(CoinsKey, clamped);
            PlayerPrefs.Save();
            OnCoinsChanged?.Invoke(clamped);
        }

        /// <summary>
        /// Adds coins to the current balance.
        /// Negative values are ignored — use SpendCoins to remove coins.
        /// </summary>
        public static void AddCoins(int amount)
        {
            if (amount <= 0) return;
            SetCoins(GetCoins() + amount);
        }

        /// <summary>
        /// Attempts to spend coins.
        /// Returns true and deducts if the player can afford it; returns false otherwise.
        /// </summary>
        public static bool SpendCoins(int amount)
        {
            if (amount <= 0) return true;
            int current = GetCoins();
            if (current < amount) return false;
            SetCoins(current - amount);
            return true;
        }
    }
}
