using System;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace NutBoltSort
{
    /// <summary>
    /// Handles popup DOTween open/close transitions and dim background fading.
    /// </summary>
    public class UIPopup : MonoBehaviour
    {
        [Header("Popup References")]
        [SerializeField] private Image dimBackground;
        [SerializeField] private RectTransform popupPanel;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Animation Settings")]
        [SerializeField, Min(0.01f)] private float openDuration = 0.2f;
        [SerializeField, Min(0.01f)] private float closeDuration = 0.15f;
        [SerializeField, Range(0f, 1f)] private float dimTargetAlpha = 0.65f;

        private Sequence currentSequence;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            if (popupPanel == null)
            {
                var transformPanel = transform.Find("PopupPanel");
                if (transformPanel != null) popupPanel = transformPanel.GetComponent<RectTransform>();
                else popupPanel = GetComponent<RectTransform>();
            }

            if (dimBackground == null)
            {
                var transformDim = transform.Find("DimBackground");
                if (transformDim != null) dimBackground = transformDim.GetComponent<Image>();
            }
        }

        public void Open(Action onComplete = null)
        {
            IsOpen = true;
            gameObject.SetActive(true);

            currentSequence?.Kill();
            currentSequence = DOTween.Sequence();

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                currentSequence.Join(canvasGroup.DOFade(1f, openDuration));
            }

            if (dimBackground != null)
            {
                Color c = dimBackground.color;
                c.a = 0f;
                dimBackground.color = c;
                currentSequence.Join(dimBackground.DOFade(dimTargetAlpha, openDuration));
            }

            if (popupPanel != null)
            {
                popupPanel.localScale = Vector3.one * 0.9f;
                currentSequence.Join(popupPanel.DOScale(Vector3.one, openDuration).SetEase(Ease.OutBack));
            }

            currentSequence.OnComplete(() => onComplete?.Invoke());
        }

        public void Close(Action onComplete = null)
        {
            IsOpen = false;

            currentSequence?.Kill();
            currentSequence = DOTween.Sequence();

            if (canvasGroup != null)
            {
                currentSequence.Join(canvasGroup.DOFade(0f, closeDuration));
            }

            if (dimBackground != null)
            {
                currentSequence.Join(dimBackground.DOFade(0f, closeDuration));
            }

            if (popupPanel != null)
            {
                currentSequence.Join(popupPanel.DOScale(Vector3.one * 0.9f, closeDuration).SetEase(Ease.InQuad));
            }

            currentSequence.OnComplete(() =>
            {
                gameObject.SetActive(false);
                onComplete?.Invoke();
            });
        }

        private void OnDestroy()
        {
            currentSequence?.Kill();
        }
    }
}
