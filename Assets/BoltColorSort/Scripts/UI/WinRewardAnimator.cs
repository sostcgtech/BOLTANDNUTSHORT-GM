using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NutBoltSort
{
    /// <summary>
    /// Presentation-only controller for an existing level-complete panel.
    /// Assign the existing UI objects in the Inspector: this component never creates,
    /// moves, or redesigns the permanent panel UI.
    ///
    /// Button setup is deliberately manual. Add OnClaimPressed / OnClaimX2Pressed to
    /// the Buttons' On Click lists after assigning this component.
    /// </summary>
    public sealed class WinRewardAnimator : MonoBehaviour
    {
        [Header("Panel entrance")]
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private RectTransform crown;
        [SerializeField] private RectTransform ribbon;
        [SerializeField] private TMP_Text levelCompleteText;
        [SerializeField] private CanvasGroup levelCompleteTextGroup;

        [Header("Reward entrance")]
        [SerializeField] private RectTransform rewardCoinPile;
        [SerializeField] private TMP_Text rewardAmountText;
        [SerializeField] private CanvasGroup rewardAmountGroup;

        [Header("Claim buttons")]
        [SerializeField] private Button claimButton;
        [SerializeField] private CanvasGroup claimButtonGroup;
        [SerializeField] private Button claimX2Button;
        [SerializeField] private CanvasGroup claimX2ButtonGroup;

        [Header("Wallet and flying coins")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform flyingCoinsLayer;
        [SerializeField] private RectTransform walletRoot;
        [SerializeField] private TMP_Text walletAmountText;
        [SerializeField] private GameObject flyingCoinPrefab;
        [SerializeField, Range(8, 12)] private int pooledCoinCount = 10;
        [SerializeField, Range(8, 12)] private int coinsPerTransfer = 10;

        [Header("Flow")]
        [SerializeField, Min(1)] private int defaultRewardAmount = 150;
        [SerializeField] private UIPopup popupToClose;
        [SerializeField] private GameManager gameManager;
        [Tooltip("Invoked for CLAIM X2. Your rewarded-ad SDK must later call OnRewardedAdSucceeded or OnRewardedAdFailed.")]
        [SerializeField] private UnityEvent requestRewardedAd;

        private readonly List<RectTransform> coinPool = new List<RectTransform>();
        private Sequence entranceSequence;
        private Sequence transferSequence;
        private Vector2 ribbonRestPosition;
        private Vector2 rewardRestPosition;
        private Vector2 claimRestPosition;
        private Vector2 claimX2RestPosition;
        private Vector3 crownRestScale;
        private Vector3 ribbonRestScale;
        private Vector3 titleRestScale;
        private Vector3 rewardRestScale;
        private Vector3 rewardTextRestScale;
        private Vector3 claimRestScale;
        private Vector3 claimX2RestScale;
        private Vector3 walletRestScale;
        private int rewardAmount;
        private bool isShowing;
        private bool isClaiming;
        private bool rewardGranted;
        private int pendingTransferCount;
        private System.Action onAllTransfersComplete;

        private void Awake()
        {
            if (canvas == null) canvas = GetComponentInParent<Canvas>();
            if (popupToClose == null) popupToClose = GetComponent<UIPopup>();
            if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();

            CacheRestState();
            CreatePool();
        }

        private void OnDestroy()
        {
            entranceSequence?.Kill();
            transferSequence?.Kill();
            foreach (RectTransform coin in coinPool)
                if (coin != null) DOTween.Kill(coin);
        }

        /// <summary>Call this immediately after the existing win popup has been opened.</summary>
        public void Show(int amount)
        {
            if (isShowing || isClaiming) return;

            rewardAmount = Mathf.Max(1, amount);
            isShowing = true;
            rewardGranted = false;
            SetClaimButtonsInteractable(false);
            ResetEntranceState();
            PlayEntrance();
        }

        /// <summary>Convenient Inspector test button / default reward entry point.</summary>
        public void ShowDefaultReward() => Show(defaultRewardAmount);

        public void OnClaimPressed()
        {
            if (!isShowing || isClaiming) return;
            StartCoroutine(ClaimRoutine(rewardAmount, claimButton));
        }

        public void OnClaimX2Pressed()
        {
            if (!isShowing || isClaiming) return;

            isClaiming = true;
            SetClaimButtonsInteractable(false);
            PlayButtonPress(claimX2Button != null ? claimX2Button.transform : null);

            // A missing provider fails safely. It never grants a fake x2 reward.
            if (requestRewardedAd == null || requestRewardedAd.GetPersistentEventCount() == 0)
            {
                Debug.LogWarning("[WinRewardAnimator] No rewarded-ad callback is assigned. X2 reward was not granted.", this);
                OnRewardedAdFailed();
                return;
            }

            requestRewardedAd.Invoke();
        }

        /// <summary>Call this only from the real rewarded-ad success callback.</summary>
        public void OnRewardedAdSucceeded()
        {
            if (!isShowing || !isClaiming || rewardGranted) return;
            StartCoroutine(ClaimRoutine(rewardAmount * 2, null, true));
        }

        /// <summary>Call this from a rewarded-ad close/fail callback when no reward was earned.</summary>
        public void OnRewardedAdFailed()
        {
            if (!isShowing || !isClaiming || rewardGranted) return;
            isClaiming = false;
            SetClaimButtonsInteractable(true);
        }

        private void CacheRestState()
        {
            if (ribbon != null) ribbonRestPosition = ribbon.anchoredPosition;
            if (rewardCoinPile != null) rewardRestPosition = rewardCoinPile.anchoredPosition;
            if (claimButton != null) claimRestPosition = ((RectTransform)claimButton.transform).anchoredPosition;
            if (claimX2Button != null) claimX2RestPosition = ((RectTransform)claimX2Button.transform).anchoredPosition;

            if (crown != null) crownRestScale = crown.localScale;
            if (ribbon != null) ribbonRestScale = ribbon.localScale;
            if (levelCompleteText != null) titleRestScale = levelCompleteText.rectTransform.localScale;
            if (rewardCoinPile != null) rewardRestScale = rewardCoinPile.localScale;
            if (rewardAmountText != null) rewardTextRestScale = rewardAmountText.rectTransform.localScale;
            if (claimButton != null) claimRestScale = claimButton.transform.localScale;
            if (claimX2Button != null) claimX2RestScale = claimX2Button.transform.localScale;
            if (walletRoot != null) walletRestScale = walletRoot.localScale;
        }

        private void CreatePool()
        {
            if (flyingCoinPrefab == null || flyingCoinsLayer == null) return;

            for (int i = 0; i < pooledCoinCount; i++)
            {
                GameObject instance = Instantiate(flyingCoinPrefab, flyingCoinsLayer);
                instance.name = flyingCoinPrefab.name + " (Pooled)";
                instance.SetActive(false);
                RectTransform rect = instance.GetComponent<RectTransform>();
                if (rect != null) coinPool.Add(rect);
            }
        }

        private void ResetEntranceState()
        {
            if (panelGroup != null) panelGroup.alpha = 1f;
            if (crown != null)
            {
                crown.localScale = Vector3.zero;
                crown.localRotation = Quaternion.Euler(0f, 0f, -8f);
            }
            if (ribbon != null) ribbon.localScale = Vector3.Scale(ribbonRestScale, new Vector3(.5f, .5f, 1f));
            if (levelCompleteTextGroup != null) levelCompleteTextGroup.alpha = 0f;
            if (levelCompleteText != null) levelCompleteText.rectTransform.localScale = titleRestScale * .85f;
            if (rewardCoinPile != null)
            {
                rewardCoinPile.localScale = Vector3.zero;
                rewardCoinPile.anchoredPosition = rewardRestPosition + Vector2.up * 30f;
                rewardCoinPile.localRotation = Quaternion.identity;
            }
            if (rewardAmountText != null) rewardAmountText.text = $"+{rewardAmount:N0}";
            if (rewardAmountGroup != null) rewardAmountGroup.alpha = 0f;
            if (rewardAmountText != null) rewardAmountText.rectTransform.localScale = rewardTextRestScale * .7f;
            PrepareButton(claimButton, claimButtonGroup, claimRestPosition);
            PrepareButton(claimX2Button, claimX2ButtonGroup, claimX2RestPosition);
        }

        private void PrepareButton(Button button, CanvasGroup group, Vector2 restPosition)
        {
            if (button == null) return;
            ((RectTransform)button.transform).anchoredPosition = restPosition + Vector2.down * 30f;
            button.transform.localScale = button == claimButton ? claimRestScale : claimX2RestScale;
            if (group != null) group.alpha = 0f;
        }

        private void PlayEntrance()
        {
            entranceSequence?.Kill();
            entranceSequence = DOTween.Sequence().SetTarget(this);

            if (crown != null)
            {
                entranceSequence.Append(crown.DOScale(crownRestScale * 1.15f, .20f).SetEase(Ease.OutBack));
                entranceSequence.Join(crown.DOLocalRotate(new Vector3(0f, 0f, 6f), .15f).SetEase(Ease.OutCubic));
                entranceSequence.Append(crown.DOScale(crownRestScale, .10f).SetEase(Ease.OutCubic));
                entranceSequence.Join(crown.DOLocalRotate(Vector3.zero, .10f).SetEase(Ease.OutCubic));
                entranceSequence.AppendCallback(() => HapticManager.Play(HapticType.Light));
            }

            float ribbonTime = Mathf.Max(.08f, entranceSequence.Duration(false) - .12f);
            if (ribbon != null)
            {
                entranceSequence.Insert(ribbonTime, ribbon.DOScale(Vector3.Scale(ribbonRestScale, new Vector3(1.08f, 1f, 1f)), .14f).SetEase(Ease.OutCubic));
                entranceSequence.Insert(ribbonTime + .14f, ribbon.DOScale(ribbonRestScale, .09f).SetEase(Ease.OutQuad));
            }
            if (levelCompleteTextGroup != null) entranceSequence.Insert(ribbonTime + .06f, levelCompleteTextGroup.DOFade(1f, .16f));
            if (levelCompleteText != null)
            {
                entranceSequence.Insert(ribbonTime + .06f, levelCompleteText.rectTransform.DOScale(titleRestScale * 1.05f, .13f).SetEase(Ease.OutCubic));
                entranceSequence.Insert(ribbonTime + .19f, levelCompleteText.rectTransform.DOScale(titleRestScale, .08f).SetEase(Ease.OutQuad));
            }

            float rewardTime = Mathf.Max(.42f, entranceSequence.Duration(false) + .02f);
            if (rewardCoinPile != null)
            {
                entranceSequence.Insert(rewardTime, rewardCoinPile.DOAnchorPos(rewardRestPosition, .20f).SetEase(Ease.OutCubic));
                entranceSequence.Insert(rewardTime, rewardCoinPile.DOScale(rewardRestScale * 1.15f, .16f).SetEase(Ease.OutBack));
                entranceSequence.Insert(rewardTime + .16f, rewardCoinPile.DOScale(rewardRestScale * .95f, .07f));
                entranceSequence.Insert(rewardTime + .23f, rewardCoinPile.DOScale(rewardRestScale, .07f));
                entranceSequence.Insert(rewardTime + .16f, rewardCoinPile.DOLocalRotate(new Vector3(0f, 0f, 3f), .06f));
                entranceSequence.Insert(rewardTime + .22f, rewardCoinPile.DOLocalRotate(new Vector3(0f, 0f, -2f), .05f));
                entranceSequence.Insert(rewardTime + .27f, rewardCoinPile.DOLocalRotate(Vector3.zero, .05f));
            }
            if (rewardAmountGroup != null) entranceSequence.Insert(rewardTime + .18f, rewardAmountGroup.DOFade(1f, .12f));
            if (rewardAmountText != null)
            {
                entranceSequence.Insert(rewardTime + .18f, rewardAmountText.rectTransform.DOScale(rewardTextRestScale * 1.1f, .12f));
                entranceSequence.Insert(rewardTime + .30f, rewardAmountText.rectTransform.DOScale(rewardTextRestScale, .08f));
            }

            float buttonsTime = rewardTime + .34f;
            InsertButtonEntrance(entranceSequence, claimButton, claimButtonGroup, claimRestPosition, buttonsTime);
            InsertButtonEntrance(entranceSequence, claimX2Button, claimX2ButtonGroup, claimX2RestPosition, buttonsTime + .05f);
            entranceSequence.OnComplete(() => SetClaimButtonsInteractable(true));
        }

        private static void InsertButtonEntrance(Sequence sequence, Button button, CanvasGroup group, Vector2 rest, float time)
        {
            if (button == null) return;
            RectTransform rect = (RectTransform)button.transform;
            sequence.Insert(time, rect.DOAnchorPos(rest, .18f).SetEase(Ease.OutCubic));
            if (group != null) sequence.Insert(time, group.DOFade(1f, .16f));
        }

        private IEnumerator ClaimRoutine(int amount, Button pressedButton, bool alreadyPressed = false)
        {
            if (rewardGranted) yield break;
            isClaiming = true;
            SetClaimButtonsInteractable(false);
            if (!alreadyPressed) PlayButtonPress(pressedButton != null ? pressedButton.transform : null);

            int startBalance = PlayerWallet.GetCoins();
            int endBalance = startBalance + Mathf.Max(1, amount);
            rewardGranted = true;
            PlayerWallet.AddCoins(amount); // saved exactly once, before the next level transition
            AudioManager.Play(SfxType.Reward);
            HapticManager.Play(HapticType.Light);

            // Wait for every flying coin AND the wallet counter to finish before
            // closing the popup. The popup closes only after all coins land.
            bool transferDone = false;
            PlayCoinTransfer(startBalance, endBalance, () => transferDone = true);
            yield return new WaitUntil(() => transferDone);

            HapticManager.Play(HapticType.Medium);
            yield return new WaitForSecondsRealtime(.25f);

            if (popupToClose != null)
                popupToClose.Close(LoadNextLevel);
            else
                LoadNextLevel();
        }

        private void PlayCoinTransfer(int startBalance, int endBalance, System.Action onComplete = null)
        {
            transferSequence?.Kill();
            transferSequence = DOTween.Sequence().SetTarget(this);

            int count = Mathf.Min(coinsPerTransfer, coinPool.Count);

            // One pending slot per flying coin + one for the wallet counter tween.
            pendingTransferCount = count + (walletAmountText != null ? 1 : 0);
            onAllTransfersComplete = onComplete;

            if (walletAmountText != null)
                transferSequence.Join(
                    DOTween.To(() => startBalance, v => walletAmountText.text = v.ToString("N0"), endBalance, .70f)
                           .SetEase(Ease.OutQuad)
                           .OnComplete(OnTransferItemDone));

            for (int i = 0; i < count; i++)
                LaunchCoin(coinPool[i], i, count);
        }

        /// <summary>Called by each flying coin and by the wallet counter when they finish.
        /// The popup closes only after every last piece completes.</summary>
        private void OnTransferItemDone()
        {
            if (--pendingTransferCount <= 0)
                onAllTransfersComplete?.Invoke();
        }

        private void LaunchCoin(RectTransform coin, int index, int count)
        {
            if (coin == null || rewardCoinPile == null || walletRoot == null || flyingCoinsLayer == null) return;
            DOTween.Kill(coin);
            coin.gameObject.SetActive(true);

            Vector2 start = WorldToLayerPoint(rewardCoinPile.position) + UnityEngine.Random.insideUnitCircle * 16f;
            Vector2 end = WorldToLayerPoint(walletRoot.position);
            Vector2 control = Vector2.Lerp(start, end, .42f) + new Vector2(UnityEngine.Random.Range(-85f, 85f), UnityEngine.Random.Range(55f, 120f));
            coin.anchoredPosition = start;
            coin.localScale = Vector3.one;
            coin.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-15f, 15f));

            float delay = index * .045f;
            Sequence seq = DOTween.Sequence().SetTarget(coin);
            seq.AppendInterval(delay);
            seq.Append(coin.DOAnchorPos(start + (control - start) * .20f, .09f).SetEase(Ease.OutQuad));
            seq.Append(coin.DOPath(new[] { (Vector3)control, (Vector3)end }, .31f, PathType.CatmullRom).SetEase(Ease.InQuad));
            seq.Join(coin.DOScale(.75f, .31f));
            seq.Join(coin.DOLocalRotate(new Vector3(0f, 0f, UnityEngine.Random.Range(-25f, 25f)), .31f));
            seq.OnComplete(() =>
            {
                coin.gameObject.SetActive(false);
                if ((index + 1) % 3 == 0 || index == count - 1) PlayWalletImpact(index == count - 1);
                OnTransferItemDone(); // count down — popup closes only when all coins land
            });
        }

        private Vector2 WorldToLayerPoint(Vector3 worldPosition)
        {
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCamera, worldPosition);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(flyingCoinsLayer, screen, uiCamera, out Vector2 local);
            return local;
        }

        private void PlayWalletImpact(bool final)
        {
            if (walletRoot == null) return;
            DOTween.Kill(walletRoot);
            Sequence seq = DOTween.Sequence().SetTarget(walletRoot);
            if (final)
            {
                seq.Append(walletRoot.DOScale(walletRestScale * 1.15f, .08f));
                seq.Join(walletRoot.DOLocalRotate(new Vector3(0f, 0f, 4f), .08f));
                seq.Append(walletRoot.DOScale(walletRestScale * .96f, .07f));
                seq.Append(walletRoot.DOScale(walletRestScale, .08f));
                seq.Join(walletRoot.DOLocalRotate(Vector3.zero, .08f));
            }
            else
            {
                seq.Append(walletRoot.DOScale(walletRestScale * 1.06f, .06f));
                seq.Append(walletRoot.DOScale(walletRestScale, .08f));
            }
        }

        private static void PlayButtonPress(Transform target)
        {
            if (target == null) return;
            DOTween.Kill(target);
            Sequence seq = DOTween.Sequence().SetTarget(target);
            seq.Append(target.DOScale(.92f, .06f));
            seq.Append(target.DOScale(1.04f, .08f));
            seq.Append(target.DOScale(1f, .07f));
        }

        private void SetClaimButtonsInteractable(bool enabled)
        {
            if (claimButton != null) claimButton.interactable = enabled;
            if (claimX2Button != null) claimX2Button.interactable = enabled;
        }

        private void LoadNextLevel()
        {
            isShowing = false;
            isClaiming = false;
            gameManager?.LoadNextLevel();
        }
    }
}
