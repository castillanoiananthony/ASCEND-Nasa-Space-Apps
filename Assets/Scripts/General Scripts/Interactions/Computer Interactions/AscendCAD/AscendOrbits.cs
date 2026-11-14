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
    public static string ReturnScene = "Hangar";

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
    static readonly float[] Warps = { 1f, 2f, 5f, 10f };

    enum Phase { Arrive, Release, Console, End }
    Phase phase = Phase.Arrive;

    AscendCADData data;
    Transform earth;
    float earthR = 0.637f, unitsPerM;
    Light sunLight, rigSun;
    Transform sunObj;

    // orbit
    double rM, omega, theta;
    Vector3 axisX, axisY = Vector3.up;
    int warpIndex;

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

    class Photo { public Texture2D tex; public float score, coverage, light, detail; public bool centered; public string kind; public int number; }
    readonly List<Photo> photos = new List<Photo>();
    const int PhotoGoal = 5;

    // ═════════════════════════════════════════════════════════════════════════
    bool ready;
    GameObject cover;

    void Awake()
    {
        cover = new GameObject("Orbit Cover", typeof(RectTransform), typeof(Canvas));
        var c = cover.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 999;
        var img = new GameObject("Black", typeof(RectTransform)).AddComponent<Image>();
        img.transform.SetParent(cover.transform, false); img.color = Color.black;
        var r = img.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
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
        omega = Math.Sqrt(Mu / (rM * rM * rM));
        Vector3 sd = SunDir();
        axisX = Vector3.ProjectOnPlane(sd, axisY).normalized;
        if (axisX.sqrMagnitude < 0.5f) axisX = Vector3.right;
        theta = -0.35;                        // start just before the sunny middle of the day side

        SetupCameras();
        BuildModels();
        BuildUI();
        arriveTime = Time.time;
        ready = true;
        if (cover) Destroy(cover);
    }

    void OnDestroy()
    {
        if (satRT) satRT.Release();
    }

    // ───────────── find things in the scene ─────────────
    void FindEarth()
    {
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (mb && mb.GetType().Name.ToLowerInvariant() == "earthbehaviour") { earth = mb.transform; break; }
        if (!earth) { var g = GameObject.Find("Earth"); if (g) earth = g.transform; }
        if (!earth)
        {
            // No Earth in this scene: make a simple one so the mission still works
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            g.name = "Earth (made by AscendOrbit)";
            Destroy(g.GetComponent<Collider>());
            g.transform.localScale = Vector3.one * 1.2742f;
            var r = g.GetComponent<Renderer>();
            r.material.color = new Color(0.15f, 0.35f, 0.7f);
            earth = g.transform;
            Debug.LogWarning("AscendOrbit: no Earth found (earthBehaviour or an object named Earth). Made a simple one.");
        }
        var mf = earth.GetComponentInChildren<MeshFilter>();
        float meshR = mf && mf.sharedMesh ? mf.sharedMesh.bounds.extents.x : 0.5f;
        earthR = Mathf.Max(1e-4f, meshR * (mf ? mf.transform.lossyScale.x : earth.lossyScale.x));
        unitsPerM = earthR / (float)EarthRm;

        // Seen from 700 km the default sphere's corners show on the horizon: swap in a smooth one (same texture layout)
        if (mf && mf.sharedMesh)
        {
            try { mf.sharedMesh = SmoothSphere(mf.sharedMesh, meshR); }
            catch (Exception e) { Debug.LogWarning("AscendOrbit: smooth Earth failed: " + e.Message); }
            BuildAtmosphere(mf.transform, meshR);
        }
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
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant().Contains("sun") && !t.name.ToLowerInvariant().Contains("sunlight") && t != earth) { sunObj = t; break; }
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional && l.enabled) { sunLight = l; break; }
    }

    Vector3 SunDir()
    {
        if (sunObj && (sunObj.position - earth.position).sqrMagnitude > 1f) return (sunObj.position - earth.position).normalized;
        if (sunLight) return -sunLight.transform.forward;
        return Vector3.right;
    }

    // Keep the Earth at the world's center (big numbers make things shake)
    void Recenter()
    {
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
        spaceCam.cullingMask = ~(1 << RigLayer);
        spaceCam.nearClipPlane = 0.0008f; spaceCam.farClipPlane = 100000f;
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
    Vector3 SatPos() => (axisX * (float)Math.Cos(theta) + axisY * (float)Math.Sin(theta)) * (float)(rM * unitsPerM);
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
        if (KeyDown(KeyCode.Minus)) warpIndex = Mathf.Max(0, warpIndex - 1);
        if (KeyDown(KeyCode.Equals)) warpIndex = Mathf.Min(Warps.Length - 1, warpIndex + 1);
        float dt = Time.deltaTime * Warps[warpIndex];

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
        if (phase == Phase.Console) ConsoleUpdate();

        if (bigText && Time.time > bigUntil) bigText.text = "";
        UpdateHud();
    }

    void LateUpdate()
    {
        if (!ready) return;
        Recenter();
        Vector3 sd = SunDir();
        if (rigSun) rigSun.transform.rotation = Quaternion.LookRotation(-sd);

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

        UpdateAtmosphere(phase == Phase.Console && satCam ? satCam.transform.position : spaceCam.transform.position);

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
        satRT = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { name = "Satellite Camera", antiAliasing = 2 };
        satCam = new GameObject("Satellite Photo Camera").AddComponent<Camera>();
        satCam.transform.SetParent(transform, false);
        satCam.CopyFrom(spaceCam);
        satCam.cullingMask = ~(1 << RigLayer);
        satCam.targetTexture = satRT;
        satCam.depth = 5f;
        satCam.nearClipPlane = 0.0005f; satCam.farClipPlane = 100000f;
        try { var d = satCam.GetUniversalAdditionalCameraData(); d.renderType = CameraRenderType.Base; d.cameraStack.Clear(); d.renderPostProcessing = true; } catch (Exception) { }
        photoPitch = photoYaw = photoRoll = 0f; photoFov = 40f;
        liveView.texture = satRT;
        hud.SetActive(false);
        console.SetActive(true);
    }

    void ConsoleUpdate()
    {
        float dt = Time.deltaTime;
        float turn = 30f * dt * Mathf.Lerp(0.25f, 1f, photoFov / 60f);
        if (Held(KeyCode.W)) photoPitch -= turn;
        if (Held(KeyCode.S)) photoPitch += turn;
        if (Held(KeyCode.A)) photoYaw -= turn;
        if (Held(KeyCode.D)) photoYaw += turn;
        if (Held(KeyCode.Q)) photoRoll += 40f * dt;
        if (Held(KeyCode.E)) photoRoll -= 40f * dt;
        if (Held(KeyCode.Z)) photoFov -= 20f * dt;
        if (Held(KeyCode.X)) photoFov += 20f * dt;
        photoFov = Mathf.Clamp(photoFov * (1f - Scroll() * 0.08f), 4f, 75f);
        photoPitch = Mathf.Clamp(photoPitch, -88f, 88f);
        photoYaw = Mathf.Clamp(photoYaw, -88f, 88f);
        if (KeyDown(KeyCode.Space) && photos.Count < PhotoGoal) TakePhoto();
    }

    void TakePhoto()
    {
        var prev = RenderTexture.active;
        RenderTexture.active = satRT;
        var tex = new Texture2D(satRT.width, satRT.height, TextureFormat.RGB24, false) { name = "Photo " + (photos.Count + 1) };
        tex.ReadPixels(new Rect(0, 0, satRT.width, satRT.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        var ph = Rate(tex);
        ph.number = photos.Count + 1;
        photos.Add(ph);
        int i = photos.Count - 1;
        thumbs[i].texture = tex; thumbs[i].color = Color.white;
        thumbLabels[i].text = $"#{ph.number}  {ph.kind}";
        StartCoroutine(FlashR($"PHOTO {photos.Count} OF {PhotoGoal}"));
        if (photos.Count >= PhotoGoal) StartCoroutine(Finish());
    }

    // Rate a photo by what the camera really sees: how much of the frame is Earth, how much of it is sunlit,
    // whether the middle is on the Earth, and how far it is zoomed in (more detail)
    Photo Rate(Texture2D tex)
    {
        int nx = 48, ny = 27, hits = 0, lit = 0;
        Vector3 sd = SunDir(), c = earth.position;
        for (int y = 0; y < ny; y++)
        for (int x = 0; x < nx; x++)
        {
            var ray = satCam.ViewportPointToRay(new Vector3((x + 0.5f) / nx, (y + 0.5f) / ny, 0f));
            if (HitEarth(ray, c, earthR, out Vector3 p)) { hits++; if (Vector3.Dot((p - c).normalized, sd) > 0.05f) lit++; }
        }
        var ph = new Photo { tex = tex };
        ph.coverage = hits / (float)(nx * ny);
        ph.light = hits > 0 ? lit / (float)hits : 0f;
        var mid = satCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        ph.centered = HitEarth(mid, c, earthR, out Vector3 mp) && Vector3.Dot((mp - c).normalized, sd) > 0.05f;
        ph.detail = Mathf.InverseLerp(60f, 6f, photoFov);

        if (hits == 0) { ph.score = 5f; ph.kind = "Empty space"; return ph; }
        float cover = Mathf.Clamp01(ph.coverage / 0.85f);
        ph.score = 100f * (0.35f * cover + 0.30f * ph.light * cover + 0.15f * (ph.centered ? 1f : 0f) + 0.20f * ph.detail);
        if (ph.light < 0.2f) ph.kind = "Night side";
        else if (ph.coverage < 0.85f) ph.kind = "Horizon view";
        else if (ph.detail > 0.6f) ph.kind = "Close-up";
        else ph.kind = "Earth view";
        if (ph.coverage < 0.85f && ph.coverage > 0.25f && ph.light > 0.6f) ph.score = Mathf.Min(100f, ph.score + 8f);   // a sunlit horizon is beautiful
        ph.score = Mathf.Round(Mathf.Clamp(ph.score, 0f, 100f));
        return ph;
    }

    static bool HitEarth(Ray r, Vector3 c, float rad, out Vector3 p)
    {
        Vector3 oc = r.origin - c;
        float b = Vector3.Dot(oc, r.direction), cc = oc.sqrMagnitude - rad * rad, disc = b * b - cc;
        p = Vector3.zero;
        if (disc < 0f) return false;
        float t = -b - Mathf.Sqrt(disc);
        if (t < 0f) return false;
        p = r.origin + r.direction * t;
        return true;
    }

    // ───────────── the end: rate, rank, pay, report ─────────────
    IEnumerator Finish()
    {
        yield return new WaitForSeconds(1.2f);
        phase = Phase.End;
        var ranked = photos.OrderByDescending(p => p.score).ToList();
        float overall = Mathf.Round(photos.Average(p => p.score));
        string grade = Grade(overall);
        string interest; float share;
        if (overall >= 80f) { interest = "VERY INTERESTED: they want more satellites like this!"; share = 1f; }
        else if (overall >= 65f) { interest = "INTERESTED: good work, the program continues."; share = 0.5f; }
        else if (overall >= 50f) { interest = "UNSURE: the photos are okay, but not great."; share = 0f; }
        else { interest = "NOT IMPRESSED: some of the budget is taken back."; share = -0.6f; }
        double before = AscendEconomy.Balance;
        double change = Math.Round(before * 0.04 * share * (overall / 100.0) / 1e5) * 1e5;
        if (overall < 50f) change = -Math.Round(before * 0.03 * (1.0 - overall / 100.0) / 1e5) * 1e5;
        AscendEconomy.Grant(change);

        // PhMail report
        string mission = AscendSimPlan.Title(AscendOrbitTransfer.Mission);
        string sat = AscendOrbitTransfer.Payload != null ? AscendOrbitTransfer.Payload.name : "your satellite";
        var sb = new StringBuilder();
        sb.Append("Hello Design Team,\n\n");
        sb.Append($"<b>{sat}</b> is working in orbit {AscendOrbitTransfer.AltKm:N0} km above Earth! Our image analysts rated the first {PhotoGoal} photos.\n\n");
        sb.Append("<b>PHOTO RANKING</b>\n");
        for (int i = 0; i < ranked.Count; i++)
        {
            var p = ranked[i];
            sb.Append($"{i + 1}. Photo #{p.number} - {p.kind}: <b>{p.score:0}/100</b> ({Grade(p.score)})  " +
                      $"<color=#9AA0A6>Earth in frame {p.coverage * 100f:0}%, sunlit {p.light * 100f:0}%, {(p.centered ? "centered" : "off-center")}, zoom {p.detail * 100f:0}%</color>\n");
        }
        sb.Append($"\n<b>OVERALL:</b> {overall:0}/100 (grade {grade})\n");
        sb.Append($"<b>GOVERNMENT INTEREST:</b> {interest}\n");
        sb.Append($"<b>BUDGET:</b> {(change >= 0 ? "+" : "-")}{AscendReport.Money(Math.Abs(change))}  (new balance {AscendReport.Money(AscendEconomy.Balance)})\n\n");
        sb.Append("<b>TIPS FOR BETTER PHOTOS</b>\n- Fill the frame with the sunlit Earth (W A S D to aim).\n- Zoom in (scroll or Z) for more detail.\n- A sunlit horizon with a bit of space looks beautiful.\n- Avoid the night side and empty space.\n\n");
        sb.Append("Congratulations on completing the mission!\n\n- Ascend Philippines Mission Control");
        string from = data ? data.agencyEmail : "ascend.philippines@phmail.com", to = data ? data.playerEmail : "design.team@phmail.com";
        AscendMail.Deliver(from, to, $"MISSION REPORT: {mission} photos - grade {grade}", sb.ToString(), overall >= 50f);

        ShowEnd(ranked, overall, grade, interest, change);
    }

    static string Grade(float s) => s >= 90f ? "S" : s >= 80f ? "A" : s >= 65f ? "B" : s >= 50f ? "C" : "D";

    void ShowEnd(List<Photo> ranked, float overall, string grade, string interest, double change)
    {
        console.SetActive(false);
        endCard = Panel("Mission Complete", canvas.transform, new Vector2(1500, 900), new Color(0.04f, 0.2f, 0.52f, 0.98f));
        var rt = (RectTransform)endCard.transform;
        var title = T(rt, "MISSION COMPLETE", 50, Color.white, FontStyles.Bold);
        Place(title.rectTransform, new Vector2(0, -30), new Vector2(1400, 70), new Vector2(0.5f, 1f)); title.alignment = TextAlignmentOptions.Center;
        var sub = T(rt, $"Overall <b>{overall:0}/100</b>  (grade {grade})", 30, new Color(1f, 0.85f, 0.4f));
        Place(sub.rectTransform, new Vector2(0, -105), new Vector2(1400, 44), new Vector2(0.5f, 1f)); sub.alignment = TextAlignmentOptions.Center;

        for (int i = 0; i < ranked.Count; i++)
        {
            var p = ranked[i];
            var card = Panel("Photo", rt, new Vector2(270, 260), new Color(0f, 0f, 0f, 0.25f));
            var crt = (RectTransform)card.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = new Vector2((i - (ranked.Count - 1) * 0.5f) * 285f, -170f);
            var img = new GameObject("Picture", typeof(RectTransform)).AddComponent<RawImage>();
            img.transform.SetParent(crt, false); img.texture = p.tex;
            Place(img.rectTransform, new Vector2(0, -10), new Vector2(250, 141), new Vector2(0.5f, 1f));
            var lab = T(crt, $"<b>#{i + 1}</b>  {p.score:0}/100  ({Grade(p.score)})\n<size=80%>Photo {p.number}: {p.kind}</size>", 20, Color.white);
            Place(lab.rectTransform, new Vector2(0, -158), new Vector2(255, 90), new Vector2(0.5f, 1f)); lab.alignment = TextAlignmentOptions.Top;
        }

        var body = T(rt, $"<b>Government interest:</b> {interest}\n<b>Budget:</b> {(change >= 0 ? "<color=#3FB950>+" : "<color=#F85149>-")}{AscendReport.Money(Math.Abs(change))}</color>   " +
                         $"New balance {AscendReport.Money(AscendEconomy.Balance)}\n\n<size=85%><color=#C9D8F0>The full report with the photo ranking was sent to your phone (PhMail).</color></size>", 26, Color.white);
        Place(body.rectTransform, new Vector2(0, -470), new Vector2(1380, 200), new Vector2(0.5f, 1f)); body.alignment = TextAlignmentOptions.Top;

        if (font && !AUI.Font) AUI.Font = font;
        var btn = AUI.Btn(rt, "RETURN TO MISSION CONTROL", 28, Color.white, new Color(0.86f, 0.2f, 0.16f), ReturnHome, 620, 80);
        var brt = (RectTransform)btn.transform; brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0f);
        brt.sizeDelta = new Vector2(620, 80); brt.anchoredPosition = new Vector2(0, 40);
    }

    void ReturnHome()
    {
        string sc = AscendOrbitTransfer.ReturnScene;
        if (!string.IsNullOrEmpty(sc) && Application.CanStreamedLevelBeLoaded(sc)) SceneManager.LoadScene(sc);
        else Debug.LogWarning("AscendOrbit: can't load the launch scene '" + sc + "'. Add it to File > Build Profiles (Scene List).");
    }

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
        hud = Panel("Orbit HUD", root, new Vector2(560, 300), new Color(0.02f, 0.07f, 0.16f, 0.9f));
        var hrt = (RectTransform)hud.transform;
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0f, 1f); hrt.anchoredPosition = new Vector2(24, -24);
        hudText = T(hrt, "", 22, Color.white);
        Place(hudText.rectTransform, Vector2.zero, new Vector2(520, 280), new Vector2(0.5f, 0.5f)); hudText.alignment = TextAlignmentOptions.TopLeft;
        hudText.lineSpacing = 6f;

        bigText = T(root, "", 90, Color.white, FontStyles.Bold);
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
        console = Panel("Satellite Control", root, new Vector2(1860, 1020), new Color(0.04f, 0.2f, 0.52f, 0.99f));
        var rt = (RectTransform)console.transform;
        var title = T(rt, "SATELLITE CONTROL", 34, Color.white, FontStyles.Bold);
        Place(title.rectTransform, new Vector2(30, -18), new Vector2(900, 50), new Vector2(0f, 1f)); title.alignment = TextAlignmentOptions.Left;

        // Live view
        var frame = Panel("Live View Frame", rt, new Vector2(1296, 736), new Color(0f, 0f, 0f, 1f));
        var lrt = (RectTransform)frame.transform; lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0f, 1f); lrt.anchoredPosition = new Vector2(22, -80);
        liveView = new GameObject("Live View", typeof(RectTransform)).AddComponent<RawImage>();
        liveView.transform.SetParent(lrt, false);
        Place(liveView.rectTransform, Vector2.zero, new Vector2(1280, 720), new Vector2(0.5f, 0.5f));
        var live = T(lrt, "<color=#FF4444>REC</color>  LIVE SATELLITE CAMERA", 20, Color.white, FontStyles.Bold);
        Place(live.rectTransform, new Vector2(20, -14), new Vector2(600, 30), new Vector2(0f, 1f)); live.alignment = TextAlignmentOptions.Left;
        // crosshair
        foreach (var sz in new[] { new Vector2(60, 2), new Vector2(2, 60) })
        {
            var c = new GameObject("Crosshair", typeof(RectTransform)).AddComponent<Image>();
            c.transform.SetParent(lrt, false); c.color = new Color(1f, 1f, 1f, 0.6f); c.raycastTarget = false;
            Place(c.rectTransform, Vector2.zero, sz, new Vector2(0.5f, 0.5f));
        }
        flash = new GameObject("Flash", typeof(RectTransform)).AddComponent<Image>();
        flash.transform.SetParent(lrt, false); flash.color = new Color(1f, 1f, 1f, 0f); flash.raycastTarget = false;
        Place(flash.rectTransform, Vector2.zero, new Vector2(1280, 720), new Vector2(0.5f, 0.5f));
        flashText = T(lrt, "", 40, Color.white, FontStyles.Bold);
        Place(flashText.rectTransform, new Vector2(0, -60), new Vector2(1000, 60), new Vector2(0.5f, 1f)); flashText.alignment = TextAlignmentOptions.Center;

        // Info column
        conCount = T(rt, "", 40, Color.white, FontStyles.Bold);
        Place(conCount.rectTransform, new Vector2(-30, -80), new Vector2(500, 60), new Vector2(1f, 1f)); conCount.alignment = TextAlignmentOptions.Left;
        conInfo = T(rt, "", 22, Color.white);
        Place(conInfo.rectTransform, new Vector2(-30, -150), new Vector2(500, 660), new Vector2(1f, 1f)); conInfo.alignment = TextAlignmentOptions.TopLeft;
        conInfo.lineSpacing = 8f;

        // Photo strip
        for (int i = 0; i < PhotoGoal; i++)
        {
            var slot = Panel("Photo Slot", rt, new Vector2(240, 135), new Color(0f, 0f, 0f, 0.35f));
            var srt = (RectTransform)slot.transform; srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 0f);
            srt.anchoredPosition = new Vector2(22 + i * 256, 54);
            var img = new GameObject("Photo", typeof(RectTransform)).AddComponent<RawImage>();
            img.transform.SetParent(srt, false); img.color = new Color(1f, 1f, 1f, 0f);
            Place(img.rectTransform, Vector2.zero, new Vector2(240, 135), new Vector2(0.5f, 0.5f));
            var lab = T(srt, $"Photo {i + 1}", 16, new Color(1f, 1f, 1f, 0.8f));
            Place(lab.rectTransform, new Vector2(0, -24), new Vector2(240, 24), new Vector2(0.5f, 0f)); lab.alignment = TextAlignmentOptions.Center;
            thumbs.Add(img); thumbLabels.Add(lab);
        }
        console.SetActive(false);
    }

    void UpdateHud()
    {
        double v = Math.Sqrt(Mu / rM), period = 2 * Math.PI / omega;
        if (phase == Phase.Arrive || phase == Phase.Release)
        {
            hudText.text =
                "<color=#59D9FF><b>IN ORBIT</b></color>\n" +
                $"Height  <b>{AscendOrbitTransfer.AltKm:N0} km</b>\n" +
                $"Speed  <b>{v * 3.6:N0} km/h</b>  <size=80%>({v / 1000:0.00} km/s)</size>\n" +
                $"One lap around Earth  <b>{period / 60:0} min</b>\n" +
                $"Time speed  <b>{Warps[warpIndex]:0}x</b>  <size=80%>(- / +)</size>\n\n" +
                (phase == Phase.Arrive ? "<color=#FFC850><b>SPACE: release the satellite</b></color>\n" : "<color=#3FB950>Satellite released</color>\n") +
                "<size=80%><color=#9AA0A6>Hold right mouse: look around   Scroll: zoom</color></size>";
        }
        if (phase == Phase.Console)
        {
            bool day = Vector3.Dot(Radial(), SunDir()) > 0f;
            conCount.text = $"PHOTOS  {photos.Count} / {PhotoGoal}";
            conInfo.text =
                $"<b>{(AscendOrbitTransfer.Payload != null ? AscendOrbitTransfer.Payload.name : "Satellite")}</b>\n" +
                $"Height  {AscendOrbitTransfer.AltKm:N0} km\nSpeed  {v / 1000:0.00} km/s\nBelow  {(day ? "<color=#FFD166>daytime</color>" : "<color=#8CB4E6>night</color>")}\n" +
                $"Zoom  {Mathf.InverseLerp(75f, 4f, photoFov) * 100f:0}%\n\n" +
                "<b>CONTROLS</b>\nW A S D  turn the camera\nQ E  roll\nScroll or Z X  zoom\n<color=#FFC850><b>SPACE  take a photo</b></color>\n\n" +
                "<b>TIPS</b>\n<size=85%>Fill the frame with the sunlit Earth, zoom in for detail, or catch a sunlit horizon. Photos of empty space or the night side score low.</size>";
        }
    }

    void Big(string s, float sec) { if (bigText) { bigText.text = s; bigUntil = Time.time + sec; } }

    IEnumerator FlashR(string msg)
    {
        flashText.text = msg;
        float t = 0f;
        while (t < 0.5f) { t += Time.deltaTime; flash.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.85f, 0f, t / 0.5f)); yield return null; }
        flash.color = new Color(1f, 1f, 1f, 0f);
        yield return new WaitForSeconds(1f);
        flashText.text = "";
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
        img.color = c; img.sprite = AUI.Rounded; img.type = Image.Type.Sliced; img.raycastTarget = true;
        var rt = (RectTransform)go.transform;
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
