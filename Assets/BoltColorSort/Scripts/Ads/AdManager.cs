using System;
using System.Collections;
using UnityEngine;

#if ADMOB_ENABLED
using GoogleMobileAds.Api;
#endif

namespace NutBoltSort
{
    /// <summary>
    /// Central AdMob manager — the ONLY script that interacts directly with
    /// the Google Mobile Ads SDK.
    ///
    /// All other systems (GameManager, UIManager, WinRewardAnimator, ShopPopup)
    /// call methods on this class and never touch the SDK themselves.
    ///
    /// Initialization happens ONCE during GameBootstrap via EnsureInstance().
    /// The resulting instance persists across all scenes (DontDestroyOnLoad).
    ///
    /// Compile guard: all SDK calls are wrapped in #if ADMOB_ENABLED so the
    /// project compiles and runs normally when the Google Mobile Ads package
    /// is not installed. When the package is absent the class still exists and
    /// its public API works — it just logs placeholder messages instead of
    /// showing real ads.
    ///
    /// HOW TO ENABLE:
    ///   1. Import the Google Mobile Ads Unity package.
    ///   2. Add ADMOB_ENABLED to Project Settings → Player → Scripting Define Symbols.
    ///   3. Add your App ID to AndroidManifest.xml (see AdConfig.cs for the test ID).
    /// </summary>
    [AddComponentMenu("BoltShift/Ads/Ad Manager")]
    [DisallowMultipleComponent]
    public sealed class AdManager : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Singleton
        // ─────────────────────────────────────────────────────────────────────

        public static AdManager Instance { get; private set; }

        /// <summary>
        /// Ensures exactly one AdManager exists on the given host and persists
        /// across scenes. Safe to call from BootTask_Managers or any scene Awake.
        /// </summary>
        public static AdManager EnsureInstance(GameObject host)
        {
            if (Instance != null) return Instance;
            Instance = host.GetComponent<AdManager>();
            if (Instance == null) Instance = host.AddComponent<AdManager>();
            return Instance;
        }

        // ─────────────────────────────────────────────────────────────────────
        // State
        // ─────────────────────────────────────────────────────────────────────

        private bool _sdkInitialized;
        private bool _adsRemoved;
        private bool _isAdShowing; // true while any full-screen ad is visible

        // Banner
        private bool _bannerVisible;

#if ADMOB_ENABLED
        private BannerView       _bannerView;
        private InterstitialAd   _interstitialAd;
        private RewardedAd       _rewardedAd;
#endif

        // Rewarded ad state
        private RewardType _pendingRewardType;
        private Action     _pendingOnRewardGranted;
        private Action     _pendingOnFailed;
        private bool       _rewardEarned;

        // Interstitial state
        private Action     _interstitialOnClosed;

        // ─────────────────────────────────────────────────────────────────────
        // Public Properties
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>True when the player has purchased Remove Ads.</summary>
        public bool AreAdsRemoved => _adsRemoved;

        /// <summary>True while any full-screen ad overlay is visible.</summary>
        public bool IsAdShowing => _isAdShowing;

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            // Prevent duplicate instances.
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            // Load persisted Remove Ads state.
            _adsRemoved = PlayerPrefs.GetInt(AdConfig.AdsRemovedKey, 0) == 1;

            InitializeSdk();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            DestroyBanner();
#if ADMOB_ENABLED
            _interstitialAd?.Destroy();
            _rewardedAd?.Destroy();
#endif
        }

        // ─────────────────────────────────────────────────────────────────────
        // SDK Initialization
        // ─────────────────────────────────────────────────────────────────────

        private void InitializeSdk()
        {
            if (_sdkInitialized) return;

#if ADMOB_ENABLED
            MobileAds.Initialize(status =>
            {
                _sdkInitialized = true;
                Debug.Log("[Ads] SDK initialized");

                // Preload ads on the main thread via a coroutine.
                // MobileAds.Initialize callback may run on a background thread.
                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    LoadInterstitial();
                    LoadRewarded();
                });
            });
#else
            _sdkInitialized = true;
            Debug.Log("[Ads] SDK initialized (ADMOB_ENABLED not defined — stub mode)");
            LoadInterstitial();
            LoadRewarded();
#endif
        }

        // ─────────────────────────────────────────────────────────────────────
        // Remove Ads
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Call this when a Remove Ads purchase is confirmed.
        /// Saves the state, hides the banner immediately, and prevents all
        /// future banner and interstitial ads.
        /// Rewarded ads are intentionally NOT affected.
        /// </summary>
        public void SetAdsRemoved(bool removed)
        {
            _adsRemoved = removed;
            PlayerPrefs.SetInt(AdConfig.AdsRemovedKey, removed ? 1 : 0);
            PlayerPrefs.Save();

            if (removed)
            {
                HideBanner();
                DestroyBanner();
                Debug.Log("[Ads] Ads removed — banner destroyed, interstitials disabled.");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // BANNER
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Shows the banner at the bottom of the screen.
        /// Creates/reuses one banner — never creates a duplicate.
        /// No-op if ads are removed.
        /// </summary>
        public void ShowBanner()
        {
            if (_adsRemoved)
            {
                Debug.Log("[Ads] ShowBanner skipped — ads removed.");
                return;
            }

            if (_bannerVisible) return; // already shown

#if ADMOB_ENABLED
            if (_bannerView == null)
            {
                CreateBanner();
            }
            _bannerView?.Show();
            _bannerVisible = true;
            Debug.Log("[Ads] Banner shown.");
#else
            _bannerVisible = true;
            Debug.Log("[Ads] Banner shown (stub — ADMOB_ENABLED not defined).");
#endif
        }

        /// <summary>
        /// Hides the banner without destroying it, so it can be re-shown quickly.
        /// </summary>
        public void HideBanner()
        {
            if (!_bannerVisible) return;
#if ADMOB_ENABLED
            _bannerView?.Hide();
#endif
            _bannerVisible = false;
            Debug.Log("[Ads] Banner hidden.");
        }

        private void CreateBanner()
        {
#if ADMOB_ENABLED
            _bannerView?.Destroy();
            _bannerView = new BannerView(AdConfig.BannerId, AdSize.GetCurrentOrientationAnchoredAdaptiveBannerAdSizeWithWidth(AdSize.FullWidth), AdPosition.Bottom);
            _bannerView.OnBannerAdLoaded     += () => Debug.Log("[Ads] Banner loaded.");
            _bannerView.OnBannerAdLoadFailed += error => Debug.LogWarning($"[Ads] Banner failed to load: {error}");
            _bannerView.LoadAd(new AdRequest());
#endif
        }

        private void DestroyBanner()
        {
#if ADMOB_ENABLED
            _bannerView?.Destroy();
            _bannerView = null;
#endif
            _bannerVisible = false;
        }

        // ═════════════════════════════════════════════════════════════════════
        // INTERSTITIAL
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>Preloads the next interstitial. Call after showing or on init.</summary>
        public void LoadInterstitial()
        {
            if (!_sdkInitialized) return;

#if ADMOB_ENABLED
            _interstitialAd?.Destroy();
            _interstitialAd = null;
            InterstitialAd.Load(AdConfig.InterstitialId, new AdRequest(), (ad, error) =>
            {
                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    if (error != null || ad == null)
                    {
                        Debug.LogWarning($"[Ads] Interstitial failed to load: {error?.GetMessage()}");
                        return;
                    }
                    _interstitialAd = ad;
                    Debug.Log("[Ads] Interstitial loaded.");
                });
            });
#else
            Debug.Log("[Ads] Interstitial loaded (stub).");
#endif
        }

        /// <summary>
        /// Checks the interstitial frequency rules and — if an ad should show —
        /// displays the interstitial then calls <paramref name="onClosed"/>.
        /// If no ad is available or rules say skip, calls <paramref name="onClosed"/> immediately.
        ///
        /// Called by UIManager.ShowWinPopup() right before showing the Win Panel.
        /// </summary>
        public void TryShowInterstitialThenWinPanel(int completedLevelNumber, Action onClosed)
        {
            if (_adsRemoved || !ShouldShowInterstitial(completedLevelNumber))
            {
                onClosed?.Invoke();
                return;
            }

            bool adReady = IsInterstitialReady();

            if (!adReady)
            {
                Debug.Log("[Ads] Interstitial not ready — proceeding to Win Panel immediately.");
                onClosed?.Invoke();
                return;
            }

            Debug.Log($"[Ads] Showing interstitial after level {completedLevelNumber}.");
            _interstitialOnClosed = onClosed;
            ShowInterstitial();
        }

        private bool ShouldShowInterstitial(int completedLevel)
        {
            if (completedLevel < AdConfig.InterstitialStartLevel) return false;
            int levelsAfterStart = completedLevel - AdConfig.InterstitialStartLevel;
            return levelsAfterStart % AdConfig.InterstitialEveryXLevels == 0;
        }

        private bool IsInterstitialReady()
        {
#if ADMOB_ENABLED
            return _interstitialAd != null && _interstitialAd.CanShowAd();
#else
            return false; // stub — no real interstitials without SDK
#endif
        }

        private void ShowInterstitial()
        {
            _isAdShowing = true;
            AudioManager.StopAll();

#if ADMOB_ENABLED
            _interstitialAd.OnAdFullScreenContentClosed += () =>
            {
                UnityMainThreadDispatcher.Enqueue(() => OnInterstitialClosed());
            };
            _interstitialAd.OnAdFullScreenContentFailed += error =>
            {
                Debug.LogWarning($"[Ads] Interstitial failed to show: {error.GetMessage()}");
                UnityMainThreadDispatcher.Enqueue(() => OnInterstitialClosed());
            };
            _interstitialAd.Show();
            Debug.Log("[Ads] Interstitial shown.");
#else
            // Stub — nothing to show; proceed immediately.
            Debug.Log("[Ads] Interstitial show (stub — no SDK).");
            OnInterstitialClosed();
#endif
        }

        private void OnInterstitialClosed()
        {
            Debug.Log("[Ads] Interstitial closed.");
            _isAdShowing = false;

#if ADMOB_ENABLED
            _interstitialAd?.Destroy();
            _interstitialAd = null;
#endif
            // Preload the next one.
            LoadInterstitial();

            // Fire the callback on the main thread.
            var cb = _interstitialOnClosed;
            _interstitialOnClosed = null;
            cb?.Invoke();
        }

        // ═════════════════════════════════════════════════════════════════════
        // REWARDED
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>Preloads the next rewarded ad. Call after showing or on init.</summary>
        public void LoadRewarded()
        {
            if (!_sdkInitialized) return;

#if ADMOB_ENABLED
            _rewardedAd?.Destroy();
            _rewardedAd = null;
            RewardedAd.Load(AdConfig.RewardedId, new AdRequest(), (ad, error) =>
            {
                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    if (error != null || ad == null)
                    {
                        Debug.LogWarning($"[Ads] Rewarded failed to load: {error?.GetMessage()}");
                        return;
                    }
                    _rewardedAd = ad;
                    Debug.Log("[Ads] Rewarded loaded.");
                });
            });
#else
            Debug.Log("[Ads] Rewarded loaded (stub).");
#endif
        }

        /// <summary>True while the rewarded ad is loaded and can show.</summary>
        public bool IsRewardedReady()
        {
#if ADMOB_ENABLED
            return _rewardedAd != null && _rewardedAd.CanShowAd();
#else
            return false;
#endif
        }

        /// <summary>
        /// Shows a rewarded ad for the given placement.
        /// <para><paramref name="onRewardGranted"/> is called ONLY if AdMob confirms the reward.</para>
        /// <para><paramref name="onFailed"/> is called if the ad fails, is not ready, or the user
        /// closes before earning — reward is never granted in those cases.</para>
        /// </summary>
        public void ShowRewardedAd(RewardType rewardType, Action onRewardGranted, Action onFailed = null)
        {
            if (_isAdShowing)
            {
                Debug.LogWarning("[Ads] ShowRewardedAd ignored — another ad is already showing.");
                onFailed?.Invoke();
                return;
            }

            if (!IsRewardedReady())
            {
                Debug.LogWarning($"[Ads] Rewarded ad not ready for {rewardType}. " +
                                 "Try again once loaded.");
                onFailed?.Invoke();
                return;
            }

            _pendingRewardType      = rewardType;
            _pendingOnRewardGranted = onRewardGranted;
            _pendingOnFailed        = onFailed;
            _rewardEarned           = false;
            _isAdShowing            = true;

            AudioManager.StopAll();

#if ADMOB_ENABLED
            _rewardedAd.OnAdFullScreenContentClosed += () =>
            {
                UnityMainThreadDispatcher.Enqueue(() => OnRewardedAdClosed());
            };
            _rewardedAd.OnAdFullScreenContentFailed += error =>
            {
                Debug.LogWarning($"[Ads] Rewarded failed to show: {error.GetMessage()}");
                UnityMainThreadDispatcher.Enqueue(() => OnRewardedAdClosed());
            };

            _rewardedAd.Show(reward =>
            {
                // This callback fires when AdMob confirms the reward was earned.
                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    _rewardEarned = true;
                    Debug.Log($"[Ads] Reward granted: {_pendingRewardType}");
                });
            });

            Debug.Log($"[Ads] Rewarded shown: {rewardType}");
#else
            // Stub mode — simulate the full ad flow in a coroutine.
            StartCoroutine(SimulateRewardedAd());
#endif
        }

#if !ADMOB_ENABLED
        private IEnumerator SimulateRewardedAd()
        {
            Debug.Log("[Ads] Rewarded simulation started (stub — ADMOB_ENABLED not defined).");
            yield return new WaitForSecondsRealtime(0.15f);
            // In stub mode always succeed so you can test reward flows in the editor.
            _rewardEarned = true;
            Debug.Log($"[Ads] Reward granted (stub): {_pendingRewardType}");
            OnRewardedAdClosed();
        }
#endif

        private void OnRewardedAdClosed()
        {
            Debug.Log("[Ads] Rewarded ad closed.");
            _isAdShowing = false;

#if ADMOB_ENABLED
            _rewardedAd?.Destroy();
            _rewardedAd = null;
#endif
            // Always reload immediately.
            LoadRewarded();

            // Grant reward only if AdMob confirmed it.
            if (_rewardEarned)
            {
                var cb = _pendingOnRewardGranted;
                ClearRewardedCallbacks();
                cb?.Invoke();
            }
            else
            {
                var cb = _pendingOnFailed;
                ClearRewardedCallbacks();
                cb?.Invoke();
            }
        }

        private void ClearRewardedCallbacks()
        {
            _pendingOnRewardGranted = null;
            _pendingOnFailed        = null;
            _rewardEarned           = false;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        // Note: MobileAds.Initialize may callback on a background thread.
        // We use a tiny main-thread dispatcher helper below.
        // If your project already has a dispatcher, delete this class.
        private static class UnityMainThreadDispatcher
        {
            private static readonly System.Collections.Generic.Queue<Action> _queue
                = new System.Collections.Generic.Queue<Action>();

            public static void Enqueue(Action action)
            {
                lock (_queue) _queue.Enqueue(action);
            }

            // Called every frame from AdManager.Update via the helper runner.
            public static void Flush()
            {
                while (true)
                {
                    Action a;
                    lock (_queue)
                    {
                        if (_queue.Count == 0) break;
                        a = _queue.Dequeue();
                    }
                    a.Invoke();
                }
            }
        }

        private void Update()
        {
            UnityMainThreadDispatcher.Flush();
        }
    }
}
