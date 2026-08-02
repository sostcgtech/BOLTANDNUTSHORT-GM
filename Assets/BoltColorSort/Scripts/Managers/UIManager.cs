using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DG.Tweening;

namespace NutBoltSort
{
    /// <summary>
    /// UIManager handles TopBar level display, Restart button, Win Popup, and UI Input Blocker.
    /// Uses TextMeshPro (TMP) for all text rendering.
    /// Supports procedural UI construction fallback if Inspector references are unassigned.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Header("Manager References")]
        [SerializeField] private GameManager gameManager;
        [SerializeField] private LevelManager levelManager;

        [Header("Canvas & SafeArea")]
        [SerializeField] private Canvas gameplayCanvas;
        [SerializeField] private SafeArea safeArea;
        [SerializeField] private CanvasGroup uiBlocker;

        [Header("Top Bar Components")]
        [SerializeField] private Button restartButton;
        [SerializeField] private TMP_Text levelDisplayTextTMP;

        [Header("Level Ribbon")]
        [SerializeField] private LevelRibbonController levelRibbon;

        [Header("Win Popup")]
        [SerializeField] private UIPopup winPopup;
        [SerializeField] private Button nextLevelWinButton;

        [Header("Gameplay Action Buttons")]
        [SerializeField] private Button undoButton;
        [SerializeField] private Button expandBoltButton;
        [SerializeField] private Button unlockBoltButton;

        [Header("Action Button Counters")]
        [SerializeField] private TMP_Text undoCounterText;
        [SerializeField] private TMP_Text expandCounterText;
        [SerializeField] private CanvasGroup undoButtonCanvasGroup;
        [SerializeField] private CanvasGroup expandButtonCanvasGroup;
        [SerializeField, Range(.55f, .7f)] private float disabledActionAlpha = .62f;

        [Header("Reward Refill States")]
        [Tooltip("Assign the background GameObject that already contains the Undo count text/icon.")]
        [SerializeField] private GameObject undoNormalState;
        [Tooltip("Assign your Watch Ad image GameObject. Its own artwork supplies the reward text.")]
        [SerializeField] private GameObject undoAdState;
        [Tooltip("Assign the background GameObject that already contains the Expand count text/icon.")]
        [SerializeField] private GameObject expandNormalState;
        [Tooltip("Assign your Watch Ad image GameObject. Its own artwork supplies the reward text.")]
        [SerializeField] private GameObject expandAdState;

        public bool IsUIOpen => (winPopup != null && winPopup.IsOpen) ||
                                (uiBlocker != null && uiBlocker.blocksRaycasts && uiBlocker.alpha > 0f);

        private void Awake()
        {
            FindManagers();
            EnsureUIHierarchy();
            EnsureActionButtonPresentation();
            EnsureRefillStates();
            BindButtonEvents();
        }

        private void Start()
        {
            if (winPopup != null && winPopup.gameObject.activeSelf) winPopup.gameObject.SetActive(false);
            SetUIBlockerActive(false);
            UpdateLevelDisplay();
            RefreshActionButtonStates();
        }

        private void FindManagers()
        {
            if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
            if (levelManager == null) levelManager = FindAnyObjectByType<LevelManager>();
        }

        public void UpdateLevelDisplay()
        {
            int levelNum = (gameManager != null) ? gameManager.CurrentLevelNumber : 1;
            string text  = $"LEVEL {levelNum}";

            if (levelDisplayTextTMP != null) levelDisplayTextTMP.text = text;
        }

        /// <summary>Shows the level notification without changing gameplay or input state.</summary>
        public void ShowLevelRibbon(bool isRestart = false)
        {
            if (levelRibbon == null || (isRestart && !levelRibbon.ShowRibbonOnRestart)) return;
            int levelNum = gameManager != null ? gameManager.CurrentLevelNumber : 1;
            levelRibbon.Show(levelNum);
        }

        public void OnRestartButtonPressed()
        {
            if (gameManager == null) return;
            CloseAllPopups();
            gameManager.RestartLevel();
            UpdateLevelDisplay();
            RefreshActionButtonStates();
        }

        public void OnUndoButtonPressed()
        {
            if (gameManager == null) return;
            if (gameManager.RemainingUndoUses > 0) gameManager.UndoLastMove();
            else gameManager.RequestUndoRewardAd();
        }

        public void OnExpandBoltButtonPressed()
        {
            if (gameManager == null) return;
            if (gameManager.RemainingExpandUses > 0) gameManager.ExpandFirstAvailableBolt();
            else gameManager.RequestExpandRewardAd();
        }

        public void OnUnlockBoltButtonPressed()
        {
            if (gameManager == null) return;
            gameManager.UnlockFirstLockedBolt();
        }

        /// <summary>Keeps both gameplay action buttons visible while synchronising their counters and availability.</summary>
        public void RefreshActionButtonStates()
        {
            RefreshUndoAndExpandUI();
        }

        /// <summary>Central refresh for use counters, normal/ad child states, and temporary ad request locking.</summary>
        public void RefreshUndoAndExpandUI(bool animateUndoReward = false, bool animateExpandReward = false)
        {
            EnsureActionButtonPresentation();
            EnsureRefillStates();

            int undoUses = Mathf.Max(0, gameManager != null ? gameManager.RemainingUndoUses : 0);
            int expandUses = Mathf.Max(0, gameManager != null ? gameManager.RemainingExpandUses : 0);

            if (undoCounterText != null) undoCounterText.text = undoUses.ToString();
            if (expandCounterText != null) expandCounterText.text = expandUses.ToString();

            SetRefillState(undoNormalState, undoAdState, undoUses > 0, animateUndoReward);
            SetRefillState(expandNormalState, expandAdState, expandUses > 0, animateExpandReward);

            // A zero-use button is still clickable because it is now a rewarded-ad button.
            SetActionButtonState(undoButton, undoButtonCanvasGroup, gameManager == null || !gameManager.IsUndoAdRequestActive);
            SetActionButtonState(expandBoltButton, expandButtonCanvasGroup, gameManager == null || !gameManager.IsExpandAdRequestActive);

            if (unlockBoltButton != null) unlockBoltButton.gameObject.SetActive(false);
        }

        public void ShowWinPopup()
        {
            if (winPopup == null) return;

            SetUIBlockerActive(true);
            winPopup.Open();
            RefreshActionButtonStates();
        }

        public void OnNextLevelWinPressed()
        {
            if (gameManager == null) return;

            if (winPopup != null)
            {
                winPopup.Close(() =>
                {
                    SetUIBlockerActive(false);
                    gameManager.LoadNextLevel();
                    UpdateLevelDisplay();
                    RefreshActionButtonStates();
                });
            }
            else
            {
                gameManager.LoadNextLevel();
                UpdateLevelDisplay();
            }
        }

        public void SetUIBlockerActive(bool active)
        {
            if (uiBlocker != null)
            {
                uiBlocker.blocksRaycasts = active;
                uiBlocker.alpha = active ? 1f : 0f;
            }
            RefreshActionButtonStates();
        }

        private void CloseAllPopups()
        {
            if (winPopup != null && winPopup.IsOpen) winPopup.Close();
            SetUIBlockerActive(false);
        }

        private void BindButtonEvents()
        {
            if (restartButton != null)
            {
                restartButton.onClick.RemoveAllListeners();
                restartButton.onClick.AddListener(OnRestartButtonPressed);
            }
            if (nextLevelWinButton != null)
            {
                nextLevelWinButton.onClick.RemoveAllListeners();
                nextLevelWinButton.onClick.AddListener(OnNextLevelWinPressed);
            }
            if (undoButton != null)
            {
                undoButton.onClick.RemoveAllListeners();
                undoButton.onClick.AddListener(OnUndoButtonPressed);
                undoButton.interactable = false; // starts disabled (no moves yet)
            }
            if (expandBoltButton != null)
            {
                expandBoltButton.onClick.RemoveAllListeners();
                expandBoltButton.onClick.AddListener(OnExpandBoltButtonPressed);
            }
            if (unlockBoltButton != null) unlockBoltButton.gameObject.SetActive(false);
        }

        private void EnsureActionButtonPresentation()
        {
            EnsureActionButtonPresentation(undoButton, ref undoCounterText, ref undoButtonCanvasGroup, "UndoCounter");
            EnsureActionButtonPresentation(expandBoltButton, ref expandCounterText, ref expandButtonCanvasGroup, "ExpandCounter");
        }

        private void EnsureActionButtonPresentation(Button button, ref TMP_Text counterText,
                                                    ref CanvasGroup canvasGroup, string counterName)
        {
            if (button == null) return;

            // Action buttons are deliberately never hidden for availability reasons.
            if (!button.gameObject.activeSelf) button.gameObject.SetActive(true);

            if (canvasGroup == null)
                canvasGroup = button.GetComponent<CanvasGroup>() ?? button.gameObject.AddComponent<CanvasGroup>();

            if (counterText == null)
                counterText = CreateCounterText(button.transform, counterName);
        }

        private void EnsureRefillStates()
        {
            EnsureRefillState(undoButton, ref undoNormalState, ref undoAdState, undoCounterText,
                "UndoNormalState", "UndoAdState");
            EnsureRefillState(expandBoltButton, ref expandNormalState, ref expandAdState, expandCounterText,
                "ExpandNormalState", "ExpandAdState");
        }

        private static void EnsureRefillState(Button button, ref GameObject normalState, ref GameObject adState,
                                              TMP_Text counterText,
                                              string normalName, string adName)
        {
            if (button == null) return;
            // The normal state should be the user's existing count background so its art and
            // text hide/show together. Never move the count text out of that object.
            if (normalState == null && counterText != null)
                normalState = counterText.transform.parent != button.transform
                    ? counterText.transform.parent.gameObject
                    : counterText.gameObject;
            if (normalState == null) normalState = CreateState(button.transform, normalName);
            if (adState == null) adState = CreateState(button.transform, adName);
        }

        private static GameObject CreateState(Transform buttonTransform, string stateName)
        {
            var state = new GameObject(stateName, typeof(RectTransform));
            state.transform.SetParent(buttonTransform, false);
            var rect = state.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return state;
        }

        private static void SetRefillState(GameObject normalState, GameObject adState, bool hasUses, bool animateReward)
        {
            if (normalState == null || adState == null) return;

            if (!animateReward || !hasUses)
            {
                normalState.SetActive(hasUses);
                adState.SetActive(!hasUses);
                return;
            }

            // Reward success: only child states animate; the button itself never moves or hides.
            normalState.SetActive(true);
            adState.SetActive(true);
            CanvasGroup normalGroup = normalState.GetComponent<CanvasGroup>() ?? normalState.AddComponent<CanvasGroup>();
            CanvasGroup adGroup = adState.GetComponent<CanvasGroup>() ?? adState.AddComponent<CanvasGroup>();
            normalState.transform.localScale = Vector3.one * .85f;
            normalGroup.alpha = 0f;
            adGroup.alpha = 1f;
            DOTween.Kill(normalState.transform);
            DOTween.Kill(adState.transform);
            Sequence sequence = DOTween.Sequence();
            sequence.Join(adGroup.DOFade(0f, .12f));
            sequence.Join(adState.transform.DOScale(.85f, .12f));
            sequence.AppendCallback(() => adState.SetActive(false));
            sequence.Append(normalGroup.DOFade(1f, .14f));
            sequence.Join(normalState.transform.DOScale(1f, .14f).SetEase(Ease.OutBack));
        }

        private static TMP_Text CreateCounterText(Transform buttonTransform, string counterName)
        {
            var counterGO = new GameObject(counterName, typeof(RectTransform));
            counterGO.transform.SetParent(buttonTransform, false);
            var rect = counterGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(.5f, 0f);
            rect.anchorMax = new Vector2(.5f, 0f);
            rect.pivot = new Vector2(.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 5f);
            rect.sizeDelta = new Vector2(70f, 34f);

            var text = counterGO.AddComponent<TextMeshProUGUI>();
            text.text = "0";
            text.fontSize = 24f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private void SetActionButtonState(Button button, CanvasGroup canvasGroup, bool interactable)
        {
            if (button == null) return;
            if (!button.gameObject.activeSelf) button.gameObject.SetActive(true);
            button.interactable = interactable;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = interactable ? 1f : disabledActionAlpha;
                canvasGroup.interactable = interactable;
                canvasGroup.blocksRaycasts = interactable;
            }
        }

        private void EnsureUIHierarchy()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<StandaloneInputModule>();
            }

            if (gameplayCanvas != null) return;

            var existingCanvas = GameObject.Find("GameplayCanvas") ?? GameObject.Find("Canvas");
            if (existingCanvas != null)
            {
                gameplayCanvas = existingCanvas.GetComponent<Canvas>();
                if (safeArea == null) safeArea = existingCanvas.GetComponentInChildren<SafeArea>();
                return;
            }

            // Procedural Canvas Builder Fallback when manual canvas is not set up
            BuildProceduralUI();
        }

        private void BuildProceduralUI()
        {
            var canvasGO = new GameObject("GameplayCanvas");
            gameplayCanvas = canvasGO.AddComponent<Canvas>();
            gameplayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            var safeAreaGO = new GameObject("SafeArea", typeof(RectTransform));
            safeAreaGO.transform.SetParent(canvasGO.transform, false);
            var safeRect = safeAreaGO.GetComponent<RectTransform>();
            safeRect.anchorMin = Vector2.zero;
            safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = Vector2.zero;
            safeRect.offsetMax = Vector2.zero;
            safeArea = safeAreaGO.AddComponent<SafeArea>();

            // TopBar
            var topBar = new GameObject("TopBar", typeof(RectTransform));
            topBar.transform.SetParent(safeAreaGO.transform, false);
            var topRect = topBar.GetComponent<RectTransform>();
            topRect.anchorMin = new Vector2(0f, 1f);
            topRect.anchorMax = new Vector2(1f, 1f);
            topRect.pivot = new Vector2(0.5f, 1f);
            topRect.anchoredPosition = new Vector2(0, -50);
            topRect.sizeDelta = new Vector2(0, 120);

            // Restart Button
            restartButton = CreateSimpleButton(topBar.transform, "RestartButton", "↻", new Vector2(60, -60), new Vector2(120, 100), new Color(0.2f, 0.4f, 0.8f));
            restartButton.onClick.AddListener(OnRestartButtonPressed);

            // Undo Button
            undoButton = CreateSimpleButton(topBar.transform, "UndoButton", "↩", new Vector2(200, -60), new Vector2(120, 100), new Color(0.4f, 0.3f, 0.6f));
            undoButton.onClick.AddListener(OnUndoButtonPressed);
            undoButton.interactable = false;

            // Expand Bolt Button (bottom-left)
            var bottomBar = new GameObject("BottomBar", typeof(RectTransform));
            bottomBar.transform.SetParent(safeAreaGO.transform, false);
            var bottomRect = bottomBar.GetComponent<RectTransform>();
            bottomRect.anchorMin = new Vector2(0f, 0f);
            bottomRect.anchorMax = new Vector2(1f, 0f);
            bottomRect.pivot     = new Vector2(0.5f, 0f);
            bottomRect.anchoredPosition = new Vector2(0, 30);
            bottomRect.sizeDelta = new Vector2(0, 110);

            expandBoltButton = CreateSimpleButton(bottomBar.transform, "ExpandBoltButton", "▲ EXPAND", new Vector2(55, 55), new Vector2(240, 90), new Color(0.15f, 0.55f, 0.80f));
            expandBoltButton.onClick.AddListener(OnExpandBoltButtonPressed);

            // Locked bolts were removed; no unlock control is created.

            // Level Display
            var displayGO = new GameObject("LevelDisplay", typeof(RectTransform));
            displayGO.transform.SetParent(topBar.transform, false);
            var displayRect = displayGO.GetComponent<RectTransform>();
            displayRect.anchorMin = new Vector2(0.5f, 0.5f);
            displayRect.anchorMax = new Vector2(0.5f, 0.5f);
            displayRect.anchoredPosition = Vector2.zero;
            displayRect.sizeDelta = new Vector2(500, 100);
            levelDisplayTextTMP = displayGO.AddComponent<TextMeshProUGUI>();
            levelDisplayTextTMP.text = "LEVEL 1";
            levelDisplayTextTMP.fontSize = 48;
            levelDisplayTextTMP.alignment = TextAlignmentOptions.Center;
            levelDisplayTextTMP.color = Color.white;
            levelDisplayTextTMP.fontStyle = FontStyles.Bold;

            // WinPopup
            winPopup = CreateProceduralWinPopup(safeAreaGO.transform, "WinPopup", out nextLevelWinButton);
            if (nextLevelWinButton != null) nextLevelWinButton.onClick.AddListener(OnNextLevelWinPressed);

            // UI Blocker
            var blockerGO = new GameObject("UIBlocker", typeof(RectTransform));
            blockerGO.transform.SetParent(safeAreaGO.transform, false);
            var blockerRect = blockerGO.GetComponent<RectTransform>();
            blockerRect.anchorMin = Vector2.zero;
            blockerRect.anchorMax = Vector2.one;
            blockerRect.offsetMin = Vector2.zero;
            blockerRect.offsetMax = Vector2.zero;
            var blockerImg = blockerGO.AddComponent<Image>();
            blockerImg.color = new Color(0, 0, 0, 0f);
            uiBlocker = blockerGO.AddComponent<CanvasGroup>();
            SetUIBlockerActive(false);
        }

        private Button CreateSimpleButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color bgColor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var txtGO = new GameObject("Text", typeof(RectTransform));
            txtGO.transform.SetParent(go.transform, false);
            var txtRect = txtGO.GetComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = Vector2.zero;
            txtRect.offsetMax = Vector2.zero;

            var tmp = txtGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 32;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.fontStyle = FontStyles.Bold;

            return btn;
        }

        private UIPopup CreateProceduralWinPopup(Transform parent, string name, out Button nextBtn)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var dimGO = new GameObject("DimBackground", typeof(RectTransform));
            dimGO.transform.SetParent(root.transform, false);
            var dimRect = dimGO.GetComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = Vector2.zero;
            dimRect.offsetMax = Vector2.zero;
            var dimImg = dimGO.AddComponent<Image>();
            dimImg.color = new Color(0, 0, 0, 0.55f);

            var panelGO = new GameObject("PopupPanel", typeof(RectTransform));
            panelGO.transform.SetParent(root.transform, false);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(800, 500);
            var panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0.12f, 0.16f, 0.24f);

            // CompleteTitle
            var titleGO = new GameObject("CompleteTitle", typeof(RectTransform));
            titleGO.transform.SetParent(panelGO.transform, false);
            var titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchoredPosition = new Vector2(0, 150);
            titleRect.sizeDelta = new Vector2(700, 80);
            var titleTxt = titleGO.AddComponent<TextMeshProUGUI>();
            titleTxt.text = "EXCELLENT!";
            titleTxt.fontSize = 54;
            titleTxt.alignment = TextAlignmentOptions.Center;
            titleTxt.color = new Color(1f, 0.84f, 0f);
            titleTxt.fontStyle = FontStyles.Bold;

            // LevelCompleteText
            var subTitleGO = new GameObject("LevelCompleteText", typeof(RectTransform));
            subTitleGO.transform.SetParent(panelGO.transform, false);
            var subRect = subTitleGO.GetComponent<RectTransform>();
            subRect.anchoredPosition = new Vector2(0, 60);
            subRect.sizeDelta = new Vector2(700, 60);
            var subTxt = subTitleGO.AddComponent<TextMeshProUGUI>();
            subTxt.text = "LEVEL COMPLETE!";
            subTxt.fontSize = 36;
            subTxt.alignment = TextAlignmentOptions.Center;
            subTxt.color = Color.white;
            subTxt.fontStyle = FontStyles.Bold;

            // NextLevelButton
            nextBtn = CreateSimpleButton(panelGO.transform, "NextLevelButton", "NEXT LEVEL", new Vector2(0, -100), new Vector2(400, 100), new Color(0.2f, 0.75f, 0.35f));
            var nRect = nextBtn.GetComponent<RectTransform>();
            nRect.anchorMin = new Vector2(0.5f, 0.5f);
            nRect.anchorMax = new Vector2(0.5f, 0.5f);
            nRect.pivot = new Vector2(0.5f, 0.5f);

            var popup = root.AddComponent<UIPopup>();
            root.SetActive(false);
            return popup;
        }
    }
}
