using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace NutBoltSort
{
    /// <summary>
    /// Core gameplay manager controlling input, selection routines, move validation, animations, and win conditions.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("Manager References")]
        [SerializeField] private LevelManager levelManager;
        [SerializeField] private UIManager uiManager;

        [Header("Bolt Interaction Animation Settings")]
        [SerializeField, Min(0f)] private float selectionLiftHeight = .72f;
        [SerializeField, Min(.01f)] private float selectionDuration = .15f;
        [SerializeField, Min(0f)] private float hoverAmount = .035f;
        [SerializeField, Min(0f)] private float hoverSpeed = 4f;
        [SerializeField, Min(0f)] private float moveArcHeight = .7f;
        [SerializeField, Min(.01f)] private float moveDuration = .20f;
        [SerializeField, Min(.01f)] private float landingDuration = .055f;
        [SerializeField, Min(0f)] private float nutLandingStagger = .02f;
        [SerializeField] private float screwRotationAmount = 42f;
        [SerializeField, Min(0f)] private float invalidShakeStrength = .16f;
        [SerializeField, Min(.01f)] private float invalidShakeDuration = .18f;
        [SerializeField, Min(0f)] private float completionBounceStrength = .12f;

        private BoltView selectedBolt;
        private List<NutView> selectedNuts = new List<NutView>();
        private Coroutine selectionRoutine;
        private Coroutine hoverRoutine;
        private Coroutine moveRoutine;

        private bool inputLocked;
        private bool won;
        private int currentLevelIndex;

        public bool IsWon => won;
        public int CurrentLevelIndex => currentLevelIndex;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;

            if (levelManager == null)
            {
                levelManager = GetComponent<LevelManager>() ?? FindObjectOfType<LevelManager>();
                if (levelManager == null)
                {
                    levelManager = gameObject.AddComponent<LevelManager>();
                }
            }

            if (uiManager == null)
            {
                uiManager = GetComponent<UIManager>() ?? FindObjectOfType<UIManager>();
                if (uiManager == null)
                {
                    uiManager = gameObject.AddComponent<UIManager>();
                }
            }
        }

        private void Start()
        {
            LoadCurrentLevel();
        }

        public void LoadCurrentLevel()
        {
            StopAllCoroutines();
            inputLocked = true;
            selectedBolt = null;
            selectedNuts.Clear();
            selectionRoutine = null;
            hoverRoutine = null;
            moveRoutine = null;
            won = false;

            if (levelManager != null)
            {
                if (levelManager.BuildLevel(currentLevelIndex, out var data))
                {
                    CheckWin();
                }
            }

            if (uiManager != null)
            {
                uiManager.UpdateLevelDisplay();
            }

            inputLocked = false;
        }

        public void RestartLevel()
        {
            inputLocked = false;
            LoadCurrentLevel();
        }

        public void LoadNextLevel()
        {
            inputLocked = false;
            if (levelManager != null && levelManager.TotalLevels > 0)
            {
                currentLevelIndex = (currentLevelIndex + 1) % levelManager.TotalLevels;
            }
            LoadCurrentLevel();
        }

        private void Update()
        {
            if (inputLocked || won) return;

            // Block 3D raycasting when pointer is over UI element or a popup is open
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (uiManager != null && uiManager.IsUIOpen) return;

            // Handle touch or mouse tap
            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = Camera.main != null ? Camera.main.ScreenPointToRay(Input.mousePosition) : default;
                if (Physics.Raycast(ray, out RaycastHit hit, 100f))
                {
                    var bolt = hit.collider.GetComponent<BoltView>() ?? hit.collider.GetComponentInParent<BoltView>();
                    if (bolt != null)
                    {
                        TapBolt(bolt);
                    }
                }
            }
        }

        public void TapBolt(BoltView tapped)
        {
            if (won || inputLocked || tapped == null) return;

            if (selectedBolt == null)
            {
                if (!tapped.IsLocked && tapped.Nuts.Count > 0)
                {
                    BeginSelection(tapped);
                }
                return;
            }

            if (selectedBolt == tapped)
            {
                BeginSelectionCancel();
                return;
            }

            if (CanMove(selectedBolt, tapped, selectedNuts))
            {
                BeginMove(tapped);
            }
            else
            {
                StartCoroutine(ShakeInvalidDestination(tapped));
            }
        }

        private void BeginSelection(BoltView source)
        {
            selectedBolt = source;
            selectedNuts = TopMatchingGroup(source);
            if (selectedNuts.Count == 0)
            {
                selectedBolt = null;
                return;
            }

            selectedBolt.SetSelectionEffect(true);
            inputLocked = true;
            selectionRoutine = StartCoroutine(LiftSelectedNuts());
        }

        private void BeginSelectionCancel()
        {
            if (selectionRoutine != null) StopCoroutine(selectionRoutine);
            StopHover();
            inputLocked = true;
            selectionRoutine = StartCoroutine(ReturnSelectedNuts());
        }

        private void BeginMove(BoltView destination)
        {
            if (selectionRoutine != null) StopCoroutine(selectionRoutine);
            StopHover();
            inputLocked = true;
            moveRoutine = StartCoroutine(MoveSelectedNuts(destination));
        }

        private List<NutView> TopMatchingGroup(BoltView bolt)
        {
            var group = new List<NutView>();
            if (bolt == null || bolt.Nuts.Count == 0) return group;

            NutColor color = bolt.Nuts[bolt.Nuts.Count - 1].Color;
            for (int i = bolt.Nuts.Count - 1; i >= 0 && bolt.Nuts[i].Color == color; i--)
            {
                group.Insert(0, bolt.Nuts[i]);
            }
            return group;
        }

        private bool CanMove(BoltView from, BoltView to, List<NutView> moving)
        {
            if (from == null || to == null || moving == null || moving.Count == 0 || to.IsLocked) return false;
            if (to.Nuts.Count + moving.Count > BoltView.Capacity) return false;
            return to.Nuts.Count == 0 || to.Nuts[to.Nuts.Count - 1].Color == moving[moving.Count - 1].Color;
        }

        private IEnumerator LiftSelectedNuts()
        {
            var starts = selectedNuts.Select(n => n.transform.position).ToArray();
            float time = 0f;
            while (time < selectionDuration)
            {
                time += Time.deltaTime;
                float t = EaseOutCubic(time / selectionDuration);
                SetSelectedHoverPose(t, 0f);
                for (int i = 0; i < selectedNuts.Count; i++)
                {
                    selectedNuts[i].transform.position = Vector3.Lerp(starts[i], selectedNuts[i].transform.position, t);
                }
                yield return null;
            }
            SetSelectedHoverPose(1f, 0f);
            selectionRoutine = null;
            hoverRoutine = StartCoroutine(HoverSelectedNuts());
            inputLocked = false;
        }

        private IEnumerator HoverSelectedNuts()
        {
            while (selectedBolt != null && selectedNuts.Count > 0)
            {
                float hover = Mathf.Sin(Time.time * hoverSpeed) * hoverAmount;
                SetSelectedHoverPose(1f, hover);
                yield return null;
            }
        }

        private void SetSelectedHoverPose(float liftProgress, float hover)
        {
            for (int i = 0; i < selectedNuts.Count; i++)
            {
                var nut = selectedNuts[i];
                int stackIndex = selectedBolt.Nuts.IndexOf(nut);
                if (stackIndex < 0) continue;

                nut.transform.position = selectedBolt.transform.TransformPoint(selectedBolt.GetStackPosition(stackIndex)) + Vector3.up * (selectionLiftHeight * liftProgress + hover);
                nut.transform.rotation = selectedBolt.transform.rotation * nut.RestingLocalRotation * Quaternion.Euler(0f, screwRotationAmount * liftProgress, 0f);
                nut.transform.localScale = nut.RestingLocalScale;
            }
        }

        private IEnumerator ReturnSelectedNuts()
        {
            var returningSource = selectedBolt;
            var starts = selectedNuts.Select(n => n.transform.position).ToArray();
            var startRotations = selectedNuts.Select(n => n.transform.rotation).ToArray();
            float time = 0f;
            while (time < selectionDuration)
            {
                time += Time.deltaTime;
                float t = EaseInOut(time / selectionDuration);
                for (int i = 0; i < selectedNuts.Count; i++)
                {
                    var nut = selectedNuts[i];
                    int stackIndex = returningSource.Nuts.IndexOf(nut);
                    nut.transform.position = Vector3.Lerp(starts[i], returningSource.transform.TransformPoint(returningSource.GetStackPosition(stackIndex)), t);
                    nut.transform.rotation = Quaternion.Slerp(startRotations[i], returningSource.transform.rotation * nut.RestingLocalRotation, t);
                }
                yield return null;
            }
            RestoreNutsToStack(returningSource, selectedNuts, returningSource.Nuts.Count - selectedNuts.Count);
            if (returningSource != null) returningSource.SetSelectionEffect(false);
            ClearSelection();
            inputLocked = false;
        }

        private IEnumerator MoveSelectedNuts(BoltView destination)
        {
            var source = selectedBolt;
            var moving = new List<NutView>(selectedNuts);
            var starts = moving.Select(n => n.transform.position).ToArray();
            var startRotations = moving.Select(n => n.transform.rotation).ToArray();
            int destinationStartIndex = destination.Nuts.Count;
            float time = 0f;

            if (source != null) source.SetSelectionEffect(false);

            while (time < moveDuration)
            {
                time += Time.deltaTime;
                float t = EaseInOut(time / moveDuration);
                for (int i = 0; i < moving.Count; i++)
                {
                    var nut = moving[i];
                    Vector3 aboveDestination = destination.transform.TransformPoint(destination.GetStackPosition(destinationStartIndex + i)) + Vector3.up * selectionLiftHeight;
                    nut.transform.position = Vector3.Lerp(starts[i], aboveDestination, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * moveArcHeight);
                    nut.transform.rotation = Quaternion.Slerp(startRotations[i], source.transform.rotation * nut.RestingLocalRotation * Quaternion.Euler(0f, screwRotationAmount * 1.35f, 0f), t);
                }
                yield return null;
            }

            source.Nuts.RemoveRange(source.Nuts.Count - moving.Count, moving.Count);
            foreach (var nut in moving)
            {
                nut.transform.SetParent(destination.NutContainer, true);
                destination.Nuts.Add(nut);
            }

            for (int i = 0; i < moving.Count; i++)
            {
                var nut = moving[i];
                Vector3 start = nut.transform.position;
                Quaternion startRotation = nut.transform.rotation;
                Vector3 end = destination.transform.TransformPoint(destination.GetStackPosition(destinationStartIndex + i));
                float landingTime = 0f;
                while (landingTime < landingDuration)
                {
                    landingTime += Time.deltaTime;
                    float t = EaseInOut(landingTime / landingDuration);
                    nut.transform.position = Vector3.Lerp(start, end, t);
                    nut.transform.rotation = Quaternion.Slerp(startRotation, destination.transform.rotation * nut.RestingLocalRotation, t);
                    yield return null;
                }
                RestoreNutToStack(destination, nut, destinationStartIndex + i);
                if (nutLandingStagger > 0f) yield return new WaitForSeconds(nutLandingStagger);
            }

            bool sourceCompleted = LockCompletedBolt(source);
            bool destinationCompleted = LockCompletedBolt(destination);
            CheckWin();
            if (sourceCompleted) yield return PlayCompletionFeedback(source);
            if (destinationCompleted && destination != source) yield return PlayCompletionFeedback(destination);
            ClearSelection();
            moveRoutine = null;

            if (won)
            {
                if (uiManager != null) uiManager.ShowWinPopup();
            }
            else
            {
                inputLocked = false;
            }
        }

        private IEnumerator ShakeInvalidDestination(BoltView bolt)
        {
            inputLocked = true;
            Vector3 home = bolt.transform.localPosition;
            float time = 0f;
            while (time < invalidShakeDuration)
            {
                time += Time.deltaTime;
                float fade = 1f - Mathf.Clamp01(time / invalidShakeDuration);
                bolt.transform.localPosition = home + Vector3.right * Mathf.Sin(time / invalidShakeDuration * Mathf.PI * 5f) * invalidShakeStrength * fade;
                yield return null;
            }
            bolt.transform.localPosition = home;
            inputLocked = false;
        }

        private bool LockCompletedBolt(BoltView bolt)
        {
            if (bolt == null || bolt.IsLocked || !bolt.IsComplete()) return false;
            bolt.LockBoltSilently();
            bolt.SetCompletionEffect(true);
            return true;
        }

        private IEnumerator PlayCompletionFeedback(BoltView bolt)
        {
            if (bolt == null) yield break;
            Vector3 basePosition = bolt.transform.localPosition;
            Vector3 baseScale = bolt.transform.localScale;
            const float duration = .12f;
            float time = 0f;
            while (time < duration)
            {
                time += Time.deltaTime;
                float t = time / duration;
                bolt.transform.localPosition = basePosition + Vector3.up * Mathf.Sin(t * Mathf.PI) * completionBounceStrength;
                bolt.transform.localScale = baseScale * (1f + Mathf.Sin(t * Mathf.PI) * .025f);
                yield return null;
            }
            bolt.transform.localPosition = basePosition;
            bolt.transform.localScale = baseScale;
        }

        private static float EaseOutCubic(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }
        private static float EaseInOut(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

        private void StopHover()
        {
            if (hoverRoutine != null) StopCoroutine(hoverRoutine);
            hoverRoutine = null;
        }

        private void RestoreNutsToStack(BoltView bolt, List<NutView> nuts, int startIndex)
        {
            for (int i = 0; i < nuts.Count; i++) RestoreNutToStack(bolt, nuts[i], startIndex + i);
        }

        private void RestoreNutToStack(BoltView bolt, NutView nut, int index)
        {
            var tr = nut.transform;
            if (tr.parent != bolt.NutContainer) tr.SetParent(bolt.NutContainer, false);
            tr.localPosition = bolt.GetStackPosition(index);
            tr.localRotation = nut.RestingLocalRotation;
            tr.localScale = nut.RestingLocalScale;
        }

        private void ClearSelection()
        {
            StopHover();
            if (selectedBolt != null) selectedBolt.SetSelectionEffect(false);
            selectedBolt = null;
            selectedNuts.Clear();
        }

        private void CheckWin()
        {
            if (won || levelManager == null) return;
            won = levelManager.ActiveBolts.All(b => b.Nuts.Count == 0 || (b.Nuts.Count == BoltView.Capacity && b.IsComplete()));
        }

        private void OnGUI()
        {
            if (uiManager != null && uiManager.enabled) return;

            var old = GUI.matrix;
            float scale = Screen.width / 1080f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            var style = new GUIStyle(GUI.skin.button) { fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

            if (GUI.Button(new Rect(50, 50, 190, 68), "↻  RESTART", style) && !inputLocked)
            {
                RestartLevel();
            }

            if (GUI.Button(new Rect(840, 50, 190, 68), "LEVEL " + (currentLevelIndex + 1), style) && !inputLocked)
            {
                LoadNextLevel();
            }

            if (won)
            {
                var s = new GUIStyle(GUI.skin.label) { fontSize = 66, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                GUI.Label(new Rect(0, Screen.height / scale * .43f, 1080, 100), "LEVEL COMPLETE!", s);
            }
            GUI.matrix = old;
        }
    }
}
