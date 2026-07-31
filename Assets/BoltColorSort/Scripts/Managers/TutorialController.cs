using System.Linq;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace NutBoltSort
{
    // ─────────────────────────────────────────────────────────────────────────
    // TutorialStep — state machine states
    // ─────────────────────────────────────────────────────────────────────────

    public enum TutorialStep
    {
        Inactive,
        WaitingForSource,      // Level 1: hand on Bolt 0, waiting for player to select
        WaitingForDest,        // Level 1: hand on Bolt 1, waiting for destination tap
        WaitingForExpand,      // Level 3: hand on Expand button
        WaitingForUnlock,      // Level 5: hand on Unlock button
        Complete
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TutorialController
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Controls all tutorial overlays via a step-based state machine.
    /// Subscribes to GameManager events; never directly manipulates bolts or nuts.
    /// Uses TextMeshPro (TMP) for instruction text rendering.
    ///
    /// Tutorial hand and text are built procedurally inside a Screen-Space Overlay Canvas.
    /// </summary>
    public class TutorialController : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("References (auto-found / auto-created if null)")]
        [SerializeField] private GameManager gameManager;
        [SerializeField] private LevelManager levelManager;
        [SerializeField] private Camera mainCamera;

        [Header("Optional pre-built UI roots")]
        [SerializeField] private RectTransform tutorialHandRect;
        [SerializeField] private TMP_Text      instructionText;
        [SerializeField] private GameObject    tutorialPanel;

        [Header("Hand Animation")]
        [SerializeField, Min(0.01f)] private float handBobAmount    = 12f;
        [SerializeField, Min(0.01f)] private float handBobDuration  = 0.45f;
        [SerializeField, Range(0.8f, 1f)] private float handMinScale = 0.88f;

        // ── PlayerPrefs keys ───────────────────────────────────────────────────
        private const string KEY_TUTORIAL            = "TutorialCompleted";
        private const string KEY_EXPANDABLE_TUTORIAL = "ExpandableTutorialCompleted";
        private const string KEY_LOCKED_TUTORIAL     = "LockedTutorialCompleted";

        // ── Runtime state ──────────────────────────────────────────────────────
        private TutorialStep _step = TutorialStep.Inactive;
        private int          _allowedSourceBoltIndex = 0;
        private int          _allowedDestBoltIndex   = 1;
        private Sequence     _handSeq;
        private int          _pointedBoltIndex = -1;
        private bool         _handTransitioning;

        // Procedural UI references
        private Canvas       _tutCanvas;
        private GameObject   _handGO;
        private TMP_Text     _handText;

        public TutorialStep CurrentStep => _step;

        // ── Events (called from GameManager) ──────────────────────────────────

        /// <summary>Called by GameManager when a new level is loaded.</summary>
        public void OnLevelLoaded(int levelNumber, GameManager gm, LevelManager lm)
        {
            gameManager  = gm;
            levelManager = lm;
            StopTutorial();
        }

        /// <summary>Called after the existing board-entry animation has settled.</summary>
        public void OnBoardReady(int levelNumber)
        {
            if (levelNumber == 1 && PlayerPrefs.GetInt(KEY_TUTORIAL, 0) == 0)
            {
                StartLevel1Tutorial();
            }
            else if (levelNumber == 3 && PlayerPrefs.GetInt(KEY_EXPANDABLE_TUTORIAL, 0) == 0)
            {
                StartExpandableTutorial();
            }
        }

        /// <summary>Returns true if the given bolt tap is allowed in the current tutorial step.</summary>
        public bool AllowTap(BoltView tapped)
        {
            if (_step == TutorialStep.Inactive || _step == TutorialStep.Complete)
                return true;
            if (tapped == null || levelManager == null) return false;

            // Level 1 deliberately permits only the bolt currently indicated by
            // the hand. We filter input instead of disabling colliders, so normal
            // gameplay state is untouched and Restart remains safe.
            if (_step == TutorialStep.WaitingForSource) return GetBoltIndex(tapped) == _allowedSourceBoltIndex;
            if (_step == TutorialStep.WaitingForDest) return GetBoltIndex(tapped) == _allowedDestBoltIndex;
            return true;
        }

        /// <summary>Called by GameManager when a bolt is successfully selected (lifted).</summary>
        public void OnBoltSelected(BoltView bolt)
        {
            if (_step != TutorialStep.WaitingForSource || levelManager == null) return;
            int idx = GetBoltIndex(bolt);
            if (idx != 0 && idx != 1) return;

            // Source is the hand-indicated first bolt. It now becomes inactive
            // for tutorial input while the other bolt becomes the only target.
            _allowedDestBoltIndex = idx == 0 ? 1 : 0;
            _step = TutorialStep.WaitingForDest;
            ShowInstruction("Tap the matching bolt");
            TransitionHandToBolt(_allowedDestBoltIndex);
        }

        /// <summary>Called when the real gameplay transfer coroutine begins.</summary>
        public void OnTransferStarted()
        {
            if (_step != TutorialStep.WaitingForDest) return;
            _step = TutorialStep.Complete; // stale callbacks can no longer restart the hand.
            PlayerPrefs.SetInt(KEY_TUTORIAL, 1);
            PlayerPrefs.Save();
            HideLevel1Pointer();
        }

        /// <summary>Called by GameManager after a transfer animation fully completes.</summary>
        public void OnTransferCompleted(int srcIdx, int dstIdx, int nutCount)
        {
            if (_step == TutorialStep.WaitingForDest)
            {
                CompleteTutorial(KEY_TUTORIAL);
            }
        }

        /// <summary>Called when an expandable bolt is expanded at least once.</summary>
        public void OnExpandableBoltUsed()
        {
            if (_step == TutorialStep.WaitingForExpand)
                CompleteTutorial(KEY_EXPANDABLE_TUTORIAL);
        }

        /// <summary>Called when a locked bolt is unlocked.</summary>
        public void OnLockedBoltUnlocked()
        {
            if (_step == TutorialStep.WaitingForUnlock)
                CompleteTutorial(KEY_LOCKED_TUTORIAL);
        }

        // ── Private Tutorial Starters ──────────────────────────────────────────

        private void StartLevel1Tutorial()
        {
            EnsureTutorialUI();
            if (tutorialHandRect != null) tutorialHandRect.localScale = Vector3.one;
            if (_handText != null) _handText.alpha = 1f;
            _allowedSourceBoltIndex = 0;
            _allowedDestBoltIndex   = 1;
            _step = TutorialStep.WaitingForSource;
            SetPanelActive(true);
            ShowInstruction("Tap the nut");
            PointHandToBolt(0);
        }

        private void StartExpandableTutorial()
        {
            EnsureTutorialUI();
            _step = TutorialStep.WaitingForExpand;
            SetPanelActive(true);
            ShowInstruction("Tap EXPAND to create more space");
            // Point hand toward the Expand button area (top of screen).
            PositionHandAtScreenPoint(new Vector2(Screen.width * 0.5f, Screen.height * 0.88f));
            StartHandAnimation();
        }

        private void StartLockedTutorial()
        {
            EnsureTutorialUI();
            _step = TutorialStep.WaitingForUnlock;
            SetPanelActive(true);
            ShowInstruction("Unlock an extra bolt when you need more space");
            PositionHandAtScreenPoint(new Vector2(Screen.width * 0.5f, Screen.height * 0.14f));
            StartHandAnimation();
        }

        private void CompleteTutorial(string prefsKey)
        {
            PlayerPrefs.SetInt(prefsKey, 1);
            PlayerPrefs.Save();
            _step = TutorialStep.Complete;
            StopTutorial();
        }

        private void StopTutorial()
        {
            _handSeq?.Kill(false);
            _handSeq = null;
            _pointedBoltIndex = -1;
            _handTransitioning = false;
            SetPanelActive(false);
            if (_handGO != null) _handGO.SetActive(false);
        }

        // ── UI Helpers ─────────────────────────────────────────────────────────

        private void ShowInstruction(string text)
        {
            if (instructionText != null) instructionText.text = text;
            if (_handText       != null) _handText.text       = "▼";
        }

        private void PointHandToBolt(int boltIndex)
        {
            if (levelManager == null || levelManager.ActiveBolts.Count <= boltIndex) return;
            BoltView target = levelManager.ActiveBolts[boltIndex];
            if (target == null) return;

            Camera cam = mainCamera != null ? mainCamera : Camera.main;
            if (cam == null) return;

            // Project bolt world position to screen space.
            _pointedBoltIndex = boltIndex;
            _handTransitioning = false;
            Vector3 screen = cam.WorldToScreenPoint(target.GetTutorialPointerWorldPosition());
            PositionHandAtScreenPoint(new Vector2(screen.x, screen.y));
            StartHandAnimation();
        }

        private void TransitionHandToBolt(int boltIndex)
        {
            if (levelManager == null || levelManager.ActiveBolts.Count <= boltIndex) return;
            BoltView target = levelManager.ActiveBolts[boltIndex];
            Camera cam = mainCamera != null ? mainCamera : Camera.main;
            if (target == null || cam == null) return;
            EnsureTutorialUI();
            if (_handGO == null || tutorialHandRect == null || _tutCanvas == null) return;
            _handSeq?.Kill(false);
            _handSeq = null;
            _pointedBoltIndex = boltIndex;
            _handTransitioning = true;
            _handGO.SetActive(true);
            Vector3 screen = cam.WorldToScreenPoint(target.GetTutorialPointerWorldPosition());
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_tutCanvas.GetComponent<RectTransform>(), screen, _tutCanvas.worldCamera, out Vector2 destination);
            _handSeq = DOTween.Sequence().SetTarget(tutorialHandRect);
            _handSeq.Append(tutorialHandRect.DOScale(.82f, .12f).SetEase(Ease.InCubic));
            _handSeq.Append(tutorialHandRect.DOAnchorPos(destination, .24f).SetEase(Ease.OutCubic));
            _handSeq.Append(tutorialHandRect.DOScale(1f, .12f).SetEase(Ease.OutBack));
            _handSeq.OnComplete(() => { _handTransitioning = false; StartHandAnimation(); });
        }

        private void HideLevel1Pointer()
        {
            _handSeq?.Kill(false);
            _handSeq = null;
            _pointedBoltIndex = -1;
            _handTransitioning = false;
            if (_handGO == null || tutorialHandRect == null) { SetPanelActive(false); return; }
            _handSeq = DOTween.Sequence().SetTarget(tutorialHandRect);
            _handSeq.Join(tutorialHandRect.DOScale(.8f, .24f).SetEase(Ease.InCubic));
            if (_handText != null) _handSeq.Join(_handText.DOFade(0f, .24f));
            _handSeq.AppendCallback(() => { if (_handGO != null) _handGO.SetActive(false); SetPanelActive(false); });
        }

        private void PositionHandAtScreenPoint(Vector2 screenPos)
        {
            EnsureTutorialUI();
            if (_handGO == null) return;
            _handGO.SetActive(true);
            if (_handText != null) _handText.alpha = 1f;

            // Convert screen point to canvas local position
            if (_tutCanvas != null && tutorialHandRect != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _tutCanvas.GetComponent<RectTransform>(), screenPos,
                    _tutCanvas.worldCamera, out Vector2 localPoint);
                tutorialHandRect.anchoredPosition = localPoint;
            }
        }

        private void StartHandAnimation()
        {
            _handSeq?.Kill(false);
            if (tutorialHandRect == null) return;

            _handSeq = DOTween.Sequence().SetLoops(-1);
            _handSeq.Append(tutorialHandRect.DOAnchorPosY(tutorialHandRect.anchoredPosition.y - handBobAmount, handBobDuration).SetEase(Ease.InOutSine));
            _handSeq.Join(tutorialHandRect.DOScale(handMinScale, handBobDuration).SetEase(Ease.InOutSine));
            _handSeq.Append(tutorialHandRect.DOAnchorPosY(tutorialHandRect.anchoredPosition.y, handBobDuration).SetEase(Ease.InOutSine));
            _handSeq.Join(tutorialHandRect.DOScale(1f, handBobDuration).SetEase(Ease.InOutSine));
        }

        private void SetPanelActive(bool active)
        {
            if (tutorialPanel != null) tutorialPanel.SetActive(active);
        }

        private int GetBoltIndex(BoltView bolt)
        {
            if (levelManager == null || levelManager.ActiveBolts == null) return -1;
            for (int i = 0; i < levelManager.ActiveBolts.Count; i++)
            {
                if (levelManager.ActiveBolts[i] == bolt) return i;
            }
            return -1;
        }

        // ── Procedural UI Builder ──────────────────────────────────────────────

        private void EnsureTutorialUI()
        {
            // Support an Inspector-assigned hand as well as the procedural hand.
            // An assigned RectTransform does not automatically populate our cached
            // Canvas/GameObject references, which previously caused the null error.
            if (tutorialHandRect != null)
            {
                _handGO = tutorialHandRect.gameObject;
                if (_handText == null) _handText = tutorialHandRect.GetComponent<TMP_Text>();
                var handGraphic = tutorialHandRect.GetComponent<Graphic>();
                if (handGraphic != null) handGraphic.raycastTarget = false;
                if (_tutCanvas == null) _tutCanvas = tutorialHandRect.GetComponentInParent<Canvas>();
                if (_tutCanvas != null) return;

                // Invalid prebuilt hand (not under a Canvas): fall back to a
                // complete procedural overlay instead of leaving a null reference.
                tutorialHandRect = null;
                _handGO = null;
                _handText = null;
            }

            // Create a dedicated overlay canvas for the tutorial
            var canvasGO = new GameObject("TutorialCanvas");
            _tutCanvas = canvasGO.AddComponent<Canvas>();
            _tutCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _tutCanvas.sortingOrder = 10; // above game canvas
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            (canvasGO.GetComponent<CanvasScaler>()).referenceResolution = new Vector2(1080, 1920);
            canvasGO.AddComponent<GraphicRaycaster>();

            // Tutorial panel (instruction text background)
            var panelGO = new GameObject("TutorialPanel", typeof(RectTransform));
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0.78f);
            panelRect.anchorMax = new Vector2(1f, 0.92f);
            panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
            var panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.65f);
            panelImg.raycastTarget = false;
            tutorialPanel = panelGO;

            // Instruction text
            var txtGO = new GameObject("InstructionText", typeof(RectTransform));
            txtGO.transform.SetParent(panelGO.transform, false);
            var txtRect = txtGO.GetComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = txtRect.offsetMax = Vector2.zero;
            
            var tmpText = txtGO.AddComponent<TextMeshProUGUI>();
            tmpText.fontSize  = 52;
            tmpText.alignment = TextAlignmentOptions.Center;
            tmpText.color     = Color.white;
            tmpText.fontStyle = FontStyles.Bold;
            tmpText.raycastTarget = false;
            instructionText   = tmpText;

            // Hand indicator
            _handGO = new GameObject("TutorialHand", typeof(RectTransform));
            _handGO.transform.SetParent(canvasGO.transform, false);
            tutorialHandRect = _handGO.GetComponent<RectTransform>();
            tutorialHandRect.sizeDelta = new Vector2(80f, 80f);

            var tmpHand = _handGO.AddComponent<TextMeshProUGUI>();
            tmpHand.fontSize  = 72;
            tmpHand.alignment = TextAlignmentOptions.Center;
            tmpHand.color     = new Color(1f, 0.92f, 0.2f);
            tmpHand.text      = "▼";
            // The hand is visual guidance only. It must never consume the tap
            // intended for the nut/bolt below it.
            tmpHand.raycastTarget = false;
            _handText         = tmpHand;

            panelGO.SetActive(false);
            _handGO.SetActive(false);
        }

        private void OnDestroy()
        {
            _handSeq?.Kill(false);
        }

        private void LateUpdate()
        {
            // Keep the hand aligned when the layout/camera changes without using hard-coded UI coordinates.
            if (!_handTransitioning && _pointedBoltIndex >= 0 && _handGO != null && _handGO.activeSelf && levelManager != null)
            {
                if (levelManager.ActiveBolts.Count <= _pointedBoltIndex) return;
                BoltView bolt = levelManager.ActiveBolts[_pointedBoltIndex];
                Camera cam = mainCamera != null ? mainCamera : Camera.main;
                if (bolt != null && cam != null)
                {
                    Vector3 screen = cam.WorldToScreenPoint(bolt.GetTutorialPointerWorldPosition());
                    PositionHandAtScreenPoint(new Vector2(screen.x, screen.y));
                }
            }
        }
    }
}
