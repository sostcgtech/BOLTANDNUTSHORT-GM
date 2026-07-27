using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace NutBoltSort
{
    /// <summary>
    /// UIManager handles TopBar level display, Restart button, Win Popup, and UI Input Blocker.
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
        [SerializeField] private Text levelDisplayTextUGUI;

        [Header("Win Popup")]
        [SerializeField] private UIPopup winPopup;
        [SerializeField] private Button nextLevelWinButton;

        public bool IsUIOpen => (winPopup != null && winPopup.IsOpen) ||
                                (uiBlocker != null && uiBlocker.blocksRaycasts && uiBlocker.alpha > 0f);

        private void Awake()
        {
            FindManagers();
            EnsureUIHierarchy();
            BindButtonEvents();
        }

        private void Start()
        {
            if (winPopup != null && winPopup.gameObject.activeSelf) winPopup.gameObject.SetActive(false);
            SetUIBlockerActive(false);
            UpdateLevelDisplay();
        }

        private void FindManagers()
        {
            if (gameManager == null) gameManager = FindObjectOfType<GameManager>();
            if (levelManager == null) levelManager = FindObjectOfType<LevelManager>();
        }

        public void UpdateLevelDisplay()
        {
            int levelNum = (gameManager != null) ? gameManager.CurrentLevelIndex + 1 : 1;
            string text = $"LEVEL {levelNum}";

            if (levelDisplayTextTMP != null) levelDisplayTextTMP.text = text;
            if (levelDisplayTextUGUI != null) levelDisplayTextUGUI.text = text;
        }

        public void OnRestartButtonPressed()
        {
            if (gameManager == null) return;
            CloseAllPopups();
            gameManager.RestartLevel();
            UpdateLevelDisplay();
        }

        public void ShowWinPopup()
        {
            if (winPopup == null) return;

            SetUIBlockerActive(true);
            winPopup.Open();
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
        }

        private void EnsureUIHierarchy()
        {
            if (FindObjectOfType<EventSystem>() == null)
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

            // Level Display
            var displayGO = new GameObject("LevelDisplay", typeof(RectTransform));
            displayGO.transform.SetParent(topBar.transform, false);
            var displayRect = displayGO.GetComponent<RectTransform>();
            displayRect.anchorMin = new Vector2(0.5f, 0.5f);
            displayRect.anchorMax = new Vector2(0.5f, 0.5f);
            displayRect.anchoredPosition = Vector2.zero;
            displayRect.sizeDelta = new Vector2(500, 100);
            levelDisplayTextUGUI = displayGO.AddComponent<Text>();
            levelDisplayTextUGUI.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            levelDisplayTextUGUI.fontSize = 48;
            levelDisplayTextUGUI.alignment = TextAnchor.MiddleCenter;
            levelDisplayTextUGUI.color = Color.white;
            levelDisplayTextUGUI.fontStyle = FontStyle.Bold;

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

            var txt = txtGO.AddComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.text = label;
            txt.fontSize = 32;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.fontStyle = FontStyle.Bold;

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
            var titleTxt = titleGO.AddComponent<Text>();
            titleTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleTxt.text = "EXCELLENT!";
            titleTxt.fontSize = 54;
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.color = new Color(1f, 0.84f, 0f);
            titleTxt.fontStyle = FontStyle.Bold;

            // LevelCompleteText
            var subTitleGO = new GameObject("LevelCompleteText", typeof(RectTransform));
            subTitleGO.transform.SetParent(panelGO.transform, false);
            var subRect = subTitleGO.GetComponent<RectTransform>();
            subRect.anchoredPosition = new Vector2(0, 60);
            subRect.sizeDelta = new Vector2(700, 60);
            var subTxt = subTitleGO.AddComponent<Text>();
            subTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            subTxt.text = "LEVEL COMPLETE!";
            subTxt.fontSize = 36;
            subTxt.alignment = TextAnchor.MiddleCenter;
            subTxt.color = Color.white;
            subTxt.fontStyle = FontStyle.Bold;

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
