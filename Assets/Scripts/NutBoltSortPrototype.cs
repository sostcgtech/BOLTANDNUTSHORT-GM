using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

/// <summary>Playfield controller for Nut & Bolt Sort.</summary>
public sealed class NutBoltSortPrototype : MonoBehaviour
{
    const int Capacity = 4;
    const float NutStep = .48f;
    [Header("Bolt Grid Layout")]
    [SerializeField] BoltGridLayoutSettings boltGridLayout = new BoltGridLayoutSettings();
    [Header("Bolt Interaction Animation")]
    [SerializeField, Min(0f)] float selectionLiftHeight = .72f;
    [SerializeField, Min(.01f)] float selectionDuration = .15f;
    [SerializeField, Min(0f)] float hoverAmount = .035f;
    [SerializeField, Min(0f)] float hoverSpeed = 4f;
    [SerializeField, Min(0f)] float moveArcHeight = .7f;
    [SerializeField, Min(.01f)] float moveDuration = .20f;
    [SerializeField, Min(.01f)] float landingDuration = .055f;
    [SerializeField, Min(0f)] float nutLandingStagger = .02f;
    [SerializeField] float screwRotationAmount = 42f;
    [SerializeField, Min(0f)] float invalidShakeStrength = .16f;
    [SerializeField, Min(.01f)] float invalidShakeDuration = .18f;
    [SerializeField, Min(0f)] float completionBounceStrength = .12f;
    readonly Dictionary<NutColor, Color> colors = new Dictionary<NutColor, Color>
    {
        { NutColor.Red, new Color(.95f,.19f,.20f) }, { NutColor.Blue, new Color(.12f,.48f,.96f) },
        { NutColor.Green, new Color(.16f,.76f,.43f) }, { NutColor.Yellow, new Color(1f,.68f,.08f) }
    };
    readonly List<Bolt> bolts = new List<Bolt>();
    Bolt selected;
    List<Nut> selectedNuts = new List<Nut>();
    Coroutine selectionRoutine;
    Coroutine hoverRoutine;
    Coroutine moveRoutine;
    bool inputLocked;
    bool won;
    int levelIndex;
    Material metal;
    Transform boltsRoot;

    enum NutColor { Red, Blue, Green, Yellow }

    void Awake()
    {
        Application.targetFrameRate = 60;
        Screen.orientation = ScreenOrientation.Portrait;
        // The scene is completely authored by hand. Play mode only reads the placed bolts.
        inputLocked = true;
        if (!LoadAuthoredScene())
        {
            Debug.LogError("Nut & Bolt Sort: no bolt board found. Create the bolt-root group in the scene before playing.", this);
            enabled = false;
        }
        inputLocked = false;
    }

    bool LoadAuthoredScene()
    {
        boltsRoot = transform.Find("01_Bolts — Bottom (move bolt roots to arrange)");
        if (boltsRoot == null) return false;
        bolts.Clear(); won = false;
        foreach (Transform root in boltsRoot)
        {
            if (root == null || !root.gameObject.activeInHierarchy) continue;
            var collider = root.GetComponent<BoxCollider>();
            if (collider == null) continue;
            var bolt = new Bolt { root = root.gameObject, collider = collider, home = root.position, id = bolts.Count };
            foreach (Transform child in root)
            {
                if (!child.name.EndsWith(" nut")) continue; // keep this named nut root; freely replace its contents.
                var word = child.name.Split(' ')[0];
                if (!Enum.TryParse(word, out NutColor color)) return false;
                bolt.nuts.Add(new Nut
                {
                    color = color,
                    root = child.gameObject,
                    restingLocalRotation = child.localRotation,
                    restingLocalScale = child.localScale
                });
            }
            bolt.nuts = bolt.nuts.OrderBy(n => n.root.transform.localPosition.y).ToList();
            bolts.Add(bolt);
        }
        ApplyBoltGridLayout();
        return bolts.Count > 0;
    }
    Transform Group(string name)
    {
        var existing = transform.Find(name);
        if (existing != null) return existing;
        var group = new GameObject(name).transform;
        group.SetParent(transform);
        return group;
    }

    Material Mat(Color c, float metallic = 0f)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        m.color = c; m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", .63f); return m;
    }
    GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var o = GameObject.CreatePrimitive(type); o.name = name; o.transform.SetParent(parent); o.transform.localPosition = pos; o.transform.localScale = scale;
        o.GetComponent<Renderer>().sharedMaterial = mat; return o;
    }
    // bottom-to-top authored layouts: four mixed bolts plus two maneuvering bolts.
    static readonly NutColor[][][] Levels = {
        new[] { new[]{NutColor.Red,NutColor.Green,NutColor.Blue,NutColor.Yellow}, new[]{NutColor.Green,NutColor.Blue,NutColor.Yellow,NutColor.Red}, new[]{NutColor.Blue,NutColor.Yellow,NutColor.Red,NutColor.Green}, new[]{NutColor.Yellow,NutColor.Red,NutColor.Green,NutColor.Blue}, Array.Empty<NutColor>(), Array.Empty<NutColor>() },
        new[] { new[]{NutColor.Red,NutColor.Blue,NutColor.Green,NutColor.Yellow}, new[]{NutColor.Yellow,NutColor.Green,NutColor.Blue,NutColor.Red}, new[]{NutColor.Green,NutColor.Yellow,NutColor.Red,NutColor.Blue}, new[]{NutColor.Blue,NutColor.Red,NutColor.Yellow,NutColor.Green}, Array.Empty<NutColor>(), Array.Empty<NutColor>() },
        new[] { new[]{NutColor.Red,NutColor.Green,NutColor.Yellow,NutColor.Blue}, new[]{NutColor.Blue,NutColor.Yellow,NutColor.Green,NutColor.Red}, new[]{NutColor.Green,NutColor.Red,NutColor.Blue,NutColor.Yellow}, new[]{NutColor.Yellow,NutColor.Blue,NutColor.Red,NutColor.Green}, Array.Empty<NutColor>(), Array.Empty<NutColor>() }
    };
    void BuildLevel(int index)
    {
        StopAllCoroutines();
        foreach (var b in bolts) if (b != null) Destroy(b.root);
        bolts.Clear(); selected = null; selectedNuts.Clear(); selectionRoutine = null; hoverRoutine = null; moveRoutine = null; won = false;
        boltsRoot = Group("01_Bolts — Bottom (move bolt roots to arrange)");
        metal ??= Mat(new Color(.30f, .36f, .46f), .78f);
        var data = Levels[index % Levels.Length];
        for (int i=0;i<data.Length;i++) bolts.Add(CreateBolt(i, data[i]));
        ApplyBoltGridLayout();
    }
    Bolt CreateBolt(int id, NutColor[] initial)
    {
        var root = new GameObject("Bolt " + (id+1) + " — replace visual children"); root.transform.SetParent(boltsRoot); root.transform.localPosition = Vector3.zero;
        var hit = root.AddComponent<BoxCollider>(); hit.center = new Vector3(0,1.25f,0); hit.size = new Vector3(1.35f,3.3f,1.35f);
        var click = root.AddComponent<BoltClick>(); click.owner = this;
        Primitive(PrimitiveType.Cylinder,"Bolt shaft",root.transform,new Vector3(0,1.2f,0),new Vector3(.26f,1.25f,.26f),metal);
        Primitive(PrimitiveType.Cylinder,"Bolt base",root.transform,new Vector3(0,.12f,0),new Vector3(.68f,.12f,.68f),metal);
        var b = new Bolt { root=root, collider=hit, home=root.transform.position, id=id };
        foreach(var c in initial) b.nuts.Add(CreateNut(root.transform,c,b.nuts.Count));
        return b;
    }
    /// <summary>
    /// Repositions active bolt roots after a board is loaded or rebuilt. Call this method if a
    /// level system intentionally enables or disables bolt roots outside the built-in flows.
    /// </summary>
    public void RefreshBoltGridLayout() => ApplyBoltGridLayout();

    void ApplyBoltGridLayout()
    {
        var roots = new List<Transform>(bolts.Count);
        foreach (var bolt in bolts)
            if (bolt != null && bolt.root != null) roots.Add(bolt.root.transform);

        BoltGridLayout.Apply(roots, boltGridLayout);

        // Selection restores a bolt to this world-space home position. Refresh it after the
        // local-space layout has moved the root, without changing rotation, scale, or parenting.
        foreach (var bolt in bolts)
            if (bolt != null && bolt.root != null && bolt.root.activeInHierarchy)
                bolt.home = bolt.root.transform.position;
    }
    Nut CreateNut(Transform parent, NutColor color, int height)
    {
        var n = new GameObject(color + " nut"); n.transform.SetParent(parent); n.transform.localPosition = new Vector3(0,.43f + height*NutStep,0);
        // Two cylinders create a satisfying chunky colored ring silhouette around the screw shaft.
        var body = Primitive(PrimitiveType.Cylinder,"Nut ring",n.transform,Vector3.zero,new Vector3(.62f,.19f,.62f),Mat(colors[color], .22f)); body.GetComponent<Collider>().enabled=false;
        var bevel = Primitive(PrimitiveType.Cylinder,"Nut bevel",n.transform,new Vector3(0,.13f,0),new Vector3(.51f,.07f,.51f),Mat(Color.Lerp(colors[color],Color.white,.17f),.15f)); bevel.GetComponent<Collider>().enabled=false;
        return new Nut { color=color, root=n, restingLocalRotation=n.transform.localRotation, restingLocalScale=n.transform.localScale };
    }
    void Tap(Bolt tapped)
    {
        if (won || inputLocked || tapped == null) return;
        if (selected == null)
        {
            if (!tapped.locked && tapped.nuts.Count > 0) BeginSelection(tapped);
            return;
        }

        // A source always remains selected after an illegal destination tap. This avoids a
        // second bolt silently replacing the player's selected group.
        if (selected == tapped) { BeginSelectionCancel(); return; }
        if (CanMove(selected, tapped, selectedNuts)) BeginMove(tapped);
        else StartCoroutine(ShakeInvalidDestination(tapped));
    }

    void BeginSelection(Bolt source)
    {
        selected = source;
        selectedNuts = TopMatchingGroup(source);
        if (selectedNuts.Count == 0) { selected = null; return; }
        inputLocked = true;
        selectionRoutine = StartCoroutine(LiftSelectedNuts());
    }

    void BeginSelectionCancel()
    {
        if (selectionRoutine != null) StopCoroutine(selectionRoutine);
        StopHover();
        inputLocked = true;
        selectionRoutine = StartCoroutine(ReturnSelectedNuts());
    }

    void BeginMove(Bolt destination)
    {
        if (selectionRoutine != null) StopCoroutine(selectionRoutine);
        StopHover();
        inputLocked = true;
        moveRoutine = StartCoroutine(MoveSelectedNuts(destination));
    }

    List<Nut> TopMatchingGroup(Bolt bolt)
    {
        var group = new List<Nut>();
        if (bolt == null || bolt.nuts.Count == 0) return group;
        NutColor color = bolt.nuts[bolt.nuts.Count - 1].color;
        for (int i = bolt.nuts.Count - 1; i >= 0 && bolt.nuts[i].color == color; i--) group.Insert(0, bolt.nuts[i]);
        return group;
    }

    bool CanMove(Bolt from, Bolt to, List<Nut> moving)
    {
        if (from == null || to == null || moving == null || moving.Count == 0 || to.locked) return false;
        if (to.nuts.Count + moving.Count > Capacity) return false;
        return to.nuts.Count == 0 || to.nuts[to.nuts.Count - 1].color == moving[moving.Count - 1].color;
    }

    IEnumerator LiftSelectedNuts()
    {
        var starts = selectedNuts.Select(n => n.root.transform.position).ToArray();
        float time = 0f;
        while (time < selectionDuration)
        {
            time += Time.deltaTime;
            float t = EaseOutCubic(time / selectionDuration);
            SetSelectedHoverPose(t, 0f);
            for (int i = 0; i < selectedNuts.Count; i++)
                selectedNuts[i].root.transform.position = Vector3.Lerp(starts[i], selectedNuts[i].root.transform.position, t);
            yield return null;
        }
        SetSelectedHoverPose(1f, 0f);
        selectionRoutine = null;
        hoverRoutine = StartCoroutine(HoverSelectedNuts());
        inputLocked = false;
    }

    IEnumerator HoverSelectedNuts()
    {
        while (selected != null && selectedNuts.Count > 0)
        {
            float hover = Mathf.Sin(Time.time * hoverSpeed) * hoverAmount;
            SetSelectedHoverPose(1f, hover);
            yield return null;
        }
    }

    void SetSelectedHoverPose(float liftProgress, float hover)
    {
        for (int i = 0; i < selectedNuts.Count; i++)
        {
            var nut = selectedNuts[i];
            int stackIndex = selected.nuts.IndexOf(nut);
            if (stackIndex < 0) continue;
            var transform = nut.root.transform;
            transform.position = selected.root.transform.TransformPoint(StackPosition(stackIndex)) + Vector3.up * (selectionLiftHeight * liftProgress + hover);
            transform.rotation = selected.root.transform.rotation * nut.restingLocalRotation * Quaternion.Euler(0f, screwRotationAmount * liftProgress, 0f);
            transform.localScale = nut.restingLocalScale;
        }
    }

    IEnumerator ReturnSelectedNuts()
    {
        var returningSource = selected;
        var starts = selectedNuts.Select(n => n.root.transform.position).ToArray();
        var startRotations = selectedNuts.Select(n => n.root.transform.rotation).ToArray();
        float time = 0f;
        while (time < selectionDuration)
        {
            time += Time.deltaTime;
            float t = EaseInOut(time / selectionDuration);
            for (int i = 0; i < selectedNuts.Count; i++)
            {
                var nut = selectedNuts[i];
                int stackIndex = returningSource.nuts.IndexOf(nut);
                nut.root.transform.position = Vector3.Lerp(starts[i], returningSource.root.transform.TransformPoint(StackPosition(stackIndex)), t);
                nut.root.transform.rotation = Quaternion.Slerp(startRotations[i], returningSource.root.transform.rotation * nut.restingLocalRotation, t);
            }
            yield return null;
        }
        RestoreNutsToStack(returningSource, selectedNuts, returningSource.nuts.Count - selectedNuts.Count);
        ClearSelection();
        inputLocked = false;
    }

    IEnumerator MoveSelectedNuts(Bolt destination)
    {
        var source = selected;
        var moving = new List<Nut>(selectedNuts);
        var starts = moving.Select(n => n.root.transform.position).ToArray();
        var startRotations = moving.Select(n => n.root.transform.rotation).ToArray();
        int destinationStartIndex = destination.nuts.Count;
        float time = 0f;

        // The whole group follows one arc; individual offsets preserve its stack order.
        while (time < moveDuration)
        {
            time += Time.deltaTime;
            float t = EaseInOut(time / moveDuration);
            for (int i = 0; i < moving.Count; i++)
            {
                var nut = moving[i];
                Vector3 aboveDestination = destination.root.transform.TransformPoint(StackPosition(destinationStartIndex + i)) + Vector3.up * selectionLiftHeight;
                nut.root.transform.position = Vector3.Lerp(starts[i], aboveDestination, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * moveArcHeight);
                nut.root.transform.rotation = Quaternion.Slerp(startRotations[i], source.root.transform.rotation * nut.restingLocalRotation * Quaternion.Euler(0f, screwRotationAmount * 1.35f, 0f), t);
            }
            yield return null;
        }

        // Commit the unchanged gameplay move only after its travel animation has completed.
        source.nuts.RemoveRange(source.nuts.Count - moving.Count, moving.Count);
        foreach (var nut in moving)
        {
            nut.root.transform.SetParent(destination.root.transform, true);
            destination.nuts.Add(nut);
        }

        for (int i = 0; i < moving.Count; i++)
        {
            var nut = moving[i];
            Vector3 start = nut.root.transform.position;
            Quaternion startRotation = nut.root.transform.rotation;
            Vector3 end = destination.root.transform.TransformPoint(StackPosition(destinationStartIndex + i));
            float landingTime = 0f;
            while (landingTime < landingDuration)
            {
                landingTime += Time.deltaTime;
                float t = EaseInOut(landingTime / landingDuration);
                nut.root.transform.position = Vector3.Lerp(start, end, t);
                nut.root.transform.rotation = Quaternion.Slerp(startRotation, destination.root.transform.rotation * nut.restingLocalRotation, t);
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
        inputLocked = false;
    }

    IEnumerator ShakeInvalidDestination(Bolt bolt)
    {
        inputLocked = true;
        Vector3 home = bolt.root.transform.localPosition;
        float time = 0f;
        while (time < invalidShakeDuration)
        {
            time += Time.deltaTime;
            float fade = 1f - Mathf.Clamp01(time / invalidShakeDuration);
            bolt.root.transform.localPosition = home + Vector3.right * Mathf.Sin(time / invalidShakeDuration * Mathf.PI * 5f) * invalidShakeStrength * fade;
            yield return null;
        }
        bolt.root.transform.localPosition = home;
        inputLocked = false;
    }

    bool LockCompletedBolt(Bolt bolt)
    {
        if (bolt.locked || bolt.nuts.Count != Capacity || !bolt.nuts.All(n => n.color == bolt.nuts[0].color)) return false;
        bolt.locked = true;
        bolt.collider.enabled = false;
        return true;
    }

    IEnumerator PlayCompletionFeedback(Bolt bolt)
    {
        Vector3 basePosition = bolt.root.transform.localPosition;
        Vector3 baseScale = bolt.root.transform.localScale;
        const float duration = .12f;
        float time = 0f;
        while (time < duration)
        {
            time += Time.deltaTime;
            float t = time / duration;
            bolt.root.transform.localPosition = basePosition + Vector3.up * Mathf.Sin(t * Mathf.PI) * completionBounceStrength;
            bolt.root.transform.localScale = baseScale * (1f + Mathf.Sin(t * Mathf.PI) * .025f);
            yield return null;
        }
        bolt.root.transform.localPosition = basePosition;
        bolt.root.transform.localScale = baseScale;
    }

    static Vector3 StackPosition(int index) => new Vector3(0f, .43f + index * NutStep, 0f);
    static float EaseOutCubic(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }
    static float EaseInOut(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

    void StopHover()
    {
        if (hoverRoutine != null) StopCoroutine(hoverRoutine);
        hoverRoutine = null;
    }

    void RestoreNutsToStack(Bolt bolt, List<Nut> nuts, int startIndex)
    {
        for (int i = 0; i < nuts.Count; i++) RestoreNutToStack(bolt, nuts[i], startIndex + i);
    }

    void RestoreNutToStack(Bolt bolt, Nut nut, int index)
    {
        var transform = nut.root.transform;
        if (transform.parent != bolt.root.transform) transform.SetParent(bolt.root.transform, false);
        transform.localPosition = StackPosition(index);
        transform.localRotation = nut.restingLocalRotation;
        transform.localScale = nut.restingLocalScale;
    }

    void ClearSelection()
    {
        StopHover();
        selected = null;
        selectedNuts.Clear();
    }
    void CheckWin()
    {
        if (won) return;
        won = bolts.All(b => b.nuts.Count == 0 || (b.nuts.Count == Capacity && b.nuts.All(n => n.color == b.nuts[0].color)));
    }
    void OnGUI()
    {
        var old=GUI.matrix; float scale=Screen.width/1080f; GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(scale,scale,1));
        var style=new GUIStyle(GUI.skin.button){fontSize=32,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
        if(GUI.Button(new Rect(50,50,190,68),"↻  RESTART",style) && !inputLocked)
        {
            inputLocked = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
        if(GUI.Button(new Rect(840,50,190,68),"LEVEL "+(levelIndex+1),style) && !inputLocked)
        {
            inputLocked = true;
            levelIndex=(levelIndex+1)%Levels.Length;
            BuildLevel(levelIndex);
            inputLocked = false;
        }
        if(won){var s=new GUIStyle(GUI.skin.label){fontSize=66,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter,normal={textColor=Color.white}};GUI.Label(new Rect(0,Screen.height/scale*.43f,1080,100),"LEVEL COMPLETE!",s);}
        GUI.matrix=old;
    }
    sealed class Nut
    {
        public NutColor color;
        public GameObject root;
        public Quaternion restingLocalRotation;
        public Vector3 restingLocalScale;
    }
    sealed class Bolt { public int id; public GameObject root; public BoxCollider collider; public Vector3 home; public List<Nut> nuts=new List<Nut>(); public bool locked; }
    sealed class BoltClick : MonoBehaviour { public NutBoltSortPrototype owner; void OnMouseUpAsButton(){owner.Tap(owner.bolts.FirstOrDefault(b=>b.root==gameObject));} }
}

/// <summary>Inspector settings for the local-space staggered perspective bolt grid.</summary>
[Serializable]
public sealed class BoltGridLayoutSettings
{
    [Header("Perspective Grid")]
    [Min(1)] public int MaximumColumns = 5;
    [Min(0.01f)] public float HorizontalSpacing = 2.2f;
    [FormerlySerializedAs("VerticalSpacing"), Min(0.01f)] public float RowDepthSpacing = 2.8f;
    public float AlternateRowHorizontalOffset = 0f;
    public Vector3 GridCenterOffset = new Vector3(0f, 0f, -0.25f);
    public bool FillBackRowFirst = true;
    public float OptionalRowHeightOffset = 0f;
}

/// <summary>
/// Reusable local-space staggered perspective placement for ordered bolt root transforms.
/// </summary>
public static class BoltGridLayout
{
    public static void Apply(IList<Transform> orderedBoltRoots, BoltGridLayoutSettings settings)
    {
        if (orderedBoltRoots == null || settings == null) return;

        var activeRoots = new List<Transform>(orderedBoltRoots.Count);
        foreach (var root in orderedBoltRoots)
            if (root != null && root.gameObject.activeInHierarchy)
                activeRoots.Add(root);

        if (activeRoots.Count == 0) return;

        int maximumColumns = Mathf.Max(1, settings.MaximumColumns);
        int rowCount = activeRoots.Count <= maximumColumns && activeRoots.Count <= 4
            ? 1
            : Mathf.CeilToInt(activeRoots.Count / (float)maximumColumns);

        // Five through ten bolts intentionally use two balanced rows (3+2 through 5+5).
        // Larger boards use the fewest rows allowed by Maximum Columns, with no isolated tail.
        if (activeRoots.Count > 4) rowCount = Mathf.Max(2, rowCount);

        int baseBoltsPerRow = activeRoots.Count / rowCount;
        int rowsWithOneExtraBolt = activeRoots.Count % rowCount;
        float rawStaggerAverage = 0f;
        for (int row = 0; row < rowCount; row++)
            rawStaggerAverage += (row & 1) == 0 ? 0f : settings.AlternateRowHorizontalOffset;
        rawStaggerAverage /= rowCount;

        int nextRoot = 0;
        for (int fillRow = 0; fillRow < rowCount; fillRow++)
        {
            int boltsInRow = baseBoltsPerRow + (fillRow < rowsWithOneExtraBolt ? 1 : 0);
            int visualRow = settings.FillBackRowFirst ? fillRow : rowCount - 1 - fillRow;
            float rowWidth = (boltsInRow - 1) * settings.HorizontalSpacing;
            float rawStagger = (visualRow & 1) == 0 ? 0f : settings.AlternateRowHorizontalOffset;
            float rowX = settings.GridCenterOffset.x + rawStagger - rawStaggerAverage;
            float rowZ = settings.GridCenterOffset.z + (rowCount - 1) * settings.RowDepthSpacing * .5f - visualRow * settings.RowDepthSpacing;
            float rowY = settings.GridCenterOffset.y + (rowCount - 1) * settings.OptionalRowHeightOffset * .5f - visualRow * settings.OptionalRowHeightOffset;

            for (int column = 0; column < boltsInRow; column++)
            {
                // Set only local position. Rotation, scale, child transforms, and hierarchy remain untouched.
                activeRoots[nextRoot++].localPosition = new Vector3(
                    rowX - rowWidth * .5f + column * settings.HorizontalSpacing,
                    rowY,
                    rowZ);
            }
        }
    }
}
