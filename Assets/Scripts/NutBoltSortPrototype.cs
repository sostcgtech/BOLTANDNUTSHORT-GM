using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Self-contained V1 playfield for the Nut & Bolt Sort prototype.</summary>
public sealed class NutBoltSortPrototype : MonoBehaviour
{
    const int Capacity = 4;
    const float NutStep = .48f;
    readonly Dictionary<NutColor, Color> colors = new Dictionary<NutColor, Color>
    {
        { NutColor.Red, new Color(.95f,.19f,.20f) }, { NutColor.Blue, new Color(.12f,.48f,.96f) },
        { NutColor.Green, new Color(.16f,.76f,.43f) }, { NutColor.Yellow, new Color(1f,.68f,.08f) }
    };
    readonly List<Bolt> bolts = new List<Bolt>();
    Bolt selected;
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
        if (!LoadAuthoredScene())
        {
            Debug.LogError("Nut & Bolt Sort: no authored bolt board found. Create the named Bolts group in the scene before playing.", this);
            enabled = false;
        }
    }

    bool LoadAuthoredScene()
    {
        boltsRoot = transform.Find("01_Bolts — Bottom (move bolt roots to arrange)");
        if (boltsRoot == null) return false;
        bolts.Clear(); won = false;
        foreach (Transform root in boltsRoot)
        {
            if (!root.name.StartsWith("Bolt ")) continue;
            var collider = root.GetComponent<BoxCollider>();
            if (collider == null) return false;
            var bolt = new Bolt { root = root.gameObject, collider = collider, home = root.position, id = bolts.Count };
            foreach (Transform child in root)
            {
                if (!child.name.EndsWith(" nut")) continue; // keep this named nut root; freely replace its contents.
                var word = child.name.Split(' ')[0];
                if (!Enum.TryParse(word, out NutColor color)) return false;
                bolt.nuts.Add(new Nut { color = color, root = child.gameObject });
            }
            bolt.nuts = bolt.nuts.OrderBy(n => n.root.transform.localPosition.y).ToList();
            bolts.Add(bolt);
        }
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
        bolts.Clear(); selected = null; won = false;
        boltsRoot = Group("01_Bolts — Bottom (move bolt roots to arrange)");
        metal ??= Mat(new Color(.30f, .36f, .46f), .78f);
        // Reference-style board: four working stacks across the rear; two clear maneuvering
        // bolts across the lower row. Keep these roots editable in the scene hierarchy.
        var locations = new[] {
            new Vector3(-3.3f,0,1.15f), new Vector3(-1.1f,0,1.15f),
            new Vector3(1.1f,0,1.15f), new Vector3(3.3f,0,1.15f),
            new Vector3(-1.15f,0,-1.65f), new Vector3(1.15f,0,-1.65f)
        };
        var data = Levels[index % Levels.Length];
        for (int i=0;i<data.Length;i++) bolts.Add(CreateBolt(i, locations[i], data[i]));
    }
    Bolt CreateBolt(int id, Vector3 pos, NutColor[] initial)
    {
        var root = new GameObject("Bolt " + (id+1) + " — replace visual children"); root.transform.SetParent(boltsRoot); root.transform.position = pos;
        var hit = root.AddComponent<BoxCollider>(); hit.center = new Vector3(0,1.25f,0); hit.size = new Vector3(1.35f,3.3f,1.35f);
        var click = root.AddComponent<BoltClick>(); click.owner = this;
        Primitive(PrimitiveType.Cylinder,"Bolt shaft",root.transform,new Vector3(0,1.2f,0),new Vector3(.26f,1.25f,.26f),metal);
        Primitive(PrimitiveType.Cylinder,"Bolt base",root.transform,new Vector3(0,.12f,0),new Vector3(.68f,.12f,.68f),metal);
        var b = new Bolt { root=root, collider=hit, home=pos, id=id };
        foreach(var c in initial) b.nuts.Add(CreateNut(root.transform,c,b.nuts.Count));
        return b;
    }
    Nut CreateNut(Transform parent, NutColor color, int height)
    {
        var n = new GameObject(color + " nut"); n.transform.SetParent(parent); n.transform.localPosition = new Vector3(0,.43f + height*NutStep,0);
        // Two cylinders create a satisfying chunky colored ring silhouette around the screw shaft.
        var body = Primitive(PrimitiveType.Cylinder,"Nut ring",n.transform,Vector3.zero,new Vector3(.62f,.19f,.62f),Mat(colors[color], .22f)); body.GetComponent<Collider>().enabled=false;
        var bevel = Primitive(PrimitiveType.Cylinder,"Nut bevel",n.transform,new Vector3(0,.13f,0),new Vector3(.51f,.07f,.51f),Mat(Color.Lerp(colors[color],Color.white,.17f),.15f)); bevel.GetComponent<Collider>().enabled=false;
        return new Nut { color=color, root=n };
    }
    void Tap(Bolt tapped)
    {
        if (won || tapped.locked) return;
        if (selected == null) { if(tapped.nuts.Count>0) Select(tapped); return; }
        if (selected == tapped) { Select(null); return; }
        if (TryMove(selected,tapped)) Select(null); else if(tapped.nuts.Count>0) Select(tapped);
    }
    void Select(Bolt b) { if(selected!=null) selected.root.transform.position=selected.home; selected=b; if(b!=null)b.root.transform.position=b.home+Vector3.up*.14f; }
    bool TryMove(Bolt from, Bolt to)
    {
        if(to.locked || from.nuts.Count==0 || to.nuts.Count>=Capacity) return false;
        NutColor c=from.nuts[from.nuts.Count-1].color;
        if(to.nuts.Count>0 && to.nuts[to.nuts.Count-1].color!=c) return false;
        int count=0; for(int i=from.nuts.Count-1;i>=0 && from.nuts[i].color==c;i--) count++;
        count=Math.Min(count,Capacity-to.nuts.Count); var moving=from.nuts.GetRange(from.nuts.Count-count,count); from.nuts.RemoveRange(from.nuts.Count-count,count);
        StartCoroutine(MoveNuts(moving,to)); CheckComplete(from); return true;
    }
    IEnumerator MoveNuts(List<Nut> nuts, Bolt destination)
    {
        var starts=nuts.Select(n=>n.root.transform.position).ToArray();
        for(int i=0;i<nuts.Count;i++) { nuts[i].root.transform.SetParent(destination.root.transform,true); destination.nuts.Add(nuts[i]); }
        float duration=.26f, time=0;
        var ends=nuts.Select((n,i)=>destination.root.transform.TransformPoint(new Vector3(0,.43f+(destination.nuts.Count-nuts.Count+i)*NutStep,0))).ToArray();
        while(time<duration){time+=Time.deltaTime;float t=Mathf.SmoothStep(0,1,time/duration);for(int i=0;i<nuts.Count;i++)nuts[i].root.transform.position=Vector3.Lerp(starts[i],ends[i],t)+Vector3.up*Mathf.Sin(t*Mathf.PI)*.38f;yield return null;}
        for(int i=0;i<nuts.Count;i++)nuts[i].root.transform.localPosition=new Vector3(0,.43f+(destination.nuts.Count-nuts.Count+i)*NutStep,0);
        CheckComplete(destination);
    }
    void CheckComplete(Bolt bolt)
    {
        if (!bolt.locked && bolt.nuts.Count == Capacity && bolt.nuts.All(n => n.color == bolt.nuts[0].color))
        {
            // A completed bolt stays on its board position with its nuts visible. It is simply
            // locked from future input and from receiving any more nuts.
            bolt.locked = true;
            bolt.collider.enabled = false;
        }
        CheckWin();
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
        if(GUI.Button(new Rect(50,50,190,68),"↻  RESTART",style)) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        if(GUI.Button(new Rect(840,50,190,68),"LEVEL "+(levelIndex+1),style)){levelIndex=(levelIndex+1)%Levels.Length;BuildLevel(levelIndex);}
        if(won){var s=new GUIStyle(GUI.skin.label){fontSize=66,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter,normal={textColor=Color.white}};GUI.Label(new Rect(0,Screen.height/scale*.43f,1080,100),"LEVEL COMPLETE!",s);}
        GUI.matrix=old;
    }
    sealed class Nut { public NutColor color; public GameObject root; }
    sealed class Bolt { public int id; public GameObject root; public BoxCollider collider; public Vector3 home; public List<Nut> nuts=new List<Nut>(); public bool locked; }
    sealed class BoltClick : MonoBehaviour { public NutBoltSortPrototype owner; void OnMouseUpAsButton(){owner.Tap(owner.bolts.FirstOrDefault(b=>b.root==gameObject));} }
}
