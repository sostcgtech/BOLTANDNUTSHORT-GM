using System;
using UnityEngine;

namespace NutBoltSort
{
    /// <summary>
    /// Central economy manager — owns Coins, UndoCount, and ExpandCount.
    ///
    /// Coins are delegated to the existing PlayerWallet static class so all
    /// existing UI that subscribes to PlayerWallet.OnCoinsChanged continues
    /// to receive live updates without any changes.
    ///
    /// Undo and Expand counts add similar per-value events.
    ///
    /// All values are backed by PlayerPrefs and saved on every change.
    ///
    /// Usage:
    ///   PlayerEconomy.Instance.AddCoins(500);
    ///   PlayerEconomy.Instance.AddUndo(5);
    ///   PlayerEconomy.Instance.AddExpand(1);
    ///
    /// Subscribe:
    ///   PlayerEconomy.Instance.OnUndoChanged  += count => myText.text = count.ToString();
    ///   PlayerEconomy.Instance.OnExpandChanged += count => myText.text = count.ToString();
    ///
    /// Do NOT create a second economy class — extend this one.
    /// </summary>
    [AddComponentMenu("BoltShift/Economy/Player Economy")]
    [DisallowMultipleComponent]
    public sealed class PlayerEconomy : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Singleton
        // ─────────────────────────────────────────────────────────────────────

        public static PlayerEconomy Instance { get; private set; }

        /// <summary>
        /// Ensures exactly one PlayerEconomy exists on <paramref name="host"/> and
        /// persists across scenes. Call from BootTask_Managers.
        /// </summary>
        public static PlayerEconomy EnsureInstance(GameObject host)
        {
            if (Instance != null) return Instance;
            Instance = host.GetComponent<PlayerEconomy>();
            if (Instance == null) Instance = host.AddComponent<PlayerEconomy>();
            return Instance;
        }

        // ─────────────────────────────────────────────────────────────────────
        // PlayerPrefs Keys
        // ─────────────────────────────────────────────────────────────────────

        private const string UndoKey   = "BoltShift.UndoCount";
        private const string ExpandKey = "BoltShift.ExpandCount";

        // ─────────────────────────────────────────────────────────────────────
        // Events
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Fired whenever the Undo count changes. Passes the new count.</summary>
        public event Action<int> OnUndoChanged;

        /// <summary>Fired whenever the Expand count changes. Passes the new count.</summary>
        public event Action<int> OnExpandChanged;

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
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ═════════════════════════════════════════════════════════════════════
        // COINS — delegated to PlayerWallet
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>Returns the current saved coin balance (never negative).</summary>
        public int GetCoins() => PlayerWallet.GetCoins();

        /// <summary>Adds coins. Negative values are ignored.</summary>
        public void AddCoins(int amount)
        {
            if (amount <= 0) return;
            PlayerWallet.AddCoins(amount);
            Debug.Log($"[Shop] Coins added: +{amount}. New balance: {GetCoins()}");
        }

        /// <summary>
        /// Attempts to spend coins.
        /// Returns true and deducts if player can afford it; false otherwise.
        /// </summary>
        public bool SpendCoins(int amount)
        {
            if (amount <= 0) return true;
            bool success = PlayerWallet.SpendCoins(amount);
            if (success)
                Debug.Log($"[Shop] Coins spent: -{amount}. New balance: {GetCoins()}");
            else
                Debug.Log($"[Shop] Not enough coins (needed {amount}, have {GetCoins()}).");
            return success;
        }

        /// <summary>Returns true if the player has at least <paramref name="amount"/> coins.</summary>
        public bool HasEnoughCoins(int amount) => PlayerWallet.GetCoins() >= amount;

        // ═════════════════════════════════════════════════════════════════════
        // UNDO COUNT
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>Returns the saved Undo count (never negative).</summary>
        public int GetUndoCount() => Mathf.Max(0, PlayerPrefs.GetInt(UndoKey, 0));

        /// <summary>Adds Undo uses. Saves and fires OnUndoChanged.</summary>
        public void AddUndo(int amount)
        {
            if (amount <= 0) return;
            int newCount = GetUndoCount() + amount;
            SaveUndo(newCount);
            Debug.Log($"[Shop] Undo added: +{amount}. Total: {newCount}");
        }

        /// <summary>
        /// Spends one Undo use.
        /// Returns false if no uses remain.
        /// </summary>
        public bool SpendUndo(int amount = 1)
        {
            int current = GetUndoCount();
            if (current < amount) return false;
            SaveUndo(current - amount);
            return true;
        }

        private void SaveUndo(int value)
        {
            value = Mathf.Max(0, value);
            PlayerPrefs.SetInt(UndoKey, value);
            PlayerPrefs.Save();
            OnUndoChanged?.Invoke(value);
        }

        // ═════════════════════════════════════════════════════════════════════
        // EXPAND COUNT
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>Returns the saved Expand count (never negative).</summary>
        public int GetExpandCount() => Mathf.Max(0, PlayerPrefs.GetInt(ExpandKey, 0));

        /// <summary>Adds Expand uses. Saves and fires OnExpandChanged.</summary>
        public void AddExpand(int amount)
        {
            if (amount <= 0) return;
            int newCount = GetExpandCount() + amount;
            SaveExpand(newCount);
            Debug.Log($"[Shop] Expand added: +{amount}. Total: {newCount}");
        }

        /// <summary>
        /// Spends one Expand use.
        /// Returns false if no uses remain.
        /// </summary>
        public bool SpendExpand(int amount = 1)
        {
            int current = GetExpandCount();
            if (current < amount) return false;
            SaveExpand(current - amount);
            return true;
        }

        private void SaveExpand(int value)
        {
            value = Mathf.Max(0, value);
            PlayerPrefs.SetInt(ExpandKey, value);
            PlayerPrefs.Save();
            OnExpandChanged?.Invoke(value);
        }
    }
}
