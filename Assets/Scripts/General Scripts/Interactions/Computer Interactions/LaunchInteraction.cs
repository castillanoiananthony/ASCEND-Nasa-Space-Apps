using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using MinMaxCurve = UnityEngine.ParticleSystem.MinMaxCurve;

/// <summary>
/// Put this on the launch console box (it needs a Collider).
/// Look at the box and press E -> T-minus countdown -> ignition under "Booster"
/// -> liftoff with exponential acceleration -> flight -> full reset.
/// While the engines are lit, keys 1 / 2 / 3 switch between 3rd person, nose cam and back.
/// </summary>
public class RocketLaunchConsole : MonoBehaviour
{
    [Header("Interaction")]
    [Tooltip("Uses its own 'look at the box + press E' check. Turn off if your existing interaction system calls Interact() instead.")]
    public bool useBuiltInInteraction = true;
    public float interactDistance = 4f;
    [Tooltip("Shown in the middle of the screen while looking at the box.")]
    public string promptMessage = "Press <b>E</b> to start launch";
    [Tooltip("Offset from screen centre.")]
    public Vector2 promptPosition = new Vector2(0f, -80f);
    public float promptFontSize = 28f;
    [Tooltip("Optional. Defaults to Camera.main at Start.")]
    public Camera playerCamera;

    [Header("Rocket")]
    public string rocketTag = "Rocket";
    public string boosterName = "Booster";

    [Header("Timing (seconds)")]
    public float countdownSeconds = 10f;
    public float ignitionToLiftoff = 3f;
    public float flightSeconds = 45f;

    [Header("Ascent: height = A * (e^(k * t) - 1)")]
    [Tooltip("Overall height multiplier.")]
    public float ascentA = 8f;
    [Tooltip("How aggressively it accelerates. Keep it small for long flights: 0.13 over 45s ends around 2700 units up. 0.35 over 45s would go millions of units up.")]
    public float ascentK = 0.13f;

    [Header("Effects")]
    [Tooltip("Multiplies fire/smoke size. Everything is already scaled to the Booster's radius.")]
    public float effectScale = 1f;
    [Tooltip("Max particles per smoke system. Lower = cheaper.")]
    public int smokeMaxParticles = 300;
    [Tooltip("Optional. Leave empty and the script makes its own.")]
    public Material fireMaterialOverride;
    [Tooltip("Optional. Leave empty and the script makes its own.")]
    public Material smokeMaterialOverride;

    [Header("UI")]
    [Tooltip("Optional. Defaults to the first Canvas in the scene.")]
    public Canvas uiCanvas;
    [Tooltip("Optional, e.g. your PROS SDF font.")]
    public TMP_FontAsset font;
    public Vector2 countdownPosition = new Vector2(50f, -50f);
    public float countdownFontSize = 42f;
    [Tooltip("Offset of the camera key hints from the right edge, middle of the screen.")]
    public Vector2 viewHintPosition = new Vector2(-40f, 0f);
    public float viewHintFontSize = 26f;
    [Tooltip("Colour of the line for the camera you're currently using.")]
    public Color activeViewColor = new Color(0.4f, 0.8f, 1f);

    [Header("Cameras")]
    [Tooltip("3rd person offset from the rocket's centre, in multiples of the rocket's height.")]
    public Vector3 thirdPersonOffset = new Vector3(0.9f, -0.15f, -1.1f);
    [Tooltip("How far out from the centre line the nose cam sits, in booster radii. 0 = dead centre on the tip.")]
    public float noseCamSideOffset = 1.2f;
    [Tooltip("Scripts to switch off while watching from a rocket camera (movement, mouse look...). Leave empty to auto-detect your scripts on the Player.")]
    public Behaviour[] disableWhileInRocketView;

    // ---------------------------------------------------------------------------------------

    enum View { Player, ThirdPerson, Nose }

    const float FlameRate = 160f;
    const float CoreRate = 120f;

    Transform rocket, booster;
    Vector3 rocketStartPos;
    Quaternion rocketStartRot;
    Rigidbody rocketBody;
    bool rocketBodyWasKinematic;
    float rocketHeight, boosterRadius, boosterRadiusRaw;
    Vector3 rocketCenterLocal, noseLocal, engineLocal;

    bool isRunning;
    View currentView = View.Player;
    Camera launchCam;
    readonly List<Behaviour> disabledScripts = new List<Behaviour>();

    TextMeshProUGUI countdownText;
    TextMeshProUGUI promptText;
    TextMeshProUGUI viewHintText;
    GameObject createdCanvas;
    bool viewKeysActive;

    readonly List<GameObject> spawned = new List<GameObject>();
    ParticleSystem flame, flameCore, groundSmoke, trailSmoke;
    Light engineLight;
    float throttle;
    Material fireMat, smokeMat;
    Texture2D softTex;

    // ---------------------------------------------------------------------------------------
    // Read by other scripts (e.g. CameraScreen)

    /// <summary>True from the moment E is pressed until the reset.</summary>
    public bool IsLaunching => isRunning;

    /// <summary>The clock shown on screen: negative during T-minus, then T+ seconds since ignition.</summary>
    public float MissionTime { get; private set; }

    /// <summary>The rocket being launched (null until the first launch).</summary>
    public Transform Rocket => rocket;

    /// <summary>Where the nose cam sits right now. False when no launch is running.</summary>
    public bool TryGetNoseCamPose(out Vector3 position, out Quaternion rotation)
    {
        if (!isRunning || !rocket)
        {
            position = default;
            rotation = default;
            return false;
        }
        position = rocket.TransformPoint(noseLocal);
        rotation = rocket.rotation * Quaternion.Euler(90f, 0f, 0f);
        return true;
    }

    // ---------------------------------------------------------------------------------------

    void Start()
    {
        if (!playerCamera) playerCamera = Camera.main;
    }

    void Update()
    {
        if (!isRunning)
        {
            bool looking = useBuiltInInteraction && IsLookingAtThis();
            ShowPrompt(looking);
            if (looking && KeyDown(KeyCode.E))
                Interact();
            return;
        }

        if (viewKeysActive)
        {
            if (KeyDown(KeyCode.Alpha1)) SetView(View.ThirdPerson);
            if (KeyDown(KeyCode.Alpha2)) SetView(View.Nose);
            if (KeyDown(KeyCode.Alpha3) || KeyDown(KeyCode.Backspace)) SetView(View.Player);
        }

        if (engineLight)
            engineLight.intensity = throttle * 6f * (0.75f + 0.5f * Mathf.PerlinNoise(Time.time * 12f, 0f));
    }

    void LateUpdate()
    {
        if (!isRunning) return;

        if (currentView == View.ThirdPerson && launchCam && rocket)
        {
            Vector3 center = rocket.TransformPoint(rocketCenterLocal);
            launchCam.transform.position = center + thirdPersonOffset * rocketHeight;
            launchCam.transform.LookAt(center);
        }
    }

    void OnDisable()
    {
        ShowPrompt(false);
        if (isRunning) ResetEverything();
    }

    /// <summary>Call this from your own interaction system if you have one.</summary>
    public void Interact()
    {
        if (isRunning) return;
        if (!FindRocket()) return;
        ShowPrompt(false);
        StartCoroutine(LaunchSequence());
    }

    // ---------------------------------------------------------------------------------------
    // Sequence

    IEnumerator LaunchSequence()
    {
        isRunning = true;
        CacheRocketData();
        CreateCountdownText();

        // T-minus countdown
        float t = countdownSeconds;
        while (t > 0f)
        {
            MissionTime = -t;
            countdownText.text = $"T-MINUS {Mathf.CeilToInt(t):00}";
            t -= Time.deltaTime;
            yield return null;
        }

        // Ignition: engines spool up, rocket still held down
        Ignite();
        ShowViewHints();

        float sinceIgnition = 0f;
        while (sinceIgnition < ignitionToLiftoff)
        {
            sinceIgnition += Time.deltaTime;
            MissionTime = sinceIgnition;
            throttle = Mathf.Clamp01(sinceIgnition / Mathf.Max(0.01f, ignitionToLiftoff * 0.6f));
            ApplyThrottle();
            countdownText.text = $"T+ {Mathf.FloorToInt(sinceIgnition):00}\n<size=60%>IGNITION</size>";
            yield return null;
        }

        // Liftoff: exponential climb
        throttle = 1f;
        ApplyThrottle();
        trailSmoke.Play();

        Vector3 upDir = rocketStartRot * Vector3.up;
        float flight = 0f;
        while (flight < flightSeconds)
        {
            flight += Time.deltaTime;
            MissionTime = sinceIgnition + flight;
            float h = ascentA * (Mathf.Exp(ascentK * flight) - 1f);
            rocket.position = rocketStartPos + upDir * h;

            // Pad smoke only billows for the first few seconds
            if (flight > 3f && groundSmoke && groundSmoke.isEmitting)
                groundSmoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            countdownText.text = $"T+ {Mathf.FloorToInt(sinceIgnition + flight):00}\n<size=60%>LIFTOFF   ALT {h:0} m</size>";
            yield return null;
        }

        ResetEverything();
    }

    void ResetEverything()
    {
        SetView(View.Player);

        foreach (var go in spawned) if (go) Destroy(go);
        spawned.Clear();
        flame = flameCore = groundSmoke = trailSmoke = null;
        engineLight = null;

        if (launchCam) Destroy(launchCam.gameObject);
        launchCam = null;

        if (countdownText) Destroy(countdownText.gameObject);
        if (viewHintText) Destroy(viewHintText.gameObject);
        if (createdCanvas) Destroy(createdCanvas);
        countdownText = null;
        viewHintText = null;
        createdCanvas = null;
        viewKeysActive = false;

        if (rocket) rocket.SetPositionAndRotation(rocketStartPos, rocketStartRot);
        if (rocketBody) rocketBody.isKinematic = rocketBodyWasKinematic;

        throttle = 0f;
        MissionTime = 0f;
        isRunning = false;
    }

    // ---------------------------------------------------------------------------------------
    // Rocket data

    bool FindRocket()
    {
        GameObject go = GameObject.FindWithTag(rocketTag);
        if (!go)
        {
            Debug.LogWarning($"[RocketLaunchConsole] No object tagged '{rocketTag}' found.");
            return false;
        }
        rocket = go.transform;
        booster = FindDeep(rocket, boosterName);
        if (!booster)
        {
            Debug.LogWarning($"[RocketLaunchConsole] No child named '{boosterName}' under '{rocket.name}'.");
            return false;
        }
        return true;
    }

    void CacheRocketData()
    {
        rocketStartPos = rocket.position;
        rocketStartRot = rocket.rotation;

        rocketBody = rocket.GetComponent<Rigidbody>();
        if (rocketBody)
        {
            rocketBodyWasKinematic = rocketBody.isKinematic;
            rocketBody.isKinematic = true;
        }

        Bounds rb = GetBounds(rocket);
        Bounds bb = GetBounds(booster);

        rocketHeight = Mathf.Max(1f, rb.size.y);
        boosterRadiusRaw = Mathf.Max(0.1f, Mathf.Max(bb.extents.x, bb.extents.z));
        boosterRadius = boosterRadiusRaw * effectScale;

        rocketCenterLocal = rocket.InverseTransformPoint(rb.center);
        engineLocal = rocket.InverseTransformPoint(new Vector3(bb.center.x, bb.min.y, bb.center.z));
        noseLocal = rocket.InverseTransformPoint(new Vector3(rb.center.x, rb.max.y, rb.center.z))
                    + Vector3.right * boosterRadiusRaw * noseCamSideOffset
                    + Vector3.up * boosterRadiusRaw * 0.3f;
    }

    static Bounds GetBounds(Transform t)
    {
        var renderers = t.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(t.position, t.lossyScale);
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var found = FindDeep(child, name);
            if (found) return found;
        }
        return null;
    }

    // ---------------------------------------------------------------------------------------
    // Effects

    void Ignite()
    {
        BuildMaterials();
        float r = boosterRadius;
        Vector3 enginePos = rocket.TransformPoint(engineLocal);
        Quaternion down = rocket.rotation * Quaternion.Euler(90f, 0f, 0f); // emitter forward = down

        // Effects are parented to the rocket root (scale 1) at the bottom of Booster,
        // so they ride with the rocket without inheriting the cylinder's scale.

        // --- Main flame
        flame = NewParticles("Engine Flame", rocket, enginePos, down, fireMat);
        {
            var m = flame.main;
            m.loop = true; m.duration = 1f;
            m.startLifetime = new MinMaxCurve(0.3f, 0.5f);
            m.startSpeed = new MinMaxCurve(r * 10f, r * 16f);
            m.startSize = new MinMaxCurve(r * 1.4f, r * 2.4f);
            m.startRotation = new MinMaxCurve(0f, Mathf.PI * 2f);
            m.simulationSpace = ParticleSystemSimulationSpace.Local;
            m.maxParticles = 400;
            var s = flame.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 7f; s.radius = r * 0.7f;
            var c = flame.colorOverLifetime; c.enabled = true;
            c.color = Grad((0f, new Color(1f, 0.95f, 0.8f, 1f)),
                           (0.25f, new Color(1f, 0.65f, 0.15f, 0.9f)),
                           (0.7f, new Color(0.9f, 0.25f, 0.05f, 0.5f)),
                           (1f, new Color(0.3f, 0.1f, 0.05f, 0f)));
            var so = flame.sizeOverLifetime; so.enabled = true; so.size = Curve(0.6f, 1.6f);
            var e = flame.emission; e.rateOverTime = 0f;
        }
        flame.Play();

        // --- Hot white core
        flameCore = NewParticles("Engine Core", rocket, enginePos, down, fireMat);
        {
            var m = flameCore.main;
            m.loop = true; m.duration = 1f;
            m.startLifetime = new MinMaxCurve(0.12f, 0.2f);
            m.startSpeed = new MinMaxCurve(r * 14f, r * 18f);
            m.startSize = new MinMaxCurve(r * 0.9f, r * 1.3f);
            m.simulationSpace = ParticleSystemSimulationSpace.Local;
            m.maxParticles = 200;
            var s = flameCore.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 3f; s.radius = r * 0.5f;
            var c = flameCore.colorOverLifetime; c.enabled = true;
            c.color = Grad((0f, new Color(0.85f, 0.9f, 1f, 1f)),
                           (0.5f, new Color(1f, 0.9f, 0.5f, 0.8f)),
                           (1f, new Color(1f, 0.6f, 0.2f, 0f)));
            var e = flameCore.emission; e.rateOverTime = 0f;
        }
        flameCore.Play();

        // --- Launch pad smoke: spreads sideways, slows down with drag, so it stays near the pad
        groundSmoke = NewParticles("Launch Pad Smoke", null, enginePos, Quaternion.Euler(90f, 0f, 0f), smokeMat);
        {
            var m = groundSmoke.main;
            m.loop = true; m.duration = 1f;
            m.startLifetime = new MinMaxCurve(4f, 6f);
            m.startSpeed = new MinMaxCurve(r * 4f, r * 7f);
            m.startSize = new MinMaxCurve(r * 2f, r * 3.5f);
            m.startRotation = new MinMaxCurve(0f, Mathf.PI * 2f);
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = smokeMaxParticles;
            var e = groundSmoke.emission; e.rateOverTime = 70f;
            var s = groundSmoke.shape; s.shapeType = ParticleSystemShapeType.Circle; s.radius = r * 1.2f;
            var v = groundSmoke.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new MinMaxCurve(0f, 0f); v.y = new MinMaxCurve(r * 0.4f, r * 1.2f); v.z = new MinMaxCurve(0f, 0f);
            var l = groundSmoke.limitVelocityOverLifetime; l.enabled = true;
            l.limit = new MinMaxCurve(r * 100f); l.drag = new MinMaxCurve(1.2f);
            l.multiplyDragByParticleSize = false; l.multiplyDragByParticleVelocity = false;
            var c = groundSmoke.colorOverLifetime; c.enabled = true;
            c.color = Grad((0f, new Color(1f, 0.75f, 0.5f, 0f)),
                           (0.08f, new Color(0.95f, 0.9f, 0.85f, 0.75f)),
                           (0.5f, new Color(0.8f, 0.8f, 0.8f, 0.55f)),
                           (1f, new Color(0.7f, 0.7f, 0.7f, 0f)));
            var so = groundSmoke.sizeOverLifetime; so.enabled = true; so.size = Curve(0.6f, 3f);
            var ro = groundSmoke.rotationOverLifetime; ro.enabled = true; ro.z = new MinMaxCurve(-0.4f, 0.4f);
        }
        groundSmoke.Play();

        // --- Exhaust trail (starts at liftoff, left behind in world space)
        trailSmoke = NewParticles("Exhaust Trail", rocket, enginePos - rocket.up * r * 2f, down, smokeMat);
        {
            var m = trailSmoke.main;
            m.loop = true; m.duration = 1f;
            m.startLifetime = new MinMaxCurve(2.5f, 3.5f);
            m.startSpeed = new MinMaxCurve(r * 2f, r * 4f);
            m.startSize = new MinMaxCurve(r * 1.5f, r * 2.5f);
            m.startRotation = new MinMaxCurve(0f, Mathf.PI * 2f);
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = smokeMaxParticles;
            var e = trailSmoke.emission; e.rateOverTime = 30f; e.rateOverDistance = 0.5f / r;
            var s = trailSmoke.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 12f; s.radius = r * 0.6f;
            var c = trailSmoke.colorOverLifetime; c.enabled = true;
            c.color = Grad((0f, new Color(1f, 0.85f, 0.7f, 0f)),
                           (0.1f, new Color(0.95f, 0.95f, 0.95f, 0.7f)),
                           (1f, new Color(0.75f, 0.75f, 0.75f, 0f)));
            var so = trailSmoke.sizeOverLifetime; so.enabled = true; so.size = Curve(0.8f, 3.5f);
            var ro = trailSmoke.rotationOverLifetime; ro.enabled = true; ro.z = new MinMaxCurve(-0.4f, 0.4f);
        }

        // --- Flickering orange light from the engines
        var lightGO = new GameObject("Engine Light");
        lightGO.transform.SetParent(rocket, false);
        lightGO.transform.position = enginePos - rocket.up * r * 3f;
        engineLight = lightGO.AddComponent<Light>();
        engineLight.type = LightType.Point;
        engineLight.color = new Color(1f, 0.6f, 0.25f);
        engineLight.range = r * 30f;
        engineLight.intensity = 0f;
        engineLight.shadows = LightShadows.None;
        spawned.Add(lightGO);

        throttle = 0f;
        ApplyThrottle();
    }

    void ApplyThrottle()
    {
        if (flame) { var e = flame.emission; e.rateOverTime = FlameRate * throttle; }
        if (flameCore) { var e = flameCore.emission; e.rateOverTime = CoreRate * throttle; }
    }

    ParticleSystem NewParticles(string name, Transform parent, Vector3 pos, Quaternion rot, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(pos, rot);
        if (parent) go.transform.SetParent(parent, true);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;

        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = mat;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;

        spawned.Add(go);
        return ps;
    }

    void BuildMaterials()
    {
        if (fireMat && smokeMat) return;

        if (!softTex) softTex = MakeSoftTexture(64);
        Shader additive = Shader.Find("Legacy Shaders/Particles/Additive");
        Shader alpha = Shader.Find("Sprites/Default");

        fireMat = fireMaterialOverride ? fireMaterialOverride
                : new Material(additive ? additive : alpha) { mainTexture = softTex };
        smokeMat = smokeMaterialOverride ? smokeMaterialOverride
                : new Material(alpha) { mainTexture = softTex };
    }

    static Texture2D MakeSoftTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Vector2 c = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), c) / (size * 0.5f);
            float a = Mathf.Clamp01(1f - d);
            a = a * a * (3f - 2f * a);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    static ParticleSystem.MinMaxGradient Grad(params (float time, Color color)[] keys)
    {
        var ck = new GradientColorKey[keys.Length];
        var ak = new GradientAlphaKey[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            ck[i] = new GradientColorKey(keys[i].color, keys[i].time);
            ak[i] = new GradientAlphaKey(keys[i].color.a, keys[i].time);
        }
        var g = new Gradient();
        g.SetKeys(ck, ak);
        return new ParticleSystem.MinMaxGradient(g);
    }

    static MinMaxCurve Curve(float from, float to)
    {
        return new MinMaxCurve(1f, AnimationCurve.Linear(0f, from, 1f, to));
    }

    // ---------------------------------------------------------------------------------------
    // Cameras

    void SetView(View v)
    {
        if (v == View.Player)
        {
            if (launchCam) launchCam.enabled = false;
            if (playerCamera) playerCamera.enabled = true;
            SetPlayerControl(true);
            currentView = v;
            UpdateViewHints();
            return;
        }

        if (!rocket) return;
        EnsureLaunchCamera();

        if (v == View.Nose)
        {
            launchCam.transform.SetParent(rocket, false);
            launchCam.transform.localPosition = noseLocal;
            launchCam.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // looking straight down
        }
        else
        {
            launchCam.transform.SetParent(null, true); // positioned every frame in LateUpdate
        }

        launchCam.enabled = true;
        if (playerCamera) playerCamera.enabled = false;
        SetPlayerControl(false);
        currentView = v;
        UpdateViewHints();
    }

    void EnsureLaunchCamera()
    {
        if (launchCam) return;
        var go = new GameObject("Launch Camera");
        launchCam = go.AddComponent<Camera>();
        if (playerCamera) launchCam.CopyFrom(playerCamera);
        launchCam.nearClipPlane = 0.05f;
        launchCam.farClipPlane = Mathf.Max(launchCam.farClipPlane, 10000f);
        launchCam.fieldOfView = 60f;
        launchCam.enabled = false;
    }

    void SetPlayerControl(bool on)
    {
        if (on)
        {
            foreach (var b in disabledScripts) if (b) b.enabled = true;
            disabledScripts.Clear();
            return;
        }
        if (disabledScripts.Count > 0) return; // already off

        foreach (var b in GetPlayerScripts())
        {
            if (b && b.enabled && b != this)
            {
                b.enabled = false;
                disabledScripts.Add(b);
            }
        }
    }

    IEnumerable<Behaviour> GetPlayerScripts()
    {
        if (disableWhileInRocketView != null && disableWhileInRocketView.Length > 0)
            return disableWhileInRocketView;

        GameObject player = GameObject.FindWithTag("Player");
        Transform root = player ? player.transform : (playerCamera ? playerCamera.transform.parent : null);
        var list = new List<Behaviour>();
        if (!root) return list;

        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>())
        {
            if (mb is UIBehaviour) continue;
            if (mb.transform.IsChildOf(transform)) continue;
            string ns = mb.GetType().Namespace;
            if (ns != null && (ns.StartsWith("Unity") || ns.StartsWith("TMPro"))) continue; // skip engine/URP components
            list.Add(mb);
        }
        return list;
    }

    // ---------------------------------------------------------------------------------------
    // UI

    void EnsureCanvas()
    {
        if (!uiCanvas) uiCanvas = FindFirstObjectByType<Canvas>();
        if (uiCanvas) return;

        createdCanvas = new GameObject("Launch Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        uiCanvas = createdCanvas.GetComponent<Canvas>();
        uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
    }

    void ShowPrompt(bool show)
    {
        if (!show)
        {
            if (promptText && promptText.gameObject.activeSelf) promptText.gameObject.SetActive(false);
            return;
        }

        if (!promptText)
        {
            EnsureCanvas();
            var go = new GameObject("Launch Prompt", typeof(RectTransform));
            go.transform.SetParent(uiCanvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(700f, 60f);

            promptText = go.AddComponent<TextMeshProUGUI>();
            if (font) promptText.font = font;
            promptText.color = Color.white;
            promptText.alignment = TextAlignmentOptions.Center;
            promptText.raycastTarget = false;
        }

        ((RectTransform)promptText.transform).anchoredPosition = promptPosition;
        promptText.fontSize = promptFontSize;
        promptText.text = promptMessage;
        if (!promptText.gameObject.activeSelf) promptText.gameObject.SetActive(true);
    }

    void CreateCountdownText()
    {
        EnsureCanvas();

        var go = new GameObject("Launch Countdown", typeof(RectTransform));
        go.transform.SetParent(uiCanvas.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = countdownPosition;
        rt.sizeDelta = new Vector2(600f, 140f);

        countdownText = go.AddComponent<TextMeshProUGUI>();
        if (font) countdownText.font = font;
        countdownText.fontSize = countdownFontSize;
        countdownText.fontStyle = FontStyles.Bold;
        countdownText.color = Color.white;
        countdownText.alignment = TextAlignmentOptions.TopLeft;
        countdownText.raycastTarget = false;
    }

    void ShowViewHints()
    {
        EnsureCanvas();

        var go = new GameObject("Launch View Hints", typeof(RectTransform));
        go.transform.SetParent(uiCanvas.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = viewHintPosition;
        rt.sizeDelta = new Vector2(500f, 160f);

        viewHintText = go.AddComponent<TextMeshProUGUI>();
        if (font) viewHintText.font = font;
        viewHintText.fontSize = viewHintFontSize;
        viewHintText.color = Color.white;
        viewHintText.alignment = TextAlignmentOptions.MidlineRight;
        viewHintText.lineSpacing = 20f;
        viewHintText.raycastTarget = false;

        viewKeysActive = true;
        UpdateViewHints();
    }

    void UpdateViewHints()
    {
        if (!viewHintText) return;
        viewHintText.text =
            HintLine("Press <b>1</b> for 3rd Person View", currentView == View.ThirdPerson) + "\n" +
            HintLine("Press <b>2</b> for Nose Cam", currentView == View.Nose) + "\n" +
            HintLine("Press <b>3</b> to go back", currentView == View.Player);
    }

    string HintLine(string text, bool active)
    {
        return active ? $"<color=#{ColorUtility.ToHtmlStringRGB(activeViewColor)}>{text}</color>" : text;
    }

    // ---------------------------------------------------------------------------------------
    // Input

    bool IsLookingAtThis()
    {
        Camera cam = playerCamera ? playerCamera : Camera.main;
        if (!cam) return false;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, ~0, QueryTriggerInteraction.Collide))
            return hit.transform == transform || hit.transform.IsChildOf(transform);
        return false;
    }

    static bool KeyDown(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return false;
        switch (key)
        {
            case KeyCode.E: return kb.eKey.wasPressedThisFrame;
            case KeyCode.Alpha1: return kb.digit1Key.wasPressedThisFrame;
            case KeyCode.Alpha2: return kb.digit2Key.wasPressedThisFrame;
            case KeyCode.Alpha3: return kb.digit3Key.wasPressedThisFrame;
            case KeyCode.Backspace: return kb.backspaceKey.wasPressedThisFrame;
        }
        return false;
#else
        return Input.GetKeyDown(key);
#endif
    }
}
