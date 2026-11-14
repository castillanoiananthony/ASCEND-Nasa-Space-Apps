// ═════════════════════════════════════════════════════════════════════════════
//  AscendOrbit.cs  -  the mission continues in space (Solar System scene)
//
//  Nothing to set up: when your rocket crosses the Karman line (Earth Watcher / Moon Mapper),
//  AscendFlight loads the "Solar System" scene and this script takes over by itself:
//    1. ORBIT     your upper stage + satellite circle the Earth (real orbit speed and period)
//                 hold right mouse to look around, scroll to zoom, SPACE releases the satellite
//    2. RELEASE   the satellite separates from the rocket and drifts away
//    3. CONSOLE   back at the Satellite Control computer: live camera of the satellite
//                 W A S D turn the camera, Q E roll, scroll (or Z X) zoom, SPACE takes a photo
//    4. THE END   after 5 photos of Earth the photos are rated and ranked, the government decides
//                 how interested it is, your money goes up or down, and a report goes to PhMail.
//                 "Return to Mission Control" loads the scene you launched from.
//
//  Needs: the scene with your Earth (an object with the earthBehaviour script, or named "Earth").
//  Both scenes must be in File > Build Profiles (Scene List).
//  The satellite is drawn at real size (meters) on its own layer (31) and blended over the
//  scaled solar system, so it looks right next to the Earth even though the Earth is tiny in units.
// ═════════════════════════════════════════════════════════════════════════════
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif
using Object = UnityEngine.Object;

// What the launch hands over to the space scene
public static class AscendOrbitTransfer
{
    public static bool Pending { get; private set; }
    public static AscendCADData Data;
    public static CADDesign Rocket, Payload;
    public static List<int> Keep = new List<int>();
    public static AscendSimMission Mission;
    public static float AltKm = 700f, FlightTime, MaxQ, MaxG;
    public static string ReturnScene = "Universe";

    public static void Begin(AscendCADData data, CADDesign rocket, CADDesign payload, List<int> keep, AscendSimMission mission,
        float altKm, string returnScene, float flightTime, float maxQ, float maxG)
    {
        Data = data; Rocket = rocket?.Clone(); Payload = payload?.Clone(); Keep = keep ?? new List<int>();
        Mission = mission; AltKm = Mathf.Clamp(altKm, 160f, 2000f); ReturnScene = returnScene;
        FlightTime = flightTime; MaxQ = maxQ; MaxG = maxG;
        Pending = true;
    }

    public static void Consume() { Pending = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Pending = false; Data = null; Rocket = Payload = null; Keep = new List<int>(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
    }

    static void OnLoaded(Scene s, LoadSceneMode mode)
    {
        if (!Pending || Object.FindAnyObjectByType<AscendOrbitDirector>()) return;
        new GameObject("Ascend Orbit").AddComponent<AscendOrbitDirector>();
    }
}

public class AscendOrbitDirector : MonoBehaviour
{
    const int RigLayer = 31;
    const double Mu = 3.986004418e14, EarthRm = 6371000.0;
    static readonly Vector3 RigOrigin = new Vector3(50000f, 50000f, 50000f);
    const float MinSpeed = 1f, MaxSpeed = 5f;   // time speed slider (1x - 5x)
    // Game time: one orbit takes 2 minutes at 1x, and the Earth turns once every 10 orbits,
    // so every place on Earth comes into view within one turn (with the camera tilted)
    const float OrbitSeconds = 120f, EarthDayOrbits = 10f;
    double missionHours;
    readonly bool[] done = new bool[5];
    Vector3 spaceSun;
    Quaternion sunOriginal;
    bool playerWasEnabled;
    readonly List<(Light l, int mask)> litRig = new List<(Light, int)>();
    readonly List<ComputerInteraction> pausedComputers = new List<ComputerInteraction>();
    float savedAmbient = 1f, savedReflect = 1f;

    enum Phase { Arrive, Release, Console, End }
    Phase phase = Phase.Arrive;

    AscendCADData data;
    Transform earth;
    float earthR = 0.637f, unitsPerM;
    Light sunLight, rigSun, earthShine;
    Transform sunObj;

    // orbit
    double rM, omega, theta;
    Vector3 axisX, axisY = Vector3.up;
    float speed = 1f;

    // models (real size, on the rig layer)
    Transform rig, assembly, stageGo, payloadGo;
    Vector3 stageVel, payloadVel, stageSpin;
    float assemblyHeight = 10f;

    // cameras
    Camera spaceCam, rigCam, satCam;
    RenderTexture satRT;
    readonly List<Camera> sceneCams = new List<Camera>();
    float camYaw = 150f, camPitch = 18f, camDist = 30f;
    float photoYaw, photoPitch, photoRoll, photoFov = 40f;

    // UI
    Canvas canvas;
    CanvasGroup fade;
    GameObject hud, console, endCard;
    TextMeshProUGUI hudText, bigText, conInfo, conCount, flashText;
    RawImage liveView;
    Image flash;
    readonly List<RawImage> thumbs = new List<RawImage>();
    readonly List<TextMeshProUGUI> thumbLabels = new List<TextMeshProUGUI>();
    float bigUntil, releaseAt = -1f, arriveTime;

    class Photo { public Texture2D tex; public float score; public string kind; public int number, target; }
    readonly List<Photo> photos = new List<Photo>();
    const int PhotoGoal = 5;

    // ═════════════════════════════════════════════════════════════════════════
    bool ready;
    GameObject cover;
    CanvasGroup coverGroup;

    IEnumerator RemoveCover()
    {
        yield return new WaitForSecondsRealtime(1.4f);
        for (float t = 0f; t < 1.6f && coverGroup; t += Time.unscaledDeltaTime) { coverGroup.alpha = 1f - t / 1.6f; yield return null; }
        if (cover) Destroy(cover);
        Big("IN ORBIT", 2.5f);
    }

    void Awake()
    {
        cover = new GameObject("Orbit Cover", typeof(RectTransform), typeof(Canvas));
        var c = cover.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 999;
        var sc = cover.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        coverGroup = cover.AddComponent<CanvasGroup>();
        var img = new GameObject("Black", typeof(RectTransform)).AddComponent<Image>();
        img.transform.SetParent(cover.transform, false); img.color = Color.black;
        var r = img.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        var title = MissionUI.Txt(cover.transform, "ENTERING ORBIT", 64, Color.white, TMPro.FontStyles.Bold);
        title.characterSpacing = 18f; title.alignment = TMPro.TextAlignmentOptions.Center;
        MissionUI.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(1600, 100));
        var sub = MissionUI.Lbl(cover.transform, "ENGINES OFF   //   " + AscendOrbitTransfer.AltKm.ToString("N0") + " KM ABOVE EARTH   //   27,000 KM/H", 16);
        sub.alignment = TMPro.TextAlignmentOptions.Center;
        MissionUI.Place(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(1600, 40));
    }

    IEnumerator Start()
    {
        AscendOrbitTransfer.Consume();
        yield return null;   // let the scene's own scripts (like earthBehaviour) set up first
        data = AscendOrbitTransfer.Data ? AscendOrbitTransfer.Data : (AscendCADData.Active ? AscendCADData.Active : AscendCADData.Resolve(null));
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;

        FindEarth();
        FindSun();
        Recenter();

        rM = EarthRm + AscendOrbitTransfer.AltKm * 1000.0;
        omega = 2.0 * Math.PI / OrbitSeconds;
        Vector3 sd = SunDir();
        axisX = Vector3.ProjectOnPlane(sd, axisY).normalized;
        if (axisX.sqrMagnitude < 0.5f) axisX = Vector3.right;
        theta = -0.35;                        // start just before the sunny middle of the day side
        // Our home first: turn the Earth so the Philippines is just ahead of the satellite
        {
            Vector3 ph = hdEarth.WorldDir(12.5f, 122.5f);
            float a = Vector3.SignedAngle(Vector3.ProjectOnPlane(ph, Vector3.up), Vector3.ProjectOnPlane(Radial(), Vector3.up), Vector3.up);
            earth.Rotate(0f, a, 0f, Space.World);
        }
        if (PlayerController.Instance && PlayerController.Instance.enabled) { playerWasEnabled = true; PlayerController.Instance.enabled = false; }
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.enabled && (l.cullingMask & (1 << RigLayer)) != 0 && l != rigSun) { litRig.Add((l, l.cullingMask)); l.cullingMask &= ~(1 << RigLayer); }
        foreach (var ci in FindObjectsByType<ComputerInteraction>(FindObjectsSortMode.None)) if (ci.enabled) { pausedComputers.Add(ci); ci.enabled = false; }
        MissionBriefing.SetUiVisible(false);
        savedAmbient = RenderSettings.ambientIntensity; savedReflect = RenderSettings.reflectionIntensity;
        RenderSettings.reflectionIntensity = 0.15f;
        AscendSky.SetAltitude(AscendOrbitTransfer.AltKm * 1000f);

        SetupCameras();
        BuildModels();
        BuildUI();
        arriveTime = Time.time;
        ready = true;
        StartCoroutine(RemoveCover());
    }

    void OnDestroy()
    {
        if (satRT) satRT.Release();
    }

    // ───────────── find things in the scene ─────────────
    // The HD Earth lives in this scene, far away and on its own layer (1 unit = 1 km)
    AscendEarth hdEarth;
    void FindEarth()
    {
        hdEarth = AscendEarth.Create();
        earth = hdEarth.transform;
        earthR = AscendEarth.RadiusKm;
        unitsPerM = earthR / (float)EarthRm;
    }

    // A very smooth sphere whose texture lines up with the original mesh (longitude/latitude read from it)
    static Mesh SmoothSphere(Mesh orig, float radius)
    {
        var ov = orig.vertices; var ou = orig.uv;
        // How longitude maps to u: find two equator points and fit u = a * lon / 360 + b
        int i1 = -1, i2 = -1;
        for (int i = 0; i < ov.Length && ou.Length == ov.Length; i++)
        {
            if (Mathf.Abs(ov[i].y) > radius * 0.05f || ou[i].x < 0.1f || ou[i].x > 0.9f) continue;
            if (i1 < 0) { i1 = i; continue; }
            if (Mathf.Abs(ou[i].x - ou[i1].x) > 0.15f && Mathf.Abs(ou[i].x - ou[i1].x) < 0.6f) { i2 = i; break; }
        }
        float a = 1f, b = 0f;
        if (i1 >= 0 && i2 >= 0)
        {
            float l1 = Mathf.Atan2(ov[i1].z, ov[i1].x) * Mathf.Rad2Deg / 360f, l2 = Mathf.Atan2(ov[i2].z, ov[i2].x) * Mathf.Rad2Deg / 360f;
            float d = Mathf.DeltaAngle(l1 * 360f, l2 * 360f) / 360f;
            a = Mathf.Sign((ou[i2].x - ou[i1].x) / (Mathf.Abs(d) > 1e-4f ? d : 1e-4f));
            b = ou[i1].x - a * l1;
        }
        // Latitude: linear in angle (UV sphere) unless the original says otherwise
        bool linearY = false;
        for (int i = 0; i < ov.Length && ou.Length == ov.Length; i++)
            if (Mathf.Abs(ov[i].y / radius - 0.5f) < 0.03f) { float vAng = 0.5f + Mathf.Asin(Mathf.Clamp(ov[i].y / radius, -1f, 1f)) / Mathf.PI, vLin = 0.5f + ov[i].y / radius * 0.5f; linearY = Mathf.Abs(ou[i].y - vLin) < Mathf.Abs(ou[i].y - vAng); break; }

        const int N = 360, M = 180;
        float lon0 = -b / a;                 // where u = 0
        var v = new Vector3[(N + 1) * (M + 1)]; var n = new Vector3[v.Length]; var uv = new Vector2[v.Length];
        for (int j = 0; j <= M; j++)
        {
            float lat = Mathf.Lerp(-90f, 90f, j / (float)M) * Mathf.Deg2Rad;
            for (int i = 0; i <= N; i++)
            {
                float u = i / (float)N;
                float lon = (lon0 + u / a) * 2f * Mathf.PI;
                var dir = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                int k = j * (N + 1) + i;
                v[k] = dir * radius; n[k] = dir;
                uv[k] = new Vector2(u, linearY ? 0.5f + dir.y * 0.5f : j / (float)M);
            }
        }
        var tri = new int[N * M * 6]; int t = 0;
        bool flip = a < 0f;
        for (int j = 0; j < M; j++)
        for (int i = 0; i < N; i++)
        {
            int p = j * (N + 1) + i, q = p + 1, r = p + N + 1, s2 = r + 1;
            if (!flip) { tri[t++] = p; tri[t++] = r; tri[t++] = q; tri[t++] = q; tri[t++] = r; tri[t++] = s2; }
            else { tri[t++] = p; tri[t++] = q; tri[t++] = r; tri[t++] = q; tri[t++] = s2; tri[t++] = r; }
        }
        var m = new Mesh { name = "Smooth Earth", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.vertices = v; m.normals = n; m.uv = uv; m.triangles = tri;
        // make sure the faces point outward
        var tr = m.triangles;
        int mid = (M / 2) * N * 6;
        Vector3 cm = Vector3.Cross(v[tr[mid + 1]] - v[tr[mid]], v[tr[mid + 2]] - v[tr[mid]]);
        if (Vector3.Dot(cm, v[tr[mid]]) < 0f) { for (int k = 0; k < tr.Length; k += 3) { int tmp = tr[k + 1]; tr[k + 1] = tr[k + 2]; tr[k + 2] = tmp; } m.triangles = tr; }
        m.RecalculateTangents();
        m.RecalculateBounds();
        return m;
    }

    // Thin blue glow around the Earth's edge (brightest at the rim, on the day side)
    Mesh atmoMesh; Vector3[] atmoN, atmoV; Color[] atmoC; Transform atmo;
    void BuildAtmosphere(Transform earthMesh, float radius)
    {
        const int N = 128, M = 64;
        var v = new Vector3[(N + 1) * (M + 1)]; atmoN = new Vector3[v.Length]; atmoC = new Color[v.Length];
        for (int j = 0; j <= M; j++)
        for (int i = 0; i <= N; i++)
        {
            float lat = Mathf.Lerp(-90f, 90f, j / (float)M) * Mathf.Deg2Rad, lon = i / (float)N * 2f * Mathf.PI;
            var d = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
            int k = j * (N + 1) + i; v[k] = d * radius * 1.016f; atmoN[k] = d; atmoC[k] = new Color(0.4f, 0.65f, 1f, 0f);
        }
        var tri = new int[N * M * 6]; int t = 0;
        for (int j = 0; j < M; j++)
        for (int i = 0; i < N; i++)
        {
            int p = j * (N + 1) + i, q = p + 1, r = p + N + 1, s2 = r + 1;
            tri[t++] = p; tri[t++] = q; tri[t++] = r; tri[t++] = q; tri[t++] = s2; tri[t++] = r;
        }
        atmoMesh = new Mesh { name = "Atmosphere" };
        atmoV = v;
        atmoMesh.vertices = v; atmoMesh.triangles = tri; atmoMesh.colors = atmoC; atmoMesh.normals = atmoN;
        atmoMesh.RecalculateBounds();
        atmo = new GameObject("Atmosphere Glow").transform;
        atmo.SetParent(earthMesh, false);
        atmo.gameObject.AddComponent<MeshFilter>().sharedMesh = atmoMesh;
        var mr = atmo.gameObject.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
        mr.sharedMaterial = new Material(sh) { name = "Atmosphere", renderQueue = 3000 };
    }

    void UpdateAtmosphere(Vector3 camPos)
    {
        if (!atmo) return;
        Vector3 sd = SunDir();
        var verts = atmoV;
        for (int k = 0; k < atmoN.Length; k++)
        {
            Vector3 wp = atmo.TransformPoint(verts[k]);
            Vector3 wn = atmo.TransformDirection(atmoN[k]);
            float facing = Mathf.Abs(Vector3.Dot(wn, (camPos - wp).normalized));
            float rim = Mathf.Pow(1f - facing, 3f);
            float day = Mathf.Clamp01(Vector3.Dot(wn, sd) * 1.5f + 0.35f);
            atmoC[k].a = Mathf.Clamp01(rim * 1.1f) * day * 0.85f;
        }
        atmoMesh.colors = atmoC;
    }

    void FindSun()
    {
        sunLight = RenderSettings.sun;
        if (!sunLight) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional && l.enabled) { sunLight = l; break; }
        Vector3 s = sunLight ? -sunLight.transform.forward : new Vector3(1f, 0.3f, 0f);
        s.y = 0f; if (s.sqrMagnitude < 1e-4f) s = Vector3.right;
        spaceSun = (s.normalized + Vector3.up * 0.15f).normalized;
        if (sunLight) { sunOriginal = sunLight.transform.rotation; sunLight.transform.rotation = Quaternion.LookRotation(-spaceSun); }
        return;
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant().Contains("sun") && !t.name.ToLowerInvariant().Contains("sunlight") && t != earth) { sunObj = t; break; }
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional && l.enabled) { sunLight = l; break; }
    }

    Vector3 SunDir()
    {
        if (spaceSun.sqrMagnitude > 0.5f) return spaceSun;
        if (sunObj && (sunObj.position - earth.position).sqrMagnitude > 1f) return (sunObj.position - earth.position).normalized;
        if (sunLight) return -sunLight.transform.forward;
        return Vector3.right;
    }

    // Keep the Earth at the world's center (big numbers make things shake)
    void Recenter()
    {
        if (hdEarth) return;   // the HD Earth stays put; never move the Universe scene
        if (!earth) return;
        Vector3 e = earth.position;
        if (e.sqrMagnitude < 1e-10f) return;
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (go == gameObject || (rig && go == rig.gameObject)) continue;
            if (go.GetComponent<Canvas>()) continue;
            go.transform.position -= e;
        }
    }

    // ───────────── cameras ─────────────
    void SetupCameras()
    {
        foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (c.enabled) { sceneCams.Add(c); c.enabled = false; }
        var template = sceneCams.FirstOrDefault();

        spaceCam = new GameObject("Space Camera").AddComponent<Camera>();
        spaceCam.transform.SetParent(transform, false);
        if (template) spaceCam.CopyFrom(template);
        spaceCam.enabled = true;
        spaceCam.clearFlags = CameraClearFlags.Skybox;
        spaceCam.cullingMask = 1 << AscendEarth.Layer;
        spaceCam.nearClipPlane = 0.5f; spaceCam.farClipPlane = 60000f;
        spaceCam.fieldOfView = 55f;
        spaceCam.depth = 10f;
        spaceCam.targetTexture = null;
        if (!FindAnyObjectByType<AudioListener>()) spaceCam.gameObject.AddComponent<AudioListener>();

        rigCam = new GameObject("Satellite Camera (real size)").AddComponent<Camera>();
        rigCam.transform.SetParent(transform, false);
        rigCam.clearFlags = CameraClearFlags.Depth;
        rigCam.cullingMask = 1 << RigLayer;
        rigCam.nearClipPlane = 0.05f; rigCam.farClipPlane = 20000f;
        rigCam.fieldOfView = 55f;
        rigCam.depth = 11f;

        // URP: draw the real-size satellite on top of the scaled Earth (camera stacking)
        try
        {
            var baseData = spaceCam.GetUniversalAdditionalCameraData();
            baseData.renderType = CameraRenderType.Base;
            baseData.renderPostProcessing = true;
            var ov = rigCam.GetUniversalAdditionalCameraData();
            ov.renderType = CameraRenderType.Overlay;
            if (!baseData.cameraStack.Contains(rigCam)) baseData.cameraStack.Add(rigCam);
        }
        catch (Exception e) { Debug.LogWarning("AscendOrbit: camera stacking failed (is URP active?): " + e.Message); }

        // Sunlight for the real-size satellite
        rigSun = new GameObject("Sunlight (satellite)").AddComponent<Light>();
        rigSun.transform.SetParent(transform, false);
        rigSun.type = LightType.Directional;
        rigSun.intensity = 1.6f;
        rigSun.color = new Color(1f, 0.97f, 0.92f);
        rigSun.cullingMask = 1 << RigLayer;
        rigSun.shadows = LightShadows.Soft;

        // Soft blue light reflected off the Earth, so the dark side of the satellite isn't pitch black
        earthShine = new GameObject("Earthshine (satellite)").AddComponent<Light>();
        earthShine.transform.SetParent(transform, false);
        earthShine.type = LightType.Directional;
        earthShine.intensity = 0.7f;
        earthShine.color = new Color(0.55f, 0.7f, 1f);
        earthShine.cullingMask = 1 << RigLayer;
        earthShine.shadows = LightShadows.None;
    }

    // ───────────── the rocket's upper stage + satellite, at real size ─────────────
    void BuildModels()
    {
        rig = new GameObject("Satellite Rig (real size)").transform;
        rig.position = RigOrigin;
        assembly = new GameObject("Upper Stage + Satellite").transform;
        assembly.SetParent(rig, false);

        var t = AscendOrbitTransfer.Rocket;
        if (data && t != null && !t.IsEmpty)
        {
            var sroot = new GameObject("Upper Stage").transform;
            sroot.SetParent(assembly, false);
            var built = AscendLaunchSite.BuildDesign(t, data, sroot, RigLayer, false);
            var remove = new List<GameObject>();
            int child = 0;
            for (int i = 0; i < t.parts.Count; i++)
            {
                var p = data.Get(t.parts[i].partId);
                if (p == null || p.attach == CADAttach.Internal) continue;
                if (child < built.transform.childCount)
                {
                    var go = built.transform.GetChild(child).gameObject;
                    if (!AscendOrbitTransfer.Keep.Contains(i)) remove.Add(go);
                    else if (p.role == CADRole.Fairing) fairingGos.Add(go.transform);
                }
                child++;
            }
            if (remove.Count < built.transform.childCount) foreach (var g in remove) { fairingGos.Remove(g.transform); DestroyImmediate(g); }
            stageGo = sroot;
            CenterOnBase(built.transform, sroot);
        }
        else
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(assembly, false); g.transform.localScale = new Vector3(1.2f, 3f, 1.2f); g.transform.localPosition = Vector3.up * 3f;
            stageGo = g.transform;
        }
        // The satellite sits on top of the stage, inside the nose cover (fairing)
        foreach (var f in fairingGos) f.gameObject.SetActive(false);
        float stageTop = TopOf(stageGo);
        foreach (var f in fairingGos) f.gameObject.SetActive(true);

        var pd = AscendOrbitTransfer.Payload;
        var proot = new GameObject("Satellite").transform;
        proot.SetParent(assembly, false);
        if (data && pd != null && !pd.IsEmpty)
        {
            var pb = AscendLaunchSite.BuildDesign(pd, data, proot, RigLayer, false);
            CenterOnBase(pb.transform, proot);
        }
        else
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(proot, false); g.transform.localScale = Vector3.one * 0.6f; g.transform.localPosition = Vector3.up * 0.3f;
        }
        proot.localPosition = Vector3.up * (stageTop + 0.05f);
        payloadGo = proot;
        assemblyHeight = Mathf.Max(1f, Mathf.Max(TopOf(payloadGo), TopOf(stageGo)));
        SetLayer(rig.gameObject, RigLayer);
        camDist = Mathf.Clamp(assemblyHeight * 2.2f, 6f, 120f);
        PlaceOrbit(0f);
    }

    // Move a built design so its bottom-center sits at its parent's origin
    static void CenterOnBase(Transform built, Transform parent)
    {
        var rs = built.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;
        Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        Vector3 local = parent.InverseTransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
        built.localPosition -= local;
    }

    float TopOf(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return 1f;
        float top = float.MinValue;
        foreach (var r in rs) top = Mathf.Max(top, assembly.InverseTransformPoint(r.bounds.center + Vector3.up * r.bounds.extents.y).y);
        return Mathf.Max(0.5f, top);
    }

    static void SetLayer(GameObject g, int layer)
    {
        g.layer = layer;
        foreach (Transform c in g.transform) SetLayer(c.gameObject, layer);
    }

    // ───────────── orbit ─────────────
    Vector3 SatPos() => (earth ? earth.position : Vector3.zero) + (axisX * (float)Math.Cos(theta) + axisY * (float)Math.Sin(theta)) * (float)(rM * unitsPerM);
    Vector3 Radial() => (axisX * (float)Math.Cos(theta) + axisY * (float)Math.Sin(theta)).normalized;
    Vector3 Prograde() => (-axisX * (float)Math.Sin(theta) + axisY * (float)Math.Cos(theta)).normalized;

    void PlaceOrbit(float dt)
    {
        theta += omega * dt;
        // The stack points along the direction of travel, belly toward the Earth
        assembly.rotation = Quaternion.LookRotation(-Radial(), Prograde());
    }

    // ═════════════════════════════════════════════════════════════════════════
    void Update()
    {
        if (!ready) return;
        if (KeyDown(KeyCode.Minus)) SetSpeed(speed - 0.5f);
        if (KeyDown(KeyCode.Equals)) SetSpeed(speed + 0.5f);
        // Arriving and releasing happen in slow motion (close to real orbital speed),
        // so the photo mission starts in daylight near the Philippines
        float dt = Time.deltaTime * speed * (phase == Phase.Console || phase == Phase.End ? 1f : 0.08f);
        if (hdEarth) { hdEarth.Spin(360f / (OrbitSeconds * EarthDayOrbits) * dt, dt); hdEarth.SetSun(SunDir()); }
        if (phase == Phase.Console && !finishing) missionHours += dt / (OrbitSeconds * EarthDayOrbits) * 24.0;

        if (phase == Phase.Arrive || phase == Phase.Release) PlaceOrbit(dt);
        else theta += omega * dt;

        if (phase == Phase.Arrive && (KeyDown(KeyCode.Space) || Time.time - arriveTime > 25f)) StartRelease();
        if (phase == Phase.Release)
        {
            float rdt = Time.deltaTime;
            MoveDrifting(rdt);
            payloadGo.position += payloadVel * rdt;
            stageGo.position += stageVel * rdt;
            stageGo.Rotate(stageSpin * rdt, Space.World);
            if (releaseAt > 0f && Time.time - releaseAt > 7f) { releaseAt = -1f; StartCoroutine(ToConsole()); }
        }
        if (phase == Phase.Console) { ConsoleUpdate(); UpdateConsoleUI(); }

        if (bigText && Time.time > bigUntil) bigText.text = "";
        UpdateHud();
    }

    void LateUpdate()
    {
        if (!ready) return;
        Recenter();
        Vector3 sd = SunDir();
        if (rigSun) rigSun.transform.rotation = Quaternion.LookRotation(-sd);
        if (earthShine) earthShine.transform.rotation = Quaternion.LookRotation(Radial());   // light comes up from the Earth below

        if (phase == Phase.Arrive || phase == Phase.Release)
        {
            Vector2 md = RightMouseHeld() ? MouseDelta() : Vector2.zero;
            camYaw += md.x * 3f; camPitch = Mathf.Clamp(camPitch - md.y * 3f, -80f, 80f);
            camDist = Mathf.Clamp(camDist * (1f - Scroll() * 0.1f), 3f, 400f);
        }

        // Real-size camera around the satellite; the space camera copies it in scaled units
        Transform target = phase == Phase.Release || phase == Phase.Console ? payloadGo : assembly;
        Vector3 center = target.position + target.up * (target == assembly ? assemblyHeight * 0.5f : 0.5f);
        Quaternion frame = Quaternion.LookRotation(Prograde(), Radial());
        Vector3 offset = frame * (Quaternion.Euler(camPitch, camYaw, 0f) * new Vector3(0f, 0f, -camDist));
        rigCam.transform.position = center + offset;
        rigCam.transform.rotation = Quaternion.LookRotation(center - rigCam.transform.position, Radial());

        Vector3 satU = SatPos();
        spaceCam.transform.position = satU + (rigCam.transform.position - RigOrigin) * unitsPerM;
        spaceCam.transform.rotation = rigCam.transform.rotation;
        spaceCam.fieldOfView = rigCam.fieldOfView;



        if (satCam)
        {
            Quaternion nadir = Quaternion.LookRotation(-Radial(), Prograde());
            satCam.transform.position = satU - Radial() * (2f * unitsPerM);
            satCam.transform.rotation = nadir * Quaternion.Euler(photoPitch, photoYaw, photoRoll);
            satCam.fieldOfView = photoFov;
        }
    }

    // ───────────── release ─────────────
    readonly List<Transform> fairingGos = new List<Transform>();
    readonly List<(Transform t, Vector3 vel, Vector3 spin)> drifting = new List<(Transform, Vector3, Vector3)>();

    void StartRelease()
    {
        phase = Phase.Release;
        StartCoroutine(ReleaseSequence());
    }

    IEnumerator ReleaseSequence()
    {
        // 1. The nose cover (fairing) opens and falls away
        if (fairingGos.Count > 0)
        {
            Big("FAIRING SEPARATION", 2f);
            foreach (var f in fairingGos)
            {
                if (!f) continue;
                f.SetParent(rig, true);
                Vector3 side = Vector3.ProjectOnPlane(UnityEngine.Random.onUnitSphere, Prograde()).normalized;
                drifting.Add((f, Prograde() * 1.2f + side * 1.5f, UnityEngine.Random.onUnitSphere * 25f));
            }
            yield return new WaitForSeconds(2.5f);
        }
        // 2. The satellite separates
        releaseAt = Time.time;
        payloadGo.SetParent(rig, true);
        stageGo.SetParent(rig, true);
        Vector3 pro = Prograde();
        payloadVel = pro * 0.7f;                      // springs push the satellite forward
        stageVel = -pro * 0.35f + Radial() * 0.05f;   // the empty stage drifts back
        stageSpin = UnityEngine.Random.onUnitSphere * 4f;
        // look at the satellite now (it's much smaller than the rocket stage)
        var rs = payloadGo.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0) { Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); camDist = Mathf.Clamp(b.size.magnitude * 3.5f, 2.5f, 40f); }
        Big("SATELLITE RELEASED!", 3f);
    }

    void MoveDrifting(float dt)
    {
        foreach (var d in drifting)
        {
            if (!d.t) continue;
            d.t.position += d.vel * dt;
            d.t.Rotate(d.spin * dt, Space.World);
        }
    }

    IEnumerator ToConsole()
    {
        Big("<size=55%>Switching to Satellite Control...</size>", 2f);
        yield return new WaitForSeconds(1.5f);
        OpenConsole();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  SATELLITE CONTROL CONSOLE
    // ═════════════════════════════════════════════════════════════════════════
    void OpenConsole()
    {
        phase = Phase.Console;
        missionHours = 0;
        satRT = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { name = "Satellite Camera", antiAliasing = 2 };
        satCam = new GameObject("Satellite Photo Camera").AddComponent<Camera>();
        satCam.transform.SetParent(transform, false);
        satCam.CopyFrom(spaceCam);
        satCam.cullingMask = 1 << AscendEarth.Layer;
        satCam.targetTexture = satRT;
        satCam.depth = 5f;
        satCam.nearClipPlane = 0.5f; satCam.farClipPlane = 60000f;
        try { var d = satCam.GetUniversalAdditionalCameraData(); d.renderType = CameraRenderType.Base; d.cameraStack.Clear(); d.renderPostProcessing = false; } catch (Exception) { }
        photoPitch = photoYaw = photoRoll = 0f; photoFov = 50f;
        liveView.texture = satRT;
        hud.SetActive(false);
        console.SetActive(true);
    }

    void ConsoleUpdate()
    {
        float dt = Time.deltaTime;
        float turn = 35f * dt * Mathf.Lerp(0.25f, 1f, photoFov / 60f);
        if (Held(KeyCode.W)) photoPitch -= turn;
        if (Held(KeyCode.S)) photoPitch += turn;
        if (Held(KeyCode.A)) photoYaw -= turn;
        if (Held(KeyCode.D)) photoYaw += turn;
        if (Held(KeyCode.Q)) photoRoll += 40f * dt;
        if (Held(KeyCode.E)) photoRoll -= 40f * dt;
        if (Held(KeyCode.Z)) photoFov -= 20f * dt;
        if (Held(KeyCode.X)) photoFov += 20f * dt;
        photoFov = Mathf.Clamp(photoFov * (1f - Scroll() * 0.08f), 4f, 80f);
        photoPitch = Mathf.Clamp(photoPitch, -70f, 70f);
        photoYaw = Mathf.Clamp(photoYaw, -70f, 70f);
        if (KeyDown(KeyCode.Space) && !finishing) TakePhoto();
    }

    bool finishing;

    void TakePhoto()
    {
        Vector3 sd = SunDir();
        int best = -1; float bestScore = -1f; string whyNot = null;
        var T = AscendEarth.Targets;
        for (int i = 0; i < T.Length; i++)
        {
            if (done[i]) continue;
            Vector3 p = hdEarth.WorldPoint(T[i].lat, T[i].lon);
            Vector3 n = (p - earth.position).normalized;
            Vector3 toCam = satCam.transform.position - p;
            if (Vector3.Dot(n, toCam) <= 0f) continue;                       // behind the horizon
            Vector3 vp = satCam.WorldToViewportPoint(p);
            if (vp.z <= 0f || vp.x < 0.03f || vp.x > 0.97f || vp.y < 0.03f || vp.y > 0.97f) continue;
            float sun = Vector3.Dot(n, sd);
            if (sun < 0.05f) { whyNot = T[i].name + " is on the night side right now. Wait for daylight!"; continue; }
            float center = 1f - Mathf.Clamp01(new Vector2(vp.x - 0.5f, vp.y - 0.5f).magnitude / 0.5f);
            float angR = T[i].radiusKm / Mathf.Max(1f, toCam.magnitude);                      // how big it looks (radians)
            float ratio = angR / (photoFov * 0.5f * Mathf.Deg2Rad);
            float fit = ratio < 0.3f ? ratio / 0.3f : ratio > 1.3f ? Mathf.Clamp01(1.3f / ratio) : 1f;
            float off = Vector3.Angle(satCam.transform.forward, -Radial());
            float nadir = 1f - Mathf.Clamp01((off - 20f) / 50f) * 0.6f;
            float light = Mathf.Clamp01(sun / 0.5f);
            float score = Mathf.Round(100f * (0.35f * center + 0.30f * fit + 0.15f * nadir + 0.20f * light));
            if (score > bestScore) { bestScore = score; best = i; }
        }
        if (best < 0) { StartCoroutine(FlashR(whyNot ?? "No target in the picture. Check the map and aim at one!", false)); return; }

        var prev = RenderTexture.active;
        RenderTexture.active = satRT;
        var tex = new Texture2D(satRT.width, satRT.height, TextureFormat.RGB24, false) { name = AscendEarth.Targets[best].name };
        tex.ReadPixels(new Rect(0, 0, satRT.width, satRT.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        done[best] = true;
        var ph = new Photo { tex = tex, score = bestScore, target = best, kind = AscendEarth.Targets[best].name, number = photos.Count + 1 };
        photos.Add(ph);
        thumbs[best].texture = tex; thumbs[best].color = Color.white;
        thumbLabels[best].text = $"{best + 1}  {AscendEarth.Targets[best].name.ToUpperInvariant()}  //  {bestScore:0}";
        thumbLabels[best].color = MissionUI.Go;
        StartCoroutine(FlashR($"{AscendEarth.Targets[best].name.ToUpperInvariant()}  -  {bestScore:0} / 100", true));
        if (photos.Count >= PhotoGoal) { finishing = true; StartCoroutine(Finish()); }
    }

    // ───────────── the end: rate, pay, report, back to the player ─────────────
    const float ParHours = 16f;          // mission time before the score starts to drop
    const float PenaltyPerHour = 3f;     // % taken off for every hour over par (max 45%)

    IEnumerator Finish()
    {
        yield return new WaitForSeconds(1.5f);
        phase = Phase.End;
        float avg = Mathf.Round(photos.Average(p => p.score));
        float hours = (float)missionHours;
        float penalty = Mathf.Clamp((hours - ParHours) * PenaltyPerHour, 0f, 45f);
        float overall = Mathf.Round(avg * (1f - penalty / 100f));
        string grade = Grade(overall);
        bool success = overall >= 50f;
        string interest; float share;
        if (overall >= 80f) { interest = "VERY HAPPY: they want more satellites like this!"; share = 1f; }
        else if (overall >= 65f) { interest = "HAPPY: good work, the program continues."; share = 0.5f; }
        else if (overall >= 50f) { interest = "OKAY: the photos are fine, but not great."; share = 0f; }
        else { interest = "NOT HAPPY: some of the money is taken back."; share = -0.6f; }
        double before = AscendEconomy.Balance;
        double change = Math.Round(before * 0.04 * share * (overall / 100.0) / 1e5) * 1e5;
        if (overall < 50f) change = -Math.Round(before * 0.03 * (1.0 - overall / 100.0) / 1e5) * 1e5;

        string sat = AscendOrbitTransfer.Payload != null ? AscendOrbitTransfer.Payload.name : "your satellite";
        var sb = new StringBuilder();
        sb.Append(success ? "<b><color=#81C995>MISSION SUCCESS!</color></b>\n\n" : "<b><color=#F28B82>MISSION FAILED</color></b>\n\n");
        sb.Append($"<b>{sat}</b> is in orbit {AscendOrbitTransfer.AltKm:N0} km above Earth. Here is how your photos did.\n\n");
        sb.Append("<b><color=#8AB4F8>YOUR PHOTOS</color></b>\n");
        foreach (var p in photos.OrderBy(p => p.target))
            sb.Append($"{p.target + 1}. {p.kind}: <b>{p.score:0}/100</b> ({Grade(p.score)})\n");
        sb.Append($"\nAverage photo score: <b>{avg:0}/100</b>\n");
        sb.Append($"\n<b><color=#8AB4F8>TIME</color></b>\nMission time: <b>{hours:0.0} hours</b> (goal: {ParHours:0} hours)\n");
        sb.Append(penalty > 0f ? $"<color=#F28B82>Too slow: -{penalty:0}% from your score</color>\n" : "<color=#81C995>On time: no points lost</color>\n");
        sb.Append($"\n<b><color=#8AB4F8>FINAL SCORE</color></b>\n<b>{overall:0}/100</b>  (grade {grade})\n");
        sb.Append($"\n<b><color=#8AB4F8>MONEY</color></b>\nThe government is {interest}\n");
        sb.Append($"Budget change: <b>{(change >= 0 ? "+" : "-")}{AscendReport.Money(Math.Abs(change))}</b>\n");
        sb.Append($"New balance: <b>{AscendReport.Money(Math.Max(0, before + change))}</b>\n\n");
        sb.Append("<b><color=#8AB4F8>TIPS</color></b>\n- Put the target in the middle of the picture.\n- Zoom so the target fills the picture.\n- Look straight down when you can.\n- Use the map to plan. Faster missions score more.\n\n");
        sb.Append("- ASCEND Mission Control");
        string subject = (success ? "MISSION REPORT: Success! " : "MISSION REPORT: Failed. ") + $"Score {overall:0}/100";
        string body = sb.ToString();

        yield return ReturnToPlayer(change, subject, body, success);
    }

    static string Grade(float s) => s >= 90f ? "S" : s >= 80f ? "A" : s >= 65f ? "B" : s >= 50f ? "C" : "D";

    // Fade out, give the player back their view and controls, then send the report
    IEnumerator ReturnToPlayer(double change, string subject, string body, bool success)
    {
        yield return FadeR(0f, 1f, 1.2f);
        if (console) console.SetActive(false);
        if (hud) hud.SetActive(false);
        RestoreScene();
        if (rig) Destroy(rig.gameObject);
        if (hdEarth) Destroy(hdEarth.gameObject);
        if (satCam) satCam.targetTexture = null;
        if (satRT) { satRT.Release(); satRT = null; }
        foreach (var c in new[] { spaceCam, rigCam, satCam }) if (c) c.enabled = false;
        yield return new WaitForSecondsRealtime(0.4f);

        AscendEconomy.Grant(change);
        string from = data ? data.agencyEmail : "ascend.philippines@phmail.com", to = data ? data.playerEmail : "design.team@phmail.com";
        AscendMail.Deliver(from, to, subject, body, success);
        MissionBriefing.Hint("Mission complete! Your report is in <b>PhMail</b>.\nPress <b>P</b> to read it.", 14f);

        yield return FadeR(1f, 0f, 1.2f);
        Destroy(gameObject);
    }

    // Everything the orbit changed in the Universe scene goes back
    bool restored;
    void RestoreScene()
    {
        if (restored) return;
        restored = true;
        foreach (var c in sceneCams) if (c) c.enabled = true;
        if (playerWasEnabled && PlayerController.Instance) PlayerController.Instance.enabled = true;
        if (sunLight) sunLight.transform.rotation = sunOriginal;
        foreach (var (l, mask) in litRig) if (l) l.cullingMask = mask;
        foreach (var ci in pausedComputers) if (ci) ci.enabled = true;
        RenderSettings.reflectionIntensity = savedReflect;
        MissionBriefing.SetUiVisible(true);
        AscendSky.ResetAltitude();
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    void OnDisable() => RestoreScene();

    // ═════════════════════════════════════════════════════════════════════════
    //  UI
    // ═════════════════════════════════════════════════════════════════════════
    TMP_FontAsset font => AUI.Font;

    void BuildUI()
    {
        var cgo = new GameObject("Ascend Orbit UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(transform, false);
        canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 700;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        if (!FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>())
        {
            var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }
        var root = (RectTransform)cgo.transform;

        // In-orbit HUD
        hud = Panel("Orbit HUD", root, new Vector2(560, 330), MissionUI.HudBg);
        var hrt = (RectTransform)hud.transform;
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0f, 1f); hrt.anchoredPosition = new Vector2(24, -24);
        MissionUI.Corners(hrt, MissionUI.Text);
        var hl = MissionUI.Lbl(hrt, "ORBIT TELEMETRY", 14);
        hl.color = MissionUI.Text;
        Place(hl.rectTransform, new Vector2(20, -14), new Vector2(520, 24), new Vector2(0f, 1f)); hl.alignment = TextAlignmentOptions.Left;
        MissionUI.HLine(hrt, 46f, MissionUI.LineDim, 14f);
        hudText = T(hrt, "", 18, Color.white);
        Place(hudText.rectTransform, new Vector2(20, -58), new Vector2(520, 262), new Vector2(0f, 1f)); hudText.alignment = TextAlignmentOptions.TopLeft;
        hudText.lineSpacing = 6f;

        bigText = T(root, "", 84, Color.white, FontStyles.Bold);
        bigText.characterSpacing = 8f;
        Place(bigText.rectTransform, new Vector2(0, 120), new Vector2(1700, 300), new Vector2(0.5f, 0.5f)); bigText.alignment = TextAlignmentOptions.Center;
        bigText.outlineWidth = 0.15f; bigText.outlineColor = new Color32(0, 0, 0, 230);

        BuildConsole(root);

        var f = new GameObject("Fade", typeof(RectTransform)).AddComponent<Image>();
        f.transform.SetParent(root, false); f.color = Color.black; f.raycastTarget = false;
        var frt = f.rectTransform; frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
        fade = f.gameObject.AddComponent<CanvasGroup>(); fade.alpha = 0f; fade.blocksRaycasts = false;
    }

    void BuildConsole(RectTransform root)
    {
        console = MissionUI.Solid(root, MissionUI.Bg, true, "Satellite Control").gameObject;
        MissionUI.Stretch((RectTransform)console.transform);
        var rt = MissionUI.Box(console.transform, "Console", MissionUI.Line, null, true);
        MissionUI.Place(rt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1860, 1020));
        MissionUI.Header(rt, "SATELLITE CONTROL", "ASCEND MISSION CONTROL   //   EARTH PHOTO MISSION", 64f);

        // Live view
        var lrt = MissionUI.Box(rt, "Live View Frame", MissionUI.Line, Color.black);
        MissionUI.Place(lrt, new Vector2(0f, 1f), new Vector2(22, -84), new Vector2(1296, 736));
        liveView = new GameObject("Live View", typeof(RectTransform)).AddComponent<RawImage>();
        liveView.transform.SetParent(lrt, false);
        Place(liveView.rectTransform, Vector2.zero, new Vector2(1280, 720), new Vector2(0.5f, 0.5f));
        liveView.transform.SetSiblingIndex(0);
        var live = T(lrt, "<color=#FF4444>REC</color>   LIVE SATELLITE CAMERA", 16, Color.white, FontStyles.Bold);
        live.characterSpacing = 6f;
        Place(live.rectTransform, new Vector2(24, -18), new Vector2(700, 30), new Vector2(0f, 1f)); live.alignment = TextAlignmentOptions.Left;
        var vf = MissionUI.Rect("Viewfinder", lrt); MissionUI.Stretch(vf, 40, 40, 40, 60);
        MissionUI.Corners(vf, new Color(1f, 1f, 1f, 0.6f), 40f, 2f);
        foreach (var (pos, sz) in new[] { (new Vector2(-24, 0), new Vector2(30, 1.5f)), (new Vector2(24, 0), new Vector2(30, 1.5f)),
                                          (new Vector2(0, -24), new Vector2(1.5f, 30)), (new Vector2(0, 24), new Vector2(1.5f, 30)) })
        {
            var c = MissionUI.Solid(lrt, new Color(1f, 1f, 1f, 0.7f), false, "Crosshair");
            Place(c.rectTransform, pos, sz, new Vector2(0.5f, 0.5f));
        }
        flash = new GameObject("Flash", typeof(RectTransform)).AddComponent<Image>();
        flash.transform.SetParent(lrt, false); flash.color = new Color(1f, 1f, 1f, 0f); flash.raycastTarget = false;
        Place(flash.rectTransform, Vector2.zero, new Vector2(1280, 720), new Vector2(0.5f, 0.5f));
        flashText = T(lrt, "", 30, Color.white, FontStyles.Bold);
        flashText.characterSpacing = 6f; flashText.outlineWidth = 0.2f; flashText.outlineColor = new Color32(0, 0, 0, 220);
        Place(flashText.rectTransform, new Vector2(0, -70), new Vector2(1200, 90), new Vector2(0.5f, 1f)); flashText.alignment = TextAlignmentOptions.Center;
        var keys = T(lrt, "W A S D  AIM      Q E  ROLL      SCROLL  ZOOM      <color=#FFC850>SPACE  TAKE PHOTO</color>", 15, MissionUI.Label, FontStyles.Bold);
        keys.characterSpacing = 4f;
        Place(keys.rectTransform, new Vector2(0, 16), new Vector2(1200, 30), new Vector2(0.5f, 0f)); keys.alignment = TextAlignmentOptions.Center;

        // Info column: map, targets, time, speed
        var info = MissionUI.Box(rt, "Mission", MissionUI.Line);
        MissionUI.Place(info, new Vector2(1f, 1f), new Vector2(-22, -84), new Vector2(498, 736));
        var il = MissionUI.Lbl(info, "WORLD MAP", 13);
        Place(il.rectTransform, new Vector2(16, -12), new Vector2(460, 24), new Vector2(0f, 1f)); il.alignment = TextAlignmentOptions.Left;
        var mapBox = MissionUI.Box(info, "Map", MissionUI.LineDim, Color.black);
        MissionUI.Place(mapBox, new Vector2(0f, 1f), new Vector2(16, -40), new Vector2(466, 233));
        mapRect = MissionUI.Rect("Map Image", mapBox); MissionUI.Stretch(mapRect, 1, 1, 1, 1);
        var mapImg = mapRect.gameObject.AddComponent<RawImage>(); mapImg.raycastTarget = false;
        mapImg.texture = hdEarth && hdEarth.surface ? hdEarth.surface.GetTexture("_DayTex") : null;
        for (int i = 0; i < AscendEarth.Targets.Length; i++)
        {
            var m = new GameObject("Target " + (i + 1), typeof(RectTransform)).AddComponent<Image>();
            m.transform.SetParent(mapRect, false); m.sprite = PhoneArt.Round; m.raycastTarget = false;
            m.rectTransform.sizeDelta = new Vector2(16, 16);
            var n = T(m.rectTransform, (i + 1).ToString(), 11, Color.black, FontStyles.Bold);
            Place(n.rectTransform, Vector2.zero, new Vector2(16, 16), new Vector2(0.5f, 0.5f)); n.alignment = TextAlignmentOptions.Center;
            targetDots.Add(m);
        }
        satDot = new GameObject("Satellite", typeof(RectTransform)).AddComponent<Image>();
        satDot.transform.SetParent(mapRect, false); satDot.sprite = PhoneArt.Round; satDot.color = new Color(1f, 0.25f, 0.2f); satDot.raycastTarget = false;
        satDot.rectTransform.sizeDelta = new Vector2(12, 12);
        var ring = MissionUI.Box(satDot.rectTransform, "View", new Color(1f, 0.3f, 0.25f, 0.8f));
        Place(ring, Vector2.zero, new Vector2(44, 44), new Vector2(0.5f, 0.5f));

        conCount = T(info, "", 18, Color.white, FontStyles.Bold);
        conCount.characterSpacing = 3f;
        Place(conCount.rectTransform, new Vector2(16, -284), new Vector2(466, 30), new Vector2(0f, 1f)); conCount.alignment = TextAlignmentOptions.Left;
        clockText = T(info, "", 15, MissionUI.Label, FontStyles.Bold);
        Place(clockText.rectTransform, new Vector2(16, -314), new Vector2(466, 26), new Vector2(0f, 1f)); clockText.alignment = TextAlignmentOptions.Left;
        MissionUI.HLine(info, 350f, MissionUI.LineDim, 14f);
        conInfo = T(info, "", 16, Color.white);
        Place(conInfo.rectTransform, new Vector2(16, -362), new Vector2(466, 290), new Vector2(0f, 1f)); conInfo.alignment = TextAlignmentOptions.TopLeft;
        conInfo.lineSpacing = 6f;

        var sl = MissionUI.Lbl(info, "SPEED", 13);
        Place(sl.rectTransform, new Vector2(16, 30), new Vector2(120, 40), new Vector2(0f, 0f)); sl.alignment = TextAlignmentOptions.MidlineLeft;
        // Speed slider: thin track, bright fill, outlined handle (1x - 5x in 0.5 steps)
        var sliderRt = MissionUI.Rect("Speed Slider", info);
        MissionUI.Place(sliderRt, new Vector2(0f, 0f), new Vector2(120, 42), new Vector2(250, 30));
        var track = MissionUI.Solid(sliderRt, MissionUI.LineDim, false, "Track").rectTransform;
        track.anchorMin = new Vector2(0f, 0.5f); track.anchorMax = new Vector2(1f, 0.5f); track.sizeDelta = new Vector2(0f, 3f); track.anchoredPosition = Vector2.zero;
        var hit = sliderRt.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
        var fillArea = MissionUI.Rect("Fill Area", sliderRt); MissionUI.Stretch(fillArea, 0, 13, 0, 13);
        var fill = MissionUI.Solid(fillArea, MissionUI.Accent, false, "Fill");
        fill.rectTransform.sizeDelta = Vector2.zero;
        var handleArea = MissionUI.Rect("Handle Area", sliderRt); MissionUI.Stretch(handleArea, 8, 0, 8, 0);
        var handle = MissionUI.Box(handleArea, "Handle", MissionUI.Text, Color.black, false, true);
        handle.sizeDelta = new Vector2(16f, 0f);
        speedSlider = sliderRt.gameObject.AddComponent<Slider>();
        speedSlider.fillRect = fill.rectTransform; speedSlider.handleRect = handle;
        speedSlider.targetGraphic = handle.GetComponent<Image>();
        speedSlider.direction = Slider.Direction.LeftToRight;
        speedSlider.minValue = MinSpeed; speedSlider.maxValue = MaxSpeed; speedSlider.value = speed;
        speedSlider.navigation = new Navigation { mode = Navigation.Mode.None };
        speedSlider.onValueChanged.AddListener(v => SetSpeed(v, false));
        speedLabel = T(info, "", 18, Color.white, FontStyles.Bold);
        Place(speedLabel.rectTransform, new Vector2(388, 42), new Vector2(90, 30), new Vector2(0f, 0f)); speedLabel.alignment = TextAlignmentOptions.MidlineLeft;

        // Photo strip: one slot per target
        for (int i = 0; i < PhotoGoal; i++)
        {
            var srt = MissionUI.Box(rt, "Photo Slot", MissionUI.LineDim);
            Place(srt, new Vector2(22 + i * 256, 54), new Vector2(240, 135), new Vector2(0f, 0f));
            var img = new GameObject("Photo", typeof(RectTransform)).AddComponent<RawImage>();
            img.transform.SetParent(srt, false); img.color = new Color(1f, 1f, 1f, 0f);
            Place(img.rectTransform, Vector2.zero, new Vector2(236, 131), new Vector2(0.5f, 0.5f));
            var lab = T(srt, $"{i + 1}  {AscendEarth.Targets[i].name.ToUpperInvariant()}", 12, MissionUI.Label);
            lab.characterSpacing = 2f;
            Place(lab.rectTransform, new Vector2(0, -26), new Vector2(256, 22), new Vector2(0.5f, 0f)); lab.alignment = TextAlignmentOptions.Center;
            thumbs.Add(img); thumbLabels.Add(lab);
        }
        console.SetActive(false);
    }

    RectTransform mapRect;
    Image satDot;
    readonly List<Image> targetDots = new List<Image>();
    Slider speedSlider;
    TextMeshProUGUI speedLabel;

    void SetSpeed(float v, bool moveSlider = true)
    {
        speed = Mathf.Clamp(Mathf.Round(v * 2f) / 2f, MinSpeed, MaxSpeed);   // steps of 0.5x
        if (moveSlider && speedSlider) speedSlider.SetValueWithoutNotify(speed);
    }
    TextMeshProUGUI clockText;

    Vector2 MapPos(float lat, float lon) => new Vector2((lon + 180f) / 360f * mapRect.rect.width, (lat + 90f) / 180f * mapRect.rect.height);

    void UpdateConsoleUI()
    {
        if (!hdEarth) return;
        var T = AscendEarth.Targets;
        Vector3 sd = SunDir();
        hdEarth.LatLonOf(Radial(), out float sLat, out float sLon);
        satDot.rectTransform.anchorMin = satDot.rectTransform.anchorMax = Vector2.zero;
        satDot.rectTransform.anchoredPosition = MapPos(sLat, sLon);
        var sb = new StringBuilder();
        Vector3 subDir = Radial();
        for (int i = 0; i < T.Length; i++)
        {
            Vector3 d = hdEarth.WorldDir(T[i].lat, T[i].lon);
            bool day = Vector3.Dot(d, sd) > 0.05f;
            float km = Vector3.Angle(subDir, d) * Mathf.Deg2Rad * AscendEarth.RadiusKm;
            var dot = targetDots[i];
            dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = Vector2.zero;
            dot.rectTransform.anchoredPosition = MapPos(T[i].lat, T[i].lon);
            dot.color = done[i] ? MissionUI.Go : day ? MissionUI.Caution : new Color(0.55f, 0.65f, 0.85f);
            sb.Append($"<b>{i + 1}  {T[i].name}</b>\n<size=85%>");
            if (done[i]) { var p = photos.FirstOrDefault(x => x.target == i); sb.Append($"<color=#3FB950>DONE  {(p != null ? p.score : 0):0}/100</color>"); }
            else sb.Append($"<color=#9AA0A6>{km:N0} km away   </color>" + (day ? "<color=#FFD166>DAY</color>" : "<color=#8CB4E6>NIGHT</color>") + (km < 1800f && day ? "   <color=#3FB950>IN RANGE</color>" : ""));
            sb.Append("</size>\n");
        }
        conInfo.text = sb.ToString();
        conCount.text = $"PHOTOS  {photos.Count} / {PhotoGoal}";
        float hrs = (float)missionHours;
        bool late = hrs > ParHours;
        clockText.text = $"MISSION TIME  <color={(late ? "#F28B82" : "#FFFFFF")}>{(int)hrs:00}:{(int)((hrs % 1f) * 60f):00} h</color>   GOAL {ParHours:0} h" + (late ? $"   <color=#F28B82>-{Mathf.Min(45f, (hrs - ParHours) * PenaltyPerHour):0}%</color>" : "");
        if (speedLabel) speedLabel.text = speed.ToString("0.0") + "x";
    }

    void UpdateHud()
    {
        double v = Math.Sqrt(Mu / rM), period = 2 * Math.PI / omega;
        if (phase == Phase.Arrive || phase == Phase.Release)
        {
            hudText.text =
                $"{L("STATUS")}<color=#3FB950>NOMINAL ORBIT</color>\n" +
                $"{L("ALTITUDE")}<b>{AscendOrbitTransfer.AltKm:N0} km</b>\n" +
                $"{L("VELOCITY")}<b>{v / 1000:0.00} km/s</b>  <size=75%><color=#9AA0A6>{v * 3.6:N0} km/h</color></size>\n" +
                $"{L("ORBIT")}<b>{period / 60 * 49.5:0} min</b>  <size=75%><color=#9AA0A6>(real time)</color></size>\n" +
                $"{L("TIME RATE")}<b>{speed:0.0}x</b>  <size=75%><color=#9AA0A6>(- / +)</color></size>\n\n" +
                (phase == Phase.Arrive ? "<color=#FFC850><b>[ SPACE ]  RELEASE SATELLITE</b></color>\n" : "<color=#3FB950>SATELLITE RELEASED</color>\n") +
                "<size=75%><color=#9AA0A6>RMB  LOOK AROUND     SCROLL  ZOOM     - / +  SPEED</color></size>";
        }
    }

    // Grey label column for readouts
    static string L(string label) => $"<color=#9AA0A6><size=75%>{label}</size></color><pos=42%>";

    void Big(string s, float sec) { if (bigText) { bigText.text = s; bigUntil = Time.time + sec; } }

    int flashId;
    IEnumerator FlashR(string msg, bool shutter)
    {
        int id = ++flashId;
        flashText.text = msg;
        float t = 0f;
        while (shutter && t < 0.5f) { t += Time.deltaTime; flash.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.85f, 0f, t / 0.5f)); yield return null; }
        flash.color = new Color(1f, 1f, 1f, 0f);
        yield return new WaitForSeconds(2.2f);
        if (id == flashId) flashText.text = "";
    }

    IEnumerator FadeR(float from, float to, float sec)
    {
        float t = 0f;
        while (t < sec) { t += Time.unscaledDeltaTime; if (fade) fade.alpha = Mathf.Lerp(from, to, t / sec); yield return null; }
        if (fade) fade.alpha = to;
    }

    GameObject Panel(string n, Transform parent, Vector2 size, Color c)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = c; img.raycastTarget = c.a > 0f;
        var rt = (RectTransform)go.transform;
        MissionUI.Border(rt, c.a > 0.5f ? MissionUI.Line : MissionUI.LineDim);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = size;
        return go;
    }

    TextMeshProUGUI T(Transform parent, string s, float size, Color c, FontStyles st = FontStyles.Normal)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font) t.font = font;
        t.text = s; t.fontSize = size; t.color = c; t.fontStyle = st; t.raycastTarget = false; t.richText = true;
        return t;
    }

    static void Place(RectTransform r, Vector2 pos, Vector2 size, Vector2 anchor)
    {
        r.anchorMin = r.anchorMax = r.pivot = anchor; r.sizeDelta = size; r.anchoredPosition = pos;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  INPUT (old Input Manager and new Input System)
    // ═════════════════════════════════════════════════════════════════════════
#if ENABLE_INPUT_SYSTEM
    static KeyControl K(KeyCode k)
    {
        var kb = Keyboard.current;
        if (kb == null) return null;
        string n = k.ToString();
        if (n.StartsWith("Alpha")) n = "Digit" + n.Substring(5);
        return Enum.TryParse(n, out Key key) && key != Key.None ? kb[key] : null;
    }
    static bool KeyDown(KeyCode k) { var c = K(k); return c != null && c.wasPressedThisFrame; }
    static bool Held(KeyCode k) { var c = K(k); return c != null && c.isPressed; }
    static bool RightMouseHeld() => Mouse.current != null && Mouse.current.rightButton.isPressed;
    static Vector2 MouseDelta() => Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.05f : Vector2.zero;
    static float Scroll() => Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
    static bool KeyDown(KeyCode k) => Input.GetKeyDown(k);
    static bool Held(KeyCode k) => Input.GetKey(k);
    static bool RightMouseHeld() => Input.GetMouseButton(1);
    static Vector2 MouseDelta() => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 0.5f;
    static float Scroll() => Input.mouseScrollDelta.y;
#endif
}
