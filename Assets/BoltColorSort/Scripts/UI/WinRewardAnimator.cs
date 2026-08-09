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
    /// Single, authoritative controller for the Level Complete / Win Panel.
    /// Handles panel entrance, reward display, button feedback, coin-transfer animation,
    /// wallet interpolation, haptics, and advancing to the next level.
    /// Reads and syncs directly with PlayerWallet.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WinRewardAnimator : MonoBehaviour
    {
        [Header("Panel Entrance")]
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private RectTransform crown;
        [SerializeField] private RectTransform ribbon;
        [SerializeField] private TMP_Text levelCompleteText;
        [SerializeField] private CanvasGroup levelCompleteTextGroup;

        [Header("Reward Entrance")]
        [SerializeField] private RectTransform rewardCoinPile;
        [SerializeField] private TMP_Text rewardAmountText;
        [SerializeField] private CanvasGroup rewardAmountGroup;

        [Header("Claim Buttons")]
        [SerializeField] private Button claimButton;
        [SerializeField] private CanvasGroup claimButtonGroup;
        [SerializeField] private Button claimX2Button;
        [SerializeField] private CanvasGroup claimX2ButtonGroup;

        [Header("Wallet and Flying Coins")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform flyingCoinsLayer;
        [SerializeField] private RectTransform walletRoot;
        [SerializeField] private TMP_Text walletAmountText;
        [SerializeField] private GameObject flyingCoinPrefab;
        [SerializeField, Range(8, 16)] private int pooledCoinCount = 12;
        [SerializeField, Range(8, 16)] private int coinsPerTransfer = 10;

        [Header("Flow")]
        [SerializeField, Min(1)] private int defaultRewardAmount = 150;
        [SerializeField] private GameManager gameManager;
        [Tooltip("Invoked for CLAIM X2. Your rewarded-ad SDK must call OnRewardedAdSucceeded or OnRewardedAdFailed.")]
        [SerializeField] private UnityEvent requestRewardedAd;

        private readonly List<RectTransform> coinPool = new List<RectTransform>();
        private Sequence entranceSequence;
        private Sequence transferSequence;

        private Vector2 ribbonRestPosition;
        private Vector2 rewardRestPosition;
        private Vector2 claimRestPosition;
        private Vector2 claimX2RestPosition;

        private Vector3 crownRestScale = Vector3.one;
        private Vector3 ribbonRestScale = Vector3.one;
        private Vector3 titleRestScale = Vector3.one;
        private Vector3 rewardRestScale = Vector3.one;
        private Vector3 rewardTextRestScale = Vector3.one;
        private Vector3 claimRestScale = Vector3.one;
        private Vector3 claimX2RestScale = Vector3.one;
        private Vector3 walletRestScale = Vector3.one;

        private int rewardAmount;
        private bool isShowing;
        private bool isClaiming;
        private bool rewardGranted;
        private int pendingTransferCount;
        private Action onAllTransfersComplete;

        public bool IsOpen => isShowing || isClaiming || (gameObject.activeSelf && (panelGroup == null || panelGroup.alpha > 0.05f));

        private void Awake()
        {
            // Disable any legacy popup script to ensure only ONE win controller runs.
            var oldPopup = GetComponent<UIPopup>();
            if (oldPopup != null) oldPopup.enabled = false;

            AutoDiscoverReferences();
            CacheRestState();
            BindButtons();
            CreatePool();
        }

        private void Start()
        {
            // Sync with PlayerWallet immediately on scene start so it displays the real balance.
            RefreshWalletDisplay(PlayerWallet.GetCoins());
            PlayerWallet.OnCoinsChanged += OnWalletCoinsChanged;
        }

        private void OnDestroy()
        {
            PlayerWallet.OnCoinsChanged -= OnWalletCoinsChanged;
            entranceSequence?.Kill();
            transferSequence?.Kill();
            foreach (RectTransform coin in coinPool)
            {
                if (coin != null) DOTween.Kill(coin);
            }
        }

        private void OnWalletCoinsChanged(int newBalance)
        {
            // Update the display text if not currently in the middle of a transfer animation.
            if (!isClaiming)
            {
                RefreshWalletDisplay(newBalance);
            }
        }

        private void RefreshWalletDisplay(int balance)
        {
            if (walletAmountText != null)
            {
                walletAmountText.text = balance.ToString("N0");
            }
        }

        /// <summary>Opens the Win Panel and plays the complete Level Complete sequence.</summary>
        public void Show(int amount)
        {
            if (isClaiming) return;

            // Kill any previous tweens and routines to guarantee a pristine state.
            StopAllCoroutines();
            entranceSequence?.Kill();
            transferSequence?.Kill();

            rewardAmount = Mathf.Max(1, amount);
            isShowing = true;
            isClaiming = false;
            rewardGranted = false;

            gameObject.SetActive(true);
            SetClaimButtonsInteractable(false);
            ResetEntranceState();
            PlayEntrance();
        }

        /// <summary>Convenient Inspector test entry point.</summary>
        public void ShowDefaultReward() => Show(defaultRewardAmount);

        /// <summary>Immediately closes the win panel and resets state (e.g. on level restart).</summary>
        public void HideImmediate()
        {
            StopAllCoroutines();
            entranceSequence?.Kill();
            transferSequence?.Kill();

            isShowing = false;
            isClaiming = false;
            rewardGranted = false;

            if (panelGroup != null) panelGroup.alpha = 0f;
            foreach (RectTransform coin in coinPool)
            {
                if (coin != null)
                {
                    DOTween.Kill(coin);
                    coin.gameObject.SetActive(false);
                }
            }

            gameObject.SetActive(false);
        }

        public void OnClaimPressed()
        {
            if (!isShowing || isClaiming) return;
            isClaiming = true;
            SetClaimButtonsInteractable(false);
            PlayButtonPress(claimButton != null ? claimButton.transform : null);
            StartCoroutine(ClaimRoutine(rewardAmount));
        }

        public void OnClaimX2Pressed()
        {
            if (!isShowing || isClaiming) return;
            isClaiming = true;
            SetClaimButtonsInteractable(false);
            PlayButtonPress(claimX2Button != null ? claimX2Button.transform : null);

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
            StartCoroutine(ClaimRoutine(rewardAmount * 2));
        }

        /// <summary>Call this from a rewarded-ad close/fail callback when no reward was earned.</summary>
        public void OnRewardedAdFailed()
        {
            if (!isShowing || !isClaiming || rewardGranted) return;
            isClaiming = false;
            SetClaimButtonsInteractable(true);
        }

        private IEnumerator ClaimRoutine(int amount)
        {
            if (rewardGranted) yield break;
            rewardGranted = true;

            int startBalance = PlayerWallet.GetCoins();
            int endBalance = startBalance + Mathf.Max(1, amount);

            // REAL saved balance is awarded ONCE here to the shared PlayerWallet database.
            PlayerWallet.AddCoins(amount);

            AudioManager.Play(SfxType.Reward);
            HapticManager.Play(HapticType.Light);

            // Transfer visual coins and smoothly tick the wallet counter.
            bool transferDone = false;
            PlayCoinTransfer(startBalance, endBalance, () => transferDone = true);
            yield return new WaitUntil(() => transferDone);

            // Final wallet impact and medium haptic.
            PlayWalletImpact(final: true);
            HapticManager.Play(HapticType.Medium);

            yield return new WaitForSecondsRealtime(0.25f);

            // Smoothly fade out the win panel.
            if (panelGroup != null)
            {
                panelGroup.DOFade(0f, 0.20f).SetEase(Ease.InQuad);
                yield return new WaitForSecondsRealtime(0.20f);
            }

            gameObject.SetActive(false);
            isShowing = false;
            isClaiming = false;

            // Advance automatically to the next level.
            gameManager?.LoadNextLevel();
        }

        private void PlayCoinTransfer(int startBalance, int endBalance, Action onComplete = null)
        {
            transferSequence?.Kill();
            transferSequence = DOTween.Sequence().SetTarget(this);

            int count = Mathf.Min(coinsPerTransfer, coinPool.Count);
            pendingTransferCount = count + (walletAmountText != null ? 1 : 0);
            onAllTransfersComplete = onComplete;

            if (walletAmountText != null)
            {
                transferSequence.Join(
                    DOTween.To(() => startBalance, v => walletAmountText.text = v.ToString("N0"), endBalance, 0.70f)
                           .SetEase(Ease.OutQuad)
                           .OnComplete(OnTransferItemDone));
            }

            for (int i = 0; i < count; i++)
            {
                LaunchCoin(coinPool[i], i, count);
            }
        }

        private void OnTransferItemDone()
        {
            if (--pendingTransferCount <= 0)
            {
                onAllTransfersComplete?.Invoke();
            }
        }

        private void LaunchCoin(RectTransform coin, int index, int count)
        {
            if (coin == null || flyingCoinsLayer == null)
            {
                OnTransferItemDone();
                return;
            }

            DOTween.Kill(coin);
            coin.gameObject.SetActive(true);

            Vector2 start = GetLayerPoint(rewardCoinPile) + UnityEngine.Random.insideUnitCircle * 18f;
            Vector2 end = walletRoot != null ? GetLayerPoint(walletRoot) : GetDefaultTopWalletLayerPos();

            // Keep the initial pop varied, then fly directly to the wallet.
            // Both start and end are anchored positions in flyingCoinsLayer space.
            Vector2 popDirection = (end - start).sqrMagnitude > 0.001f
                ? (end - start).normalized
                : Vector2.up;

            coin.anchoredPosition = start;
            coin.localScale = Vector3.one;
            coin.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-15f, 15f));

            float delay = index * 0.045f;
            Sequence seq = DOTween.Sequence().SetTarget(coin);
            seq.AppendInterval(delay);

            // 1. Pop outward slightly from the pile.
            Vector2 popTarget = start + popDirection * 25f;
            seq.Append(coin.DOAnchorPos(popTarget, 0.08f).SetEase(Ease.OutQuad));

            // 2. Move in the same UI coordinate space directly to WalletRoot.
            // DOPath moves a Transform in world space, so it must not be used with
            // the anchored UI positions returned by GetLayerPoint.
            seq.Append(coin.DOAnchorPos(end, 0.32f).SetEase(Ease.InQuad));
            seq.Join(coin.DOScale(0.75f, 0.32f));
            seq.Join(coin.DOLocalRotate(new Vector3(0f, 0f, UnityEngine.Random.Range(-30f, 30f)), 0.32f));

            seq.OnComplete(() =>
            {
                coin.gameObject.SetActive(false);

                // Periodic pulse during collection + tick haptic.
                if ((index + 1) % 3 == 0 || index == count - 1)
                {
                    PlayWalletImpact(final: false);
                    HapticManager.Play(HapticType.Light);
                }

                OnTransferItemDone();
            });
        }

        private Vector2 GetLayerPoint(RectTransform targetRect)
        {
            if (targetRect == null || flyingCoinsLayer == null) return Vector2.zero;

            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(uiCamera, targetRect.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(flyingCoinsLayer, screenPoint, uiCamera, out Vector2 localPoint);
            return localPoint;
        }

        private Vector2 GetDefaultTopWalletLayerPos()
        {
            if (flyingCoinsLayer != null)
            {
                return new Vector2(0f, (flyingCoinsLayer.rect.height * 0.5f) - 80f);
            }
            return new Vector2(0f, 400f);
        }

        private void PlayWalletImpact(bool final)
        {
            if (walletRoot == null) return;
            DOTween.Kill(walletRoot);
            Sequence seq = DOTween.Sequence().SetTarget(walletRoot);
            if (final)
            {
                seq.Append(walletRoot.DOScale(walletRestScale * 1.15f, 0.08f).SetEase(Ease.OutQuad));
                seq.Join(walletRoot.DOLocalRotate(new Vector3(0f, 0f, 4f), 0.08f));
                seq.Append(walletRoot.DOScale(walletRestScale * 0.96f, 0.07f));
                seq.Append(walletRoot.DOScale(walletRestScale, 0.08f));
                seq.Join(walletRoot.DOLocalRotate(Vector3.zero, 0.08f));
            }
            else
            {
                seq.Append(walletRoot.DOScale(walletRestScale * 1.06f, 0.06f).SetEase(Ease.OutQuad));
                seq.Append(walletRoot.DOScale(walletRestScale, 0.07f).SetEase(Ease.InQuad));
            }
        }

        private static void PlayButtonPress(Transform target)
        {
            if (target == null) return;
            DOTween.Kill(target);
            Sequence seq = DOTween.Sequence().SetTarget(target);
            seq.Append(target.DOScale(0.92f, 0.06f).SetEase(Ease.OutQuad));
            seq.Append(target.DOScale(1.04f, 0.08f).SetEase(Ease.OutBack));
            seq.Append(target.DOScale(1f, 0.07f).SetEase(Ease.OutQuad));
        }

        private void SetClaimButtonsInteractable(bool enabled)
        {
            if (claimButton != null) claimButton.interactable = enabled;
            if (claimX2Button != null) claimX2Button.interactable = enabled;
        }

        private void ResetEntranceState()
        {
            if (panelGroup != null) panelGroup.alpha = 1f;

            if (crown != null)
            {
                crown.localScale = Vector3.zero;
                crown.localRotation = Quaternion.Euler(0f, 0f, -8f);
            }

            if (ribbon != null)
            {
                ribbon.localScale = Vector3.Scale(ribbonRestScale, new Vector3(0.5f, 0.5f, 1f));
            }

            if (levelCompleteTextGroup != null) levelCompleteTextGroup.alpha = 0f;
            if (levelCompleteText != null)
            {
                levelCompleteText.rectTransform.localScale = titleRestScale * 0.85f;
            }

            if (rewardCoinPile != null)
            {
                rewardCoinPile.localScale = Vector3.zero;
                rewardCoinPile.anchoredPosition = rewardRestPosition + Vector2.up * 30f;
                rewardCoinPile.localRotation = Quaternion.identity;
            }

            if (rewardAmountText != null)
            {
                rewardAmountText.text = $"+{rewardAmount:N0}";
                rewardAmountText.rectTransform.localScale = rewardTextRestScale * 0.7f;
            }
            if (rewardAmountGroup != null) rewardAmountGroup.alpha = 0f;

            PrepareButton(claimButton, claimButtonGroup, claimRestPosition, claimRestScale);
            PrepareButton(claimX2Button, claimX2ButtonGroup, claimX2RestPosition, claimX2RestScale);
        }

        private static void PrepareButton(Button button, CanvasGroup group, Vector2 restPosition, Vector3 restScale)
        {
            if (button == null) return;
            ((RectTransform)button.transform).anchoredPosition = restPosition + Vector2.down * 30f;
            button.transform.localScale = restScale;
            if (group != null) group.alpha = 0f;
        }

        private void PlayEntrance()
        {
            entranceSequence?.Kill();
            entranceSequence = DOTween.Sequence().SetTarget(this);

            // 1. Crown enters
            if (crown != null)
            {
                entranceSequence.Append(crown.DOScale(crownRestScale * 1.15f, 0.20f).SetEase(Ease.OutBack));
                entranceSequence.Join(crown.DOLocalRotate(new Vector3(0f, 0f, 6f), 0.15f).SetEase(Ease.OutCubic));
                entranceSequence.Append(crown.DOScale(crownRestScale, 0.10f).SetEase(Ease.OutCubic));
                entranceSequence.Join(crown.DOLocalRotate(Vector3.zero, 0.10f).SetEase(Ease.OutCubic));
                entranceSequence.AppendCallback(() => HapticManager.Play(HapticType.Light));
            }

            // 2. Ribbon unfolds underneath crown
            float ribbonTime = Mathf.Max(0.08f, entranceSequence.Duration(false) - 0.12f);
            if (ribbon != null)
            {
                entranceSequence.Insert(ribbonTime, ribbon.DOScale(Vector3.Scale(ribbonRestScale, new Vector3(1.08f, 1f, 1f)), 0.14f).SetEase(Ease.OutCubic));
                entranceSequence.Insert(ribbonTime + 0.14f, ribbon.DOScale(ribbonRestScale, 0.09f).SetEase(Ease.OutQuad));
            }

            // 3. "LEVEL COMPLETE!" text pops in subtly
            if (levelCompleteTextGroup != null)
            {
                entranceSequence.Insert(ribbonTime + 0.06f, levelCompleteTextGroup.DOFade(1f, 0.16f));
            }
            if (levelCompleteText != null)
            {
                entranceSequence.Insert(ribbonTime + 0.06f, levelCompleteText.rectTransform.DOScale(titleRestScale * 1.05f, 0.13f).SetEase(Ease.OutCubic));
                entranceSequence.Insert(ribbonTime + 0.19f, levelCompleteText.rectTransform.DOScale(titleRestScale, 0.08f).SetEase(Ease.OutQuad));
            }

            // 4. Reward coin pile pops up
            float rewardTime = Mathf.Max(0.42f, entranceSequence.Duration(false) + 0.02f);
            if (rewardCoinPile != null)
            {
                entranceSequence.Insert(rewardTime, rewardCoinPile.DOAnchorPos(rewardRestPosition, 0.20f).SetEase(Ease.OutCubic));
                entranceSequence.Insert(rewardTime, rewardCoinPile.DOScale(rewardRestScale * 1.15f, 0.16f).SetEase(Ease.OutBack));
                entranceSequence.Insert(rewardTime + 0.16f, rewardCoinPile.DOScale(rewardRestScale * 0.95f, 0.07f));
                entranceSequence.Insert(rewardTime + 0.23f, rewardCoinPile.DOScale(rewardRestScale, 0.07f));
                entranceSequence.Insert(rewardTime + 0.16f, rewardCoinPile.DOLocalRotate(new Vector3(0f, 0f, 3f), 0.06f));
                entranceSequence.Insert(rewardTime + 0.22f, rewardCoinPile.DOLocalRotate(new Vector3(0f, 0f, -2f), 0.05f));
                entranceSequence.Insert(rewardTime + 0.27f, rewardCoinPile.DOLocalRotate(Vector3.zero, 0.05f));
            }

            // 5. Reward amount text fades & pops
            if (rewardAmountGroup != null)
            {
                entranceSequence.Insert(rewardTime + 0.18f, rewardAmountGroup.DOFade(1f, 0.12f));
            }
            if (rewardAmountText != null)
            {
                entranceSequence.Insert(rewardTime + 0.18f, rewardAmountText.rectTransform.DOScale(rewardTextRestScale * 1.1f, 0.12f).SetEase(Ease.OutBack));
                entranceSequence.Insert(rewardTime + 0.30f, rewardAmountText.rectTransform.DOScale(rewardTextRestScale, 0.08f));
            }

            // 6. Claim buttons slide in
            float buttonsTime = rewardTime + 0.32f;
            InsertButtonEntrance(entranceSequence, claimButton, claimButtonGroup, claimRestPosition, buttonsTime);
            InsertButtonEntrance(entranceSequence, claimX2Button, claimX2ButtonGroup, claimX2RestPosition, buttonsTime + 0.05f);

            entranceSequence.OnComplete(() => SetClaimButtonsInteractable(true));
        }

        private static void InsertButtonEntrance(Sequence sequence, Button button, CanvasGroup group, Vector2 rest, float time)
        {
            if (button == null) return;
            RectTransform rect = (RectTransform)button.transform;
            sequence.Insert(time, rect.DOAnchorPos(rest, 0.18f).SetEase(Ease.OutCubic));
            if (group != null) sequence.Insert(time, group.DOFade(1f, 0.16f));
        }

        private void BindButtons()
        {
            if (claimButton != null)
            {
                claimButton.onClick.RemoveAllListeners();
                claimButton.onClick.AddListener(OnClaimPressed);
            }

            if (claimX2Button != null)
            {
                claimX2Button.onClick.RemoveAllListeners();
                claimX2Button.onClick.AddListener(OnClaimX2Pressed);
            }
        }

        private void AutoDiscoverReferences()
        {
            if (canvas == null) canvas = GetComponentInParent<Canvas>() ?? FindAnyObjectByType<Canvas>();
            if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();

            if (panelGroup == null) panelGroup = GetComponent<CanvasGroup>() ?? GetComponentInChildren<CanvasGroup>();

            if (crown == null)
            {
                var t = transform.Find("PopupPanel/Crown") ?? transform.Find("Crown");
                if (t != null) crown = t.GetComponent<RectTransform>();
            }

            if (ribbon == null)
            {
                var t = transform.Find("PopupPanel/Ribon") ?? transform.Find("PopupPanel/Ribbon") ?? transform.Find("Ribon") ?? transform.Find("Ribbon");
                if (t != null) ribbon = t.GetComponent<RectTransform>();
            }

            if (levelCompleteText == null)
            {
                var t = transform.Find("PopupPanel/Ribon/Text") ?? transform.Find("PopupPanel/Text") ?? transform.Find("PopupPanel/LevelCompleteText");
                if (t != null) levelCompleteText = t.GetComponent<TMP_Text>();
            }
            if (levelCompleteText != null && levelCompleteTextGroup == null)
            {
                levelCompleteTextGroup = levelCompleteText.GetComponent<CanvasGroup>() ?? levelCompleteText.gameObject.AddComponent<CanvasGroup>();
            }

            if (rewardCoinPile == null)
            {
                var t = transform.Find("PopupPanel/CoinCenter") ?? transform.Find("PopupPanel/RewardCoinPile") ?? transform.Find("CoinCenter");
                if (t != null) rewardCoinPile = t.GetComponent<RectTransform>();
            }

            if (rewardAmountText == null)
            {
                var t = transform.Find("PopupPanel/coinReward") ?? transform.Find("PopupPanel/RewardAmountText") ?? transform.Find("coinReward");
                if (t != null) rewardAmountText = t.GetComponent<TMP_Text>();
            }
            if (rewardAmountText != null && rewardAmountGroup == null)
            {
                rewardAmountGroup = rewardAmountText.GetComponent<CanvasGroup>() ?? rewardAmountText.gameObject.AddComponent<CanvasGroup>();
            }

            if (claimButton == null)
            {
                var btns = GetComponentsInChildren<Button>(true);
                foreach (var b in btns)
                {
                    if (b.name.IndexOf("x2", StringComparison.OrdinalIgnoreCase) < 0 &&
                        (b.name.IndexOf("claim", StringComparison.OrdinalIgnoreCase) >= 0 || b.name.IndexOf("next", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        claimButton = b;
                        break;
                    }
                }
            }
            if (claimButton != null && claimButtonGroup == null)
            {
                claimButtonGroup = claimButton.GetComponent<CanvasGroup>() ?? claimButton.gameObject.AddComponent<CanvasGroup>();
            }

            if (claimX2Button == null)
            {
                var btns = GetComponentsInChildren<Button>(true);
                foreach (var b in btns)
                {
                    if (b.name.IndexOf("x2", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        claimX2Button = b;
                        break;
                    }
                }
            }
            if (claimX2Button != null && claimX2ButtonGroup == null)
            {
                claimX2ButtonGroup = claimX2Button.GetComponent<CanvasGroup>() ?? claimX2Button.gameObject.AddComponent<CanvasGroup>();
            }

            // Ensure flying coins layer exists on the root canvas on top of everything.
            if (flyingCoinsLayer == null && canvas != null)
            {
                var existingLayer = canvas.transform.Find("FlyingCoinsLayer");
                if (existingLayer != null)
                {
                    flyingCoinsLayer = existingLayer.GetComponent<RectTransform>();
                }
                else
                {
                    var layerGO = new GameObject("FlyingCoinsLayer", typeof(RectTransform));
                    layerGO.transform.SetParent(canvas.transform, false);
                    flyingCoinsLayer = layerGO.GetComponent<RectTransform>();
                    flyingCoinsLayer.anchorMin = Vector2.zero;
                    flyingCoinsLayer.anchorMax = Vector2.one;
                    flyingCoinsLayer.offsetMin = Vector2.zero;
                    flyingCoinsLayer.offsetMax = Vector2.zero;
                    flyingCoinsLayer.pivot = new Vector2(0.5f, 0.5f);
                }
            }
            if (flyingCoinsLayer != null)
            {
                flyingCoinsLayer.SetAsLastSibling();
            }

            // Auto-discover wallet counter in Canvas / TopBar.
            if (walletRoot == null && canvas != null)
            {
                var texts = canvas.GetComponentsInChildren<TMP_Text>(true);
                foreach (var t in texts)
                {
                    if (t.name.IndexOf("coin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.name.IndexOf("wallet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (t.transform.parent != null && (t.transform.parent.name.IndexOf("coin", StringComparison.OrdinalIgnoreCase) >= 0 || t.transform.parent.name.IndexOf("wallet", StringComparison.OrdinalIgnoreCase) >= 0)))
                    {
                        if (!t.transform.IsChildOf(transform))
                        {
                            walletAmountText = t;
                            walletRoot = t.rectTransform;
                            break;
                        }
                    }
                }
            }

            if (walletRoot == null && canvas != null)
            {
                var topBar = canvas.transform.Find("SafeArea/TopBar") ?? canvas.transform.Find("TopBar");
                if (topBar != null)
                {
                    walletRoot = topBar.GetComponent<RectTransform>();
                }
            }

            if (walletAmountText == null && walletRoot != null)
            {
                walletAmountText = walletRoot.GetComponentInChildren<TMP_Text>();
            }
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
            if (flyingCoinsLayer == null) return;

            Sprite coinSprite = null;
            if (rewardCoinPile != null)
            {
                var img = rewardCoinPile.GetComponent<Image>();
                if (img != null) coinSprite = img.sprite;
            }

            for (int i = 0; i < pooledCoinCount; i++)
            {
                GameObject instance;
                if (flyingCoinPrefab != null)
                {
                    instance = Instantiate(flyingCoinPrefab, flyingCoinsLayer);
                }
                else
                {
                    instance = new GameObject("FlyingCoin_Pooled", typeof(RectTransform), typeof(Image));
                    instance.transform.SetParent(flyingCoinsLayer, false);
                    var img = instance.GetComponent<Image>();
                    img.raycastTarget = false;
                    if (coinSprite != null) img.sprite = coinSprite;
                    else img.color = new Color(1f, 0.85f, 0.15f);
                    var rt = instance.GetComponent<RectTransform>();
                    rt.sizeDelta = new Vector2(64f, 64f);
                }

                instance.SetActive(false);
                var rect = instance.GetComponent<RectTransform>();
                if (rect != null) coinPool.Add(rect);
            }
        }
    }
}
