using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NutBoltSort
{
    /// <summary>
    /// A non-blocking, UI-only notification for the level currently being played.
    /// It deliberately owns only its own sequence, so board and gameplay tweens continue normally.
    /// </summary>
    public sealed class LevelRibbonController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RectTransform levelRibbon;
        [SerializeField] private Image ribbonImage;
        [SerializeField] private TMP_Text levelText;

        [Header("Animation")]
        [SerializeField, Min(.01f)] private float entryDuration = .35f;
        [SerializeField, Min(0f)] private float settleDuration = .10f;
        [SerializeField, Min(0f)] private float holdDuration = .65f;
        [SerializeField, Min(.01f)] private float exitDuration = .30f;
        [SerializeField] private Vector2 centerPosition = Vector2.zero;

        [Header("Behaviour")]
        [SerializeField] private bool showRibbonOnRestart;

        private CanvasGroup canvasGroup;
        private Sequence ribbonSequence;
        private int playVersion;

        public bool ShowRibbonOnRestart => showRibbonOnRestart;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
        }

        private void OnDisable()
        {
            // An externally-disabled ribbon must never leave an active sequence behind.
            KillRibbonTween();
        }

        private void OnDestroy() => KillRibbonTween();

        public void Show(int levelNumber)
        {
            if (levelRibbon == null || levelText == null)
            {
                Debug.LogWarning("[LevelRibbon] Assign Level Ribbon and Level Text in the Inspector before playing the ribbon.", this);
                return;
            }
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

            KillRibbonTween();
            playVersion++;
            int version = playVersion;

            levelText.text = $"LEVEL {Mathf.Max(1, levelNumber)}";

            gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            ResetVisualState();

            RectTransform parent = levelRibbon.parent as RectTransform;
            float parentHalfHeight = parent != null ? parent.rect.height * .5f : Screen.height * .5f;
            float ribbonHalfHeight = levelRibbon.rect.height * .5f;
            float offscreenDistance = parentHalfHeight + ribbonHalfHeight;
            Vector2 aboveScreen = centerPosition + Vector2.up * offscreenDistance;
            Vector2 belowScreen = centerPosition + Vector2.down * offscreenDistance;

            levelRibbon.anchoredPosition = aboveScreen;
            ribbonSequence = DOTween.Sequence().SetTarget(this);
            ribbonSequence.Append(levelRibbon.DOAnchorPos(centerPosition, entryDuration).SetEase(Ease.OutBack));
            if (settleDuration > 0f)
                ribbonSequence.Append(levelRibbon.DOAnchorPos(centerPosition, settleDuration).SetEase(Ease.OutCubic));
            ribbonSequence.Join(levelRibbon.DOScale(1.04f, settleDuration > 0f ? settleDuration : .08f).SetEase(Ease.OutCubic));
            ribbonSequence.Append(levelRibbon.DOScale(1f, .08f).SetEase(Ease.OutCubic));
            ribbonSequence.AppendInterval(holdDuration);
            ribbonSequence.Append(levelRibbon.DOAnchorPos(belowScreen, exitDuration).SetEase(Ease.InCubic));
            ribbonSequence.OnComplete(() =>
            {
                if (version == playVersion) gameObject.SetActive(false);
            });
        }

        private void ResetVisualState()
        {
            levelRibbon.localRotation = Quaternion.identity;
            levelRibbon.localScale = Vector3.one;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
        }

        private void KillRibbonTween()
        {
            if (ribbonSequence != null && ribbonSequence.IsActive()) ribbonSequence.Kill(false);
            ribbonSequence = null;
        }
    }
}
