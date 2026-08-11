using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace NutBoltSort
{
    public enum DailyRewardCardState
    {
        Claimed,
        Available,
        Future
    }

    /// <summary>
    /// Visual controller for a single Day Card in the Daily Reward calendar (Day 1 - Day 7).
    /// Handles the 3 visual states (Claimed, Available, Future) and claim pop animations.
    /// </summary>
    [AddComponentMenu("BoltShift/DailyReward/Daily Reward Day Card")]
    [DisallowMultipleComponent]
    public sealed class DailyRewardDayCard : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        [Header("Text Labels")]
        [SerializeField] private TMP_Text dayTitleText;
        [SerializeField] private TMP_Text coinAmountText;
        [SerializeField] private TMP_Text undoAmountText;
        [SerializeField] private TMP_Text expandAmountText;

        [Header("Icons & Bonus Groups")]
        [SerializeField] private Image rewardIconImage;
        [SerializeField] private GameObject undoGroup;
        [SerializeField] private GameObject expandGroup;

        [Header("State Overlays & Visuals")]
        [Tooltip("Overlay shown when this day has already been claimed (checkmark, dark tint).")]
        [SerializeField] private GameObject claimedOverlay;

        [Tooltip("Highlight object shown when this day is today's claimable reward (glow, bright border).")]
        [SerializeField] private GameObject availableHighlight;

        [Tooltip("Overlay shown for future/locked days.")]
        [SerializeField] private GameObject futureOverlay;

        [Header("Canvas Group")]
        [SerializeField] private CanvasGroup cardCanvasGroup;

        // ─────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────

        public DailyRewardCardState CurrentState { get; private set; } = DailyRewardCardState.Future;

        public void Setup(int dayNumber, DailyRewardItem rewardItem, DailyRewardCardState state)
        {
            CurrentState = state;

            if (dayTitleText != null)
                dayTitleText.text = $"DAY {dayNumber}";

            if (rewardItem != null)
            {
                if (coinAmountText != null)
                    coinAmountText.text = rewardItem.coins > 0 ? rewardItem.coins.ToString("N0") : "";

                if (undoGroup != null)
                {
                    bool hasUndo = rewardItem.undo > 0;
                    undoGroup.SetActive(hasUndo);
                    if (hasUndo && undoAmountText != null) undoAmountText.text = $"+{rewardItem.undo}";
                }

                if (expandGroup != null)
                {
                    bool hasExpand = rewardItem.expand > 0;
                    expandGroup.SetActive(hasExpand);
                    if (hasExpand && expandAmountText != null) expandAmountText.text = $"+{rewardItem.expand}";
                }

                if (rewardIconImage != null && rewardItem.rewardIcon != null)
                    rewardIconImage.sprite = rewardItem.rewardIcon;
            }

            // Auto-discover state overlays if unassigned
            if (claimedOverlay == null)
            {
                var t = transform.Find("ClaimedOverlay") ?? transform.Find("Claimed") ?? transform.Find("Checkmark");
                if (t != null) claimedOverlay = t.gameObject;
            }
            if (availableHighlight == null)
            {
                var t = transform.Find("AvailableHighlight") ?? transform.Find("Highlight") ?? transform.Find("Glow");
                if (t != null) availableHighlight = t.gameObject;
            }
            if (futureOverlay == null)
            {
                var t = transform.Find("FutureOverlay") ?? transform.Find("Future") ?? transform.Find("Locked");
                if (t != null) futureOverlay = t.gameObject;
            }

            // Apply visual state overlays
            if (claimedOverlay != null)     claimedOverlay.SetActive(state == DailyRewardCardState.Claimed);
            if (availableHighlight != null) availableHighlight.SetActive(state == DailyRewardCardState.Available);
            if (futureOverlay != null)       futureOverlay.SetActive(state == DailyRewardCardState.Future);

            // Background tinting feedback
            Image bgImg = GetComponent<Image>();
            if (bgImg != null)
            {
                bgImg.color = state switch
                {
                    DailyRewardCardState.Claimed   => new Color(0.55f, 0.60f, 0.65f, 1.0f), // Darkened/greyed claimed state
                    DailyRewardCardState.Available => new Color(1.00f, 1.00f, 1.00f, 1.0f), // Bright highlight available state
                    DailyRewardCardState.Future    => new Color(0.75f, 0.78f, 0.82f, 1.0f), // Muted future state
                    _                              => Color.white
                };
            }

            if (cardCanvasGroup != null)
            {
                cardCanvasGroup.alpha = state switch
                {
                    DailyRewardCardState.Claimed   => 0.70f,
                    DailyRewardCardState.Available => 1.0f,
                    DailyRewardCardState.Future    => 0.55f,
                    _                              => 1.0f
                };
            }
        }

        /// <summary>
        /// Plays a scale pop animation when this card's reward is claimed.
        /// </summary>
        public void PlayClaimAnimation(Action onComplete = null)
        {
            DOTween.Kill(transform);
            Sequence seq = DOTween.Sequence().SetTarget(transform);
            seq.Append(transform.DOScale(Vector3.one * 1.15f, 0.12f).SetEase(Ease.OutQuad));
            seq.Append(transform.DOScale(Vector3.one * 0.95f, 0.08f).SetEase(Ease.InQuad));
            seq.Append(transform.DOScale(Vector3.one, 0.08f).SetEase(Ease.OutBack));
            seq.OnComplete(() => onComplete?.Invoke());
        }
    }
}
