// ═════════════════════════════════════════════════════════════════════════════
//  PlayerBody.cs  -  your own body in first person: look down and you see your
//  legs, body and arms. Idle / walk / run animations follow how fast you move.
//  Male or Female comes from Settings (GameSettings.PlayerCharacter).
//  Put it on the Player (next to PlayerController). The head is hidden so it
//  never blocks the camera.
// ═════════════════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[RequireComponent(typeof(PlayerController))]
public class PlayerBody : MonoBehaviour
{
    [System.Serializable]
    public class Look
    {
        public GameObject model;
        public AnimationClip idle, walk, run;
        [Tooltip("Height of this person in meters.")] public float height = 1.75f;
        [Tooltip("Speed (m/s) where the walk / run animation plays at normal speed.")] public float walkAnimSpeed = 1.35f, runAnimSpeed = 4.2f;
    }

    public Look male = new Look(), female = new Look { height = 1.65f };
    [Tooltip("Body sits this far behind the camera, so looking down shows your chest and feet (not the inside of your neck).")]
    public float backOffset = 0.27f;

    PlayerController pc;
    CharacterController cc;
    GameObject body;
    Look current;
    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    readonly float[] w = { 1f, 0f, 0f };
    Renderer[] renderers;
    bool shown = true;

    void Awake() { pc = GetComponent<PlayerController>(); cc = GetComponent<CharacterController>(); }
    void OnEnable() { GameSettings.Changed += Rebuild; }
    void OnDisable() { GameSettings.Changed -= Rebuild; }
    void Start() { Rebuild(); }
    void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }

    void Rebuild()
    {
        var want = GameSettings.PlayerCharacter == GameSettings.Character.Female ? female : male;
        if (want == current && body) return;
        if (graph.IsValid()) graph.Destroy();
        if (body) Destroy(body);
        current = want;
        if (!want.model || !want.walk) return;

        body = Instantiate(want.model, transform);
        body.name = "Player Body";
        foreach (var c in body.GetComponentsInChildren<Collider>()) Destroy(c);
        renderers = body.GetComponentsInChildren<Renderer>();
        Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds();
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        body.transform.localScale = Vector3.one * (want.height / Mathf.Max(0.1f, b.size.y));
        // the head (and hair) is invisible to you but still casts its shadow, so your shadow looks complete
        foreach (var r in renderers)
        {
            string n = r.name.ToLowerInvariant();
            r.shadowCastingMode = n.Contains("head") || n.Contains("hair") ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
        }

        var anim = body.GetComponentInChildren<Animator>();
        if (!anim) anim = body.AddComponent<Animator>();
        anim.applyRootMotion = false;
        graph = PlayableGraph.Create("Player Body");
        var output = AnimationPlayableOutput.Create(graph, "Body", anim);
        mixer = AnimationMixerPlayable.Create(graph, 3);
        output.SetSourcePlayable(mixer);
        var clips = new[] { want.idle ? want.idle : want.walk, want.walk, want.run ? want.run : want.walk };
        for (int i = 0; i < 3; i++) { var cp = AnimationClipPlayable.Create(graph, clips[i]); graph.Connect(cp, 0, mixer, i); }
        graph.Play();
        Place();
    }

    // Feet on the floor, a little behind the eyes
    void Place()
    {
        if (!body) return;
        float feet = cc ? cc.center.y - cc.height * 0.5f : 0f;
        body.transform.localPosition = new Vector3(0f, feet, -backOffset);
        body.transform.localRotation = Quaternion.identity;
    }

    void LateUpdate()
    {
        if (!body || !graph.IsValid()) return;
        bool show = pc && !pc.Seated && pc.enabled;
        if (show != shown) { shown = show; foreach (var r in renderers) if (r) r.enabled = show; }
        if (!show) return;
        Place();

        // blend idle / walk / run by real speed
        float sp = pc.GroundSpeed;
        float walkTop = Mathf.Max(1f, pc.WalkSpeed), runTop = Mathf.Max(walkTop + 0.1f, pc.SprintSpeed);
        float tIdle = sp < 0.15f ? 1f : 0f;
        float tRun = sp > walkTop * 1.05f ? Mathf.Clamp01((sp - walkTop) / Mathf.Max(0.1f, runTop - walkTop) * 1.5f) : 0f;
        float tWalk = 1f - tIdle - tRun;
        float k = 1f - Mathf.Exp(-Time.deltaTime * 10f);
        w[0] = Mathf.Lerp(w[0], tIdle, k); w[1] = Mathf.Lerp(w[1], Mathf.Max(0f, tWalk), k); w[2] = Mathf.Lerp(w[2], tRun, k);
        float sum = w[0] + w[1] + w[2];
        for (int i = 0; i < 3; i++) mixer.SetInputWeight(i, w[i] / Mathf.Max(0.001f, sum));
        mixer.GetInput(1).SetSpeed(Mathf.Clamp(sp / current.walkAnimSpeed, 0.5f, 3.5f));
        mixer.GetInput(2).SetSpeed(Mathf.Clamp(sp / current.runAnimSpeed, 0.6f, 2.5f));
    }
}
