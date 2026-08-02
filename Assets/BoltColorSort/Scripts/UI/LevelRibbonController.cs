using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NutBoltSort
{
    public enum LevelRibbonAnimationStyle
    {
        CenterPop,
        SideSlide,
        ElasticReveal,
        TopDrop
    }

    /// <summary>
    /// Plays one of several non-blocking presentation styles on an Inspector-assigned ribbon.
    /// This component never creates UI objects and owns only its own DOTween sequence.
    /// </summary>
    public sealed class LevelRibbonController : MonoBehaviour
    {
        [Header("Animation Style")]
        [SerializeField] private LevelRibbonAnimationStyle animationStyle = LevelRibbonAnimationStyle.CenterPop;

        [Header("References")]
        [SerializeField] private RectTransform ribbon;
        [SerializeField] private CanvasGroup ribbonCanvasGroup;
        [SerializeField] private Image ribbonImage;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private CanvasGroup textCanvasGroup;

        [Header("Timing")]
        [SerializeField, Min(.01f)] private float entryDuration = .22f;
        [SerializeField, Min(0f)] private float settleDuration = .10f;
        [SerializeField, Min(0f)] private float holdDuration = .65f;
        [SerializeField, Min(.01f)] private float exitDuration = .22f;

        [Header("Layout")]
        [SerializeField] private Vector2 centerPosition = Vector2.zero;
        [SerializeField, Min(0f)] private float sideEntryOffset = 40f;
        [SerializeField, Min(0f)] private float sideExitOffset = 40f;

        [Header("Center Pop")]
        [SerializeField, Range(.05f, 1f)] private float popStartScale = .55f;
        [SerializeField, Min(1f)] private float popOvershootScale = 1.08f;

        [Header("Elastic Reveal")]
        [SerializeField, Range(.01f, 1f)] private float elasticStartScaleX = .05f;

        [Header("Behaviour")]
        [SerializeField] private bool showRibbonOnRestart;
        [SerializeField, Min(1)] private int previewLevelNumber = 1;

        private Sequence ribbonSequence;
        private int playVersion;

        public bool ShowRibbonOnRestart => showRibbonOnRestart;

        private void OnDisable() => KillRibbonTween();
        private void OnDestroy() => KillRibbonTween();

        public void Show(int levelNumber)
        {
            Play(levelNumber, animationStyle);
        }

        [ContextMenu("Preview Center Pop")]
        private void PreviewCenterPop() => Preview(LevelRibbonAnimationStyle.CenterPop);

        [ContextMenu("Preview Side Slide")]
        private void PreviewSideSlide() => Preview(LevelRibbonAnimationStyle.SideSlide);

        [ContextMenu("Preview Elastic Reveal")]
        private void PreviewElasticReveal() => Preview(LevelRibbonAnimationStyle.ElasticReveal);

        [ContextMenu("Preview Top Drop")]
        private void PreviewTopDrop() => Preview(LevelRibbonAnimationStyle.TopDrop);

        private void Preview(LevelRibbonAnimationStyle style)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[LevelRibbon] Enter Play Mode to preview a ribbon animation.", this);
                return;
            }

            Play(previewLevelNumber, style);
        }

        private void Play(int levelNumber, LevelRibbonAnimationStyle style)
        {
            if (ribbon == null || levelText == null)
            {
                Debug.LogWarning("[LevelRibbon] Assign Ribbon and Level Text in the Inspector before playing the ribbon.", this);
                return;
            }

            KillRibbonTween();
            playVersion++;
            int version = playVersion;

            levelText.text = $"LEVEL {Mathf.Max(1, levelNumber)}";
            gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            ResetVisualState();

            switch (style)
            {
                case LevelRibbonAnimationStyle.SideSlide:
                    PlaySideSlide(version);
                    break;
                case LevelRibbonAnimationStyle.ElasticReveal:
                    PlayElasticReveal(version);
                    break;
                case LevelRibbonAnimationStyle.TopDrop:
                    PlayTopDrop(version);
                    break;
                default:
                    PlayCenterPop(version);
                    break;
            }
        }

        private void PlayCenterPop(int version)
        {
            ribbon.anchoredPosition = centerPosition;
            ribbon.localScale = Vector3.one * popStartScale;
            SetRibbonAlpha(0f);
            SetTextAlpha(0f);

            ribbonSequence = DOTween.Sequence().SetTarget(this);
            ribbonSequence.Append(ribbon.DOScale(popOvershootScale, entryDuration).SetEase(Ease.OutBack));
            ribbonSequence.Join(DOFadeRibbon(1f, entryDuration));
            ribbonSequence.Join(DOFadeText(1f, entryDuration));
            ribbonSequence.Append(ribbon.DOScale(1f, settleDuration).SetEase(Ease.OutCubic));
            ribbonSequence.AppendInterval(holdDuration);
            ribbonSequence.Append(ribbon.DOScale(.75f, exitDuration).SetEase(Ease.InBack));
            ribbonSequence.Join(DOFadeRibbon(0f, exitDuration));
            ribbonSequence.Join(DOFadeText(0f, exitDuration));
            FinishSequence(version);
        }

        private void PlaySideSlide(int version)
        {
            GetHorizontalOffscreenPositions(out Vector2 left, out Vector2 right);
            Vector2 overshoot = centerPosition + Vector2.right * 28f;
            ribbon.anchoredPosition = left;
            ribbon.localScale = Vector3.one * .95f;
            SetRibbonAlpha(1f);
            SetTextAlpha(1f);

            Sequence enter = DOTween.Sequence();
            enter.Append(ribbon.DOAnchorPos(overshoot, entryDuration).SetEase(Ease.OutCubic));
            enter.Join(ribbon.DOScale(new Vector3(1.08f, .94f, 1f), entryDuration * .55f).SetEase(Ease.OutCubic));
            enter.Insert(entryDuration * .55f, ribbon.DOScale(Vector3.one, entryDuration * .45f).SetEase(Ease.OutCubic));

            ribbonSequence = DOTween.Sequence().SetTarget(this);
            ribbonSequence.Append(enter);
            ribbonSequence.Append(ribbon.DOAnchorPos(centerPosition, settleDuration).SetEase(Ease.OutBack));
            ribbonSequence.AppendInterval(holdDuration);
            ribbonSequence.Append(ribbon.DOAnchorPos(right, exitDuration).SetEase(Ease.InCubic));
            FinishSequence(version);
        }

        private void PlayElasticReveal(int version)
        {
            ribbon.anchoredPosition = centerPosition;
            ribbon.localScale = new Vector3(elasticStartScaleX, .9f, 1f);
            levelText.rectTransform.localScale = Vector3.one * .6f;
            SetRibbonAlpha(1f);
            SetTextAlpha(0f);

            ribbonSequence = DOTween.Sequence().SetTarget(this);
            ribbonSequence.Append(ribbon.DOScale(new Vector3(popOvershootScale, 1f, 1f), entryDuration).SetEase(Ease.OutBack));
            ribbonSequence.Append(ribbon.DOScale(Vector3.one, settleDuration).SetEase(Ease.OutCubic));
            ribbonSequence.Append(levelText.rectTransform.DOScale(1.08f, .15f).SetEase(Ease.OutBack));
            ribbonSequence.Join(DOFadeText(1f, .15f));
            ribbonSequence.Append(levelText.rectTransform.DOScale(1f, .08f).SetEase(Ease.OutCubic));
            ribbonSequence.AppendInterval(holdDuration);
            ribbonSequence.Append(DOFadeText(0f, .10f));
            ribbonSequence.Append(ribbon.DOScale(new Vector3(elasticStartScaleX, .9f, 1f), exitDuration).SetEase(Ease.InBack));
            FinishSequence(version);
        }

        // Original level-start ribbon motion: enter from above and leave below the canvas.
        private void PlayTopDrop(int version)
        {
            GetVerticalOffscreenPositions(out Vector2 above, out Vector2 below);
            ribbon.anchoredPosition = above;
            ribbon.localScale = Vector3.one;
            SetRibbonAlpha(1f);
            SetTextAlpha(1f);

            ribbonSequence = DOTween.Sequence().SetTarget(this);
            ribbonSequence.Append(ribbon.DOAnchorPos(centerPosition, entryDuration).SetEase(Ease.OutBack));
            ribbonSequence.Append(ribbon.DOScale(1.04f, settleDuration).SetEase(Ease.OutCubic));
            ribbonSequence.Append(ribbon.DOScale(1f, .08f).SetEase(Ease.OutCubic));
            ribbonSequence.AppendInterval(holdDuration);
            ribbonSequence.Append(ribbon.DOAnchorPos(below, exitDuration).SetEase(Ease.InCubic));
            FinishSequence(version);
        }

        private void FinishSequence(int version)
        {
            ribbonSequence.OnComplete(() =>
            {
                if (version == playVersion) gameObject.SetActive(false);
            });
        }

        private void ResetVisualState()
        {
            ribbon.anchoredPosition = centerPosition;
            ribbon.localRotation = Quaternion.identity;
            ribbon.localScale = Vector3.one;
            levelText.rectTransform.localScale = Vector3.one;
            SetRibbonAlpha(1f);
            SetTextAlpha(1f);
            if (ribbonCanvasGroup != null)
            {
                ribbonCanvasGroup.interactable = false;
                ribbonCanvasGroup.blocksRaycasts = false;
            }
            if (textCanvasGroup != null)
            {
                textCanvasGroup.interactable = false;
                textCanvasGroup.blocksRaycasts = false;
            }
        }

        private void GetHorizontalOffscreenPositions(out Vector2 left, out Vector2 right)
        {
            RectTransform parent = ribbon.parent as RectTransform;
            float parentHalfWidth = parent != null ? parent.rect.width * .5f : Screen.width * .5f;
            float ribbonHalfWidth = ribbon.rect.width * .5f;
            left = centerPosition + Vector2.left * (parentHalfWidth + ribbonHalfWidth + sideEntryOffset);
            right = centerPosition + Vector2.right * (parentHalfWidth + ribbonHalfWidth + sideExitOffset);
        }

        private void GetVerticalOffscreenPositions(out Vector2 above, out Vector2 below)
        {
            RectTransform parent = ribbon.parent as RectTransform;
            float parentHalfHeight = parent != null ? parent.rect.height * .5f : Screen.height * .5f;
            float ribbonHalfHeight = ribbon.rect.height * .5f;
            float offscreenDistance = parentHalfHeight + ribbonHalfHeight;
            above = centerPosition + Vector2.up * offscreenDistance;
            below = centerPosition + Vector2.down * offscreenDistance;
        }

        private Tween DOFadeRibbon(float alpha, float duration)
        {
            if (ribbonCanvasGroup != null) return ribbonCanvasGroup.DOFade(alpha, duration);
            if (ribbonImage != null) return ribbonImage.DOFade(alpha, duration);
            return DOVirtual.Float(0f, 0f, duration, _ => { });
        }

        private Tween DOFadeText(float alpha, float duration)
        {
            if (textCanvasGroup != null) return textCanvasGroup.DOFade(alpha, duration);
            return levelText.DOFade(alpha, duration);
        }

        private void SetRibbonAlpha(float alpha)
        {
            if (ribbonCanvasGroup != null) ribbonCanvasGroup.alpha = alpha;
            else if (ribbonImage != null)
            {
                Color color = ribbonImage.color;
                color.a = alpha;
                ribbonImage.color = color;
            }
        }

        private void SetTextAlpha(float alpha)
        {
            if (textCanvasGroup != null) textCanvasGroup.alpha = alpha;
            else
            {
                Color color = levelText.color;
                color.a = alpha;
                levelText.color = color;
            }
        }

        private void KillRibbonTween()
        {
            if (ribbonSequence != null && ribbonSequence.IsActive()) ribbonSequence.Kill(false);
            ribbonSequence = null;
        }
    }
}
