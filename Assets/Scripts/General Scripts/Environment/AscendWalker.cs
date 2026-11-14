// ═════════════════════════════════════════════════════════════════════════════
//  AscendWalker.cs  -  a simple person who walks a loop of waypoints forever.
//  The body is built from code (no model needed): head, hair, torso, arms and legs
//  that swing while walking. Cheap: ~15 small meshes, no physics, no animator.
//  Made by ASCEND > Site > Build Launch Area, Roads & People (or add it yourself and
//  fill in the waypoints).
// ═════════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public class AscendWalker : MonoBehaviour
{
    public enum Outfit { StaffPolo, LabCoat, Engineer, Visitor }

    [Tooltip("The loop to walk (world positions). The person follows the ground under each point.")]
    public List<Vector3> waypoints = new List<Vector3>();
    public float speed = 1.3f;
    public float startAt = 0f;            // 0..1 along the loop
    public Outfit outfit = Outfit.StaffPolo;
    public Color skin = new Color(0.78f, 0.6f, 0.45f);
    public Color hair = new Color(0.08f, 0.06f, 0.05f);
    public Color shirt = new Color(0.1f, 0.18f, 0.35f);
    public Color pants = new Color(0.55f, 0.5f, 0.4f);
    [Range(0.9f, 1.1f)] public float heightScale = 1f;

    [Header("Real character (optional - used instead of the simple body)")]
    [Tooltip("A rigged character model, e.g. Assets/Imports/People/*.glb")]
    public GameObject model;
    public AnimationClip walkClip, idleClip, waveClip;
    [Tooltip("Walking speed (m/s) at which the walk animation plays at normal speed.")]
    public float animWalkSpeed = 1.35f;
    [Tooltip("Height of the person in meters.")]
    public float personHeight = 1.72f;
    [Tooltip("Chance to stop for a moment at a waypoint (look around or wave).")]
    [Range(0f, 1f)] public float pauseChance = 0.06f;

    static readonly List<AscendWalker> all = new List<AscendWalker>();
    void OnEnable() { all.Add(this); }
    void OnDisable() { all.Remove(this); }

    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    readonly float[] weights = new float[3];
    int state;                 // 0 walk, 1 idle, 2 wave
    float pauseUntil;

    int target;
    float phase;
    Transform body, armL, armR, legL, legR, shinL, shinR;
    static Material mat;
    static readonly int ColorId = Shader.PropertyToID("_BaseColor");

    void Start()
    {
        if (waypoints.Count < 2) { enabled = false; return; }
        if (model && walkClip) BuildModel(); else BuildBody();
        // solid, so the player can't walk through people (own layer so ground checks ignore it)
        gameObject.layer = 2;   // Ignore Raycast
        foreach (Transform t in GetComponentsInChildren<Transform>()) t.gameObject.layer = 2;
        var cap = gameObject.AddComponent<CapsuleCollider>();
        float h = personHeight * heightScale;
        cap.height = h; cap.radius = 0.28f; cap.center = new Vector3(0f, h * 0.5f, 0f);
        // start part-way around the loop
        float total = 0f; for (int i = 0; i < waypoints.Count; i++) total += Vector3.Distance(waypoints[i], waypoints[(i + 1) % waypoints.Count]);
        float d = Mathf.Repeat(startAt, 1f) * total;
        for (int i = 0; i < waypoints.Count; i++)
        {
            Vector3 a = waypoints[i], b = waypoints[(i + 1) % waypoints.Count];
            float seg = Vector3.Distance(a, b);
            if (d <= seg) { transform.position = Vector3.Lerp(a, b, seg > 0f ? d / seg : 0f); target = (i + 1) % waypoints.Count; break; }
            d -= seg;
        }
        transform.position = Ground(transform.position);
        phase = Random.value * 10f;
    }

    void Update()
    {
        if (waypoints.Count < 2) return;
        BlendAnimations();
        if (Time.time < pauseUntil) return;                       // standing still for a moment
        if (state != 0) SetState(0);
        Vector3 pos = transform.position, goal = waypoints[target];
        Vector3 to = goal - pos; to.y = 0f;
        if (to.magnitude < 0.6f)
        {
            target = (target + 1) % waypoints.Count;
            if (graph.IsValid() && Random.value < pauseChance)
            {
                SetState(waveClip && Random.value < 0.35f ? 2 : 1);
                pauseUntil = Time.time + (state == 2 ? Mathf.Max(1.5f, waveClip.length) : Random.Range(2f, 5f));
            }
            return;
        }
        Vector3 dir = to.normalized;

        // don't bump into other people or the player: step aside (keep right) and slow down
        Vector3 push = Vector3.zero; float slow = 1f;
        foreach (var o in all)
        {
            if (o == this || !o) continue;
            Vector3 d = pos - o.transform.position; d.y = 0f; float dist = d.magnitude;
            if (dist < 1.8f && dist > 0.01f)
            {
                push += d / dist * (1.8f - dist) / 1.8f;
                if (Vector3.Dot(dir, -d / dist) > 0.5f) { push += Vector3.Cross(Vector3.up, dir) * 0.6f; slow = Mathf.Min(slow, Mathf.Clamp01(dist / 1.8f) + 0.35f); }
            }
        }
        var pl = PlayerController.Instance;
        if (pl)
        {
            Vector3 d = pos - pl.transform.position; d.y = 0f; float dist = d.magnitude;
            if (dist < 2.2f && dist > 0.01f) { push += d / dist * (2.2f - dist) / 2.2f * 1.5f; if (Vector3.Dot(dir, -d / dist) > 0.4f) slow = Mathf.Min(slow, 0.4f); }
        }
        if (push.sqrMagnitude > 0f) dir = (dir + push * 1.6f).normalized;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 1f - Mathf.Exp(-Time.deltaTime * 4f));
        pos += transform.forward * speed * slow * Time.deltaTime;
        if (graph.IsValid()) mixer.GetInput(0).SetSpeed(speed * slow / Mathf.Max(0.1f, animWalkSpeed));
        transform.position = Ground(pos);

        if (graph.IsValid()) return;   // the real character animates itself

        // walking: arms and legs swing opposite each other, knees bend on the back swing
        phase += Time.deltaTime * speed * 4.2f;
        float s = Mathf.Sin(phase);
        if (legL) { legL.localRotation = Quaternion.Euler(s * 26f, 0, 0); legR.localRotation = Quaternion.Euler(-s * 26f, 0, 0); }
        if (shinL) { shinL.localRotation = Quaternion.Euler(Mathf.Max(0f, -s) * 35f, 0, 0); shinR.localRotation = Quaternion.Euler(Mathf.Max(0f, s) * 35f, 0, 0); }
        if (armL) { armL.localRotation = Quaternion.Euler(-s * 22f, 0, 4f); armR.localRotation = Quaternion.Euler(s * 22f, 0, -4f); }
        if (body) body.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Cos(phase)) * 0.03f, 0f);   // small bob per step
    }

    // ── real character: model + walk / idle / wave blended with Playables (no controller asset needed) ──
    void BuildModel()
    {
        var m = Instantiate(model, transform);
        m.name = "Model";
        m.transform.localPosition = Vector3.zero; m.transform.localRotation = Quaternion.identity;
        foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c);
        // scale to a real person's height
        var rs = m.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float h = Mathf.Max(0.1f, b.size.y);
            m.transform.localScale = Vector3.one * (personHeight * heightScale / h);
        }
        foreach (var r in rs) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false; }

        var anim = m.GetComponentInChildren<Animator>();
        if (!anim) anim = m.AddComponent<Animator>();
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;   // cheap when off screen
        graph = PlayableGraph.Create(name + " Walk");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "Animation", anim);
        mixer = AnimationMixerPlayable.Create(graph, 3);
        output.SetSourcePlayable(mixer);
        var clips = new[] { walkClip, idleClip ? idleClip : walkClip, waveClip ? waveClip : (idleClip ? idleClip : walkClip) };
        for (int i = 0; i < 3; i++)
        {
            var cp = AnimationClipPlayable.Create(graph, clips[i]);
            cp.SetApplyFootIK(false);
            graph.Connect(cp, 0, mixer, i);
        }
        weights[0] = 1f; mixer.SetInputWeight(0, 1f);
        mixer.GetInput(0).SetSpeed(speed / Mathf.Max(0.1f, animWalkSpeed));
        mixer.GetInput(0).SetTime(Random.value * walkClip.length);   // not everyone in step
        graph.Play();
    }

    void SetState(int s)
    {
        state = s;
        if (graph.IsValid() && s == 2) mixer.GetInput(2).SetTime(0);
    }

    void BlendAnimations()
    {
        if (!graph.IsValid()) return;
        float k = 1f - Mathf.Exp(-Time.deltaTime * 6f);
        float sum = 0f;
        for (int i = 0; i < 3; i++) { weights[i] = Mathf.Lerp(weights[i], i == state ? 1f : 0f, k); sum += weights[i]; }
        for (int i = 0; i < 3; i++) mixer.SetInputWeight(i, weights[i] / Mathf.Max(0.001f, sum));
    }

    void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }

    static Vector3 Ground(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 8f, ~(1 << 2), QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
        else
        {
            foreach (var t in Terrain.activeTerrains)
            {
                Vector3 tp = t.transform.position, sz = t.terrainData.size;
                if (p.x >= tp.x && p.x <= tp.x + sz.x && p.z >= tp.z && p.z <= tp.z + sz.z) { p.y = t.SampleHeight(p) + tp.y; break; }
            }
        }
        return p;
    }

    // ── body from simple shapes (no colliders) ──
    Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color c, string n)
    {
        var g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.name = n;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos; g.transform.localScale = scale;
        var r = g.GetComponent<MeshRenderer>();
        if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "Walker" }; if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f); }
        r.sharedMaterial = mat;
        var mpb = new MaterialPropertyBlock(); mpb.SetColor(ColorId, c); mpb.SetColor("_Color", c); r.SetPropertyBlock(mpb);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return g.transform;
    }

    Transform Pivot(Transform parent, Vector3 pos, string n)
    {
        var t = new GameObject(n).transform; t.SetParent(parent, false); t.localPosition = pos; return t;
    }

    void BuildBody()
    {
        body = Pivot(transform, Vector3.zero, "Body");
        body.localScale = Vector3.one * heightScale;
        Color shoe = new Color(0.08f, 0.08f, 0.09f);
        bool coat = outfit == Outfit.LabCoat;
        Color top = coat ? new Color(0.95f, 0.95f, 0.96f) : shirt;

        // legs (pivot at the hip, knee pivot halfway)
        legL = Pivot(body, new Vector3(-0.1f, 0.92f, 0f), "Leg L");
        legR = Pivot(body, new Vector3(0.1f, 0.92f, 0f), "Leg R");
        foreach (var (leg, side) in new[] { (legL, -1), (legR, 1) })
        {
            Part(PrimitiveType.Capsule, leg, new Vector3(0, -0.22f, 0), new Vector3(0.15f, 0.25f, 0.15f), pants, "Thigh");
            var knee = Pivot(leg, new Vector3(0, -0.46f, 0), "Knee");
            Part(PrimitiveType.Capsule, knee, new Vector3(0, -0.2f, 0), new Vector3(0.12f, 0.23f, 0.12f), pants, "Shin");
            Part(PrimitiveType.Cube, knee, new Vector3(0, -0.43f, 0.05f), new Vector3(0.11f, 0.08f, 0.26f), shoe, "Shoe");
            if (side < 0) shinL = knee; else shinR = knee;
        }

        // torso, belt line, collar
        Part(PrimitiveType.Capsule, body, new Vector3(0, 1.22f, 0), new Vector3(0.38f, 0.3f, 0.22f), top, "Torso");
        Part(PrimitiveType.Cube, body, new Vector3(0, 0.96f, 0), new Vector3(0.34f, 0.12f, 0.2f), pants, "Hips");
        if (coat) Part(PrimitiveType.Cube, body, new Vector3(0, 0.9f, 0), new Vector3(0.4f, 0.42f, 0.25f), top, "Coat");
        if (outfit == Outfit.Engineer) Part(PrimitiveType.Cube, body, new Vector3(0, 1.24f, 0.115f), new Vector3(0.3f, 0.36f, 0.02f), new Color(1f, 0.55f, 0.1f), "Vest");
        if (outfit == Outfit.StaffPolo) Part(PrimitiveType.Cube, body, new Vector3(0.08f, 1.36f, 0.11f), new Vector3(0.07f, 0.05f, 0.01f), new Color(1f, 0.78f, 0.3f), "Badge");

        // head + hair
        Part(PrimitiveType.Cylinder, body, new Vector3(0, 1.55f, 0), new Vector3(0.09f, 0.05f, 0.09f), skin, "Neck");
        Part(PrimitiveType.Sphere, body, new Vector3(0, 1.68f, 0), new Vector3(0.21f, 0.24f, 0.22f), skin, "Head");
        Part(PrimitiveType.Sphere, body, new Vector3(0, 1.74f, -0.015f), new Vector3(0.225f, 0.17f, 0.23f), hair, "Hair");
        if (outfit == Outfit.Engineer) Part(PrimitiveType.Sphere, body, new Vector3(0, 1.78f, 0), new Vector3(0.25f, 0.14f, 0.27f), new Color(1f, 0.85f, 0.15f), "Hard Hat");

        // arms (pivot at the shoulder)
        armL = Pivot(body, new Vector3(-0.23f, 1.43f, 0f), "Arm L");
        armR = Pivot(body, new Vector3(0.23f, 1.43f, 0f), "Arm R");
        foreach (var arm in new[] { armL, armR })
        {
            Part(PrimitiveType.Capsule, arm, new Vector3(0, -0.17f, 0), new Vector3(0.1f, 0.18f, 0.1f), top, "Upper Arm");
            Part(PrimitiveType.Capsule, arm, new Vector3(0, -0.43f, 0.02f), new Vector3(0.085f, 0.15f, 0.085f), coat ? top : skin, "Forearm");
            Part(PrimitiveType.Sphere, arm, new Vector3(0, -0.6f, 0.03f), new Vector3(0.08f, 0.09f, 0.08f), skin, "Hand");
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        for (int i = 0; i < waypoints.Count; i++) Gizmos.DrawLine(waypoints[i], waypoints[(i + 1) % waypoints.Count]);
    }
}
