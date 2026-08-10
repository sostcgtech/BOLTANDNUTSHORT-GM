using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NutBoltSort
{
    /// <summary>
    /// Displays a small, fixed set of Main Menu level nodes around the player's
    /// current saved level. It only changes assigned text/images; it never creates
    /// level nodes or makes them selectable.
    /// </summary>
    [AddComponentMenu("BoltShift/Main Menu Level Progress")]
    [DisallowMultipleComponent]
    public sealed class MainMenuLevelProgress : MonoBehaviour
    {
        private const string CurrentLevelKey = "CurrentLevelNumber";

        [Header("Level Number Text")]
        [SerializeField] private TMP_Text levelPlus5Text;
        [SerializeField] private TMP_Text levelPlus4Text;
        [SerializeField] private TMP_Text levelPlus3Text;
        [SerializeField] private TMP_Text levelPlus2Text;
        [SerializeField] private TMP_Text levelPlus1Text;
        [SerializeField] private TMP_Text currentLevelText;
        [SerializeField] private TMP_Text levelMinus1Text;
        [SerializeField] private TMP_Text levelMinus2Text;

        [Header("Optional Node Backgrounds")]
        [Tooltip("Optional. These are hidden together with their lower level text when the value would be below Level 1.")]
        [SerializeField] private Image levelMinus1Image;
        [SerializeField] private Image levelMinus2Image;
        [Tooltip("Your existing highlighted current-level background. This script leaves it active and does not animate it.")]
        [SerializeField] private Image currentLevelImage;

        [Header("Play Button")]
        [SerializeField] private TMP_Text playButtonLevelText;

        [Header("Optional Refresh Animation")]
        [Tooltip("Parent RectTransform containing the level-number nodes. Leave empty to update without animation.")]
        [SerializeField] private RectTransform levelNumberGroup;
        [SerializeField, Range(15f, 25f)] private float refreshOffsetY = 20f;
        [SerializeField, Range(0.20f, 0.30f)] private float refreshDuration = 0.25f;

        private int displayedLevel = -1;
        private Vector2 groupRestPosition;
        private CanvasGroup groupCanvasGroup;

        private void Awake()
        {
            if (levelNumberGroup != null)
            {
                groupRestPosition = levelNumberGroup.anchoredPosition;
                groupCanvasGroup = levelNumberGroup.GetComponent<CanvasGroup>() ??
                                   levelNumberGroup.gameObject.AddComponent<CanvasGroup>();
            }
        }

        private void OnEnable()
        {
            RefreshFromSavedLevel(animate: false);
        }

        private void Update()
        {
            int savedLevel = GetSavedLevel();
            if (savedLevel != displayedLevel)
            {
                RefreshFromSavedLevel(animate: displayedLevel >= 1);
            }
        }

        private void OnDisable()
        {
            if (levelNumberGroup != null)
            {
                DOTween.Kill(levelNumberGroup);
                levelNumberGroup.anchoredPosition = groupRestPosition;
            }

            if (groupCanvasGroup != null) groupCanvasGroup.alpha = 1f;
        }

        /// <summary>Call this after changing CurrentLevelNumber while Main Menu is open.</summary>
        public void RefreshFromSavedLevel() => RefreshFromSavedLevel(animate: true);

        private void RefreshFromSavedLevel(bool animate)
        {
            int currentLevel = GetSavedLevel();

            SetNode(levelPlus5Text, null, currentLevel + 5);
            SetNode(levelPlus4Text, null, currentLevel + 4);
            SetNode(levelPlus3Text, null, currentLevel + 3);
            SetNode(levelPlus2Text, null, currentLevel + 2);
            SetNode(levelPlus1Text, null, currentLevel + 1);
            SetNode(currentLevelText, currentLevelImage, currentLevel);
            SetNode(levelMinus1Text, levelMinus1Image, currentLevel - 1);
            SetNode(levelMinus2Text, levelMinus2Image, currentLevel - 2);

            if (playButtonLevelText != null)
            {
                playButtonLevelText.text = $"LEVEL {currentLevel}";
            }

            bool changed = displayedLevel >= 1 && displayedLevel != currentLevel;
            displayedLevel = currentLevel;

            if (animate && changed)
            {
                PlayRefreshAnimation();
            }
        }

        private void SetNode(TMP_Text text, Image image, int level)
        {
            bool visible = level >= 1;
            if (text != null)
            {
                text.gameObject.SetActive(visible);
                if (visible) text.text = level.ToString();
            }

            if (image != null)
            {
                image.gameObject.SetActive(visible);
            }
        }

        private void PlayRefreshAnimation()
        {
            if (levelNumberGroup == null) return;

            DOTween.Kill(levelNumberGroup);
            levelNumberGroup.anchoredPosition = groupRestPosition + Vector2.down * refreshOffsetY;

            if (groupCanvasGroup != null) groupCanvasGroup.alpha = 0.72f;

            Sequence sequence = DOTween.Sequence().SetTarget(levelNumberGroup);
            sequence.Join(levelNumberGroup.DOAnchorPos(groupRestPosition, refreshDuration).SetEase(Ease.OutCubic));
            if (groupCanvasGroup != null)
            {
                sequence.Join(groupCanvasGroup.DOFade(1f, refreshDuration).SetEase(Ease.OutCubic));
            }
        }

        private static int GetSavedLevel()
        {
            return Mathf.Max(1, PlayerPrefs.GetInt(CurrentLevelKey, 1));
        }
    }
}
