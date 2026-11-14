// ═════════════════════════════════════════════════════════════════════════════
//  AscendFlight.cs  -  launch the approved rocket with real physics
//
//  Put "AscendLaunchController" on your launch cube (it needs a Collider).
//  Look at the cube and press E: countdown, ignition, liftoff.
//
//  Real physics, using the real numbers from the AscendCAD part catalog:
//    - thrust changes with air pressure (sea level -> vacuum), propellant burns away so the rocket gets lighter
//    - gravity weakens with height, air gets thinner with height (density + speed of sound)
//    - drag from the rocket's real width, changing with Mach number
//    - side boosters burn with the core and drop off when empty, stages separate at burnout,
//      the fairing opens high up
//    - a gravity turn: straight up, small pitch kick, then follow the flight path
//  Ways to fail (just like real rockets):
//    - too heavy to leave the pad (thrust-to-weight below 1)
//    - air pressure too high (Max Q) -> breaks apart
//    - no guidance (flight computer + IMU) -> tumbles sideways -> ripped apart
//    - too much g-force -> crushed
//    - out of fuel before space -> falls back and crashes
//  Reaching 100 km (Karman line) = SPACE: the screen fades and the rocket vanishes (next scene comes later).
//
//  Keys while flying: C = camera, 1-4 = time speed, mouse = look around, scroll = zoom.
// ═════════════════════════════════════════════════════════════════════════════
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Object = UnityEngine.Object;

public class AscendLaunchController : MonoBehaviour
{
    [Header("Launch cube (this object needs a Collider)")]
    public KeyCode launchKey = KeyCode.E;
    public float interactDistance = 5f;

    [Header("Data")]
    [Tooltip("Your AscendCAD Data asset. Leave empty to use the one on the computer.")]
    public AscendCADData ascendCADData;

    [Header("Flight")]
    public float countdownSeconds = 10f;
    [Tooltip("World direction the rocket leans toward after liftoff.")]
    public Vector3 downrangeDirection = new Vector3(1f, 0f, 0f);
    [Tooltip("Height where space begins (Karman line).")]
    public float karmanLineKm = 100f;
    [Tooltip("Height where the payload fairing opens.")]
    public float fairingJettisonKm = 85f;
    [Tooltip("How much air push (dynamic pressure, kPa) the rocket can take. Real rockets peak around 30-35 kPa.")]
    public float structuralLimitKPa = 45f;
    [Tooltip("Limit when the top of the rocket is not a nose cone or fairing.")]
    public float bluntNoseLimitKPa = 25f;
    [Tooltip("Most g-force the structure survives.")]
    public float maxGForce = 10f;

    [Header("Player")]
    [Tooltip("Movement and mouse-look scripts to switch off during the flight.")]
    public Behaviour[] disableWhileFlying;
    [Tooltip("Things like the crosshair to hide during the flight.")]
    public GameObject[] hideWhileFlying;

    [Header("Look")]
    [Tooltip("Optional TextMesh Pro font.")]
    public TMP_FontAsset font;

    [Header("Events")]
    [Tooltip("Runs when the rocket reaches the Karman line (hook the space scene up here later).")]
    public UnityEvent onReachedSpace;
    public UnityEvent onFlightFailed;

    // ───────────── constants ─────────────
    const double EarthR = 6371000.0, Mu = 3.986004418e14;
    const float G0 = 9.80665f, StepDt = 0.02f;
    static readonly float[] Warps = { 1f, 2f, 4f, 10f };

    // ───────────── runtime: parts ─────────────
    class Eng
    {
        public CADPart p; public int part; public string pool; public bool solid, dead, lit; public Grp grp;
        public float igniteTime = float.MaxValue, thrustN, mdot;
        public Transform t; public ParticleSystem flame, smoke; public Light glow; public float size;
    }
    class Grp
    {
        public string key, label; public bool side, attached = true; public int stage; public float dia;
        public readonly List<int> parts = new List<int>(); public readonly List<string> pools = new List<string>();
        public readonly List<Eng> engines = new List<Eng>();
    }

    enum Phase { Idle, Countdown, Flight, Ending }
    Phase phase = Phase.Idle;

    AscendCADData data;
    AscendRocket info;
    [NonSerialized] CADDesign design;
    CADPart[] P;
    Transform[] partGo;
    bool[] attached;
    int[] stageOf;
    Grp[] grpOf;
    readonly List<Grp> groups = new List<Grp>();
    readonly List<Eng> engines = new List<Eng>();
    readonly Dictionary<string, float> fuel = new Dictionary<string, float>(), fuelStart = new Dictionary<string, float>();
    readonly Dictionary<string, float> need = new Dictionary<string, float>();
    readonly List<int> fairingParts = new List<int>();
    Transform payloadGo;
    float payloadKg, coreDia, rocketHeight;
    int stageCount, curStage;
    bool guided, aeroNose, passive, cansat, engineCut;
    float goalKm = 100f, railDeg;
    string successBig = "100 KM - SPACE!";

    // ───────────── runtime: physics state ─────────────
    double px, py, vx, vy;            // 2D, Earth center at (0, -R) -> launch site at (0, R)
    float tFlight, tilt, throttle = 1f, tumbleRate;
    bool liftedOff, stageBurnout, pendingIgnite, fairingGone, maxQSaid, machSaid, throttleSaid, gThrottleSaid, fallingSaid, ended;
    float burnoutTime, igniteAt, kickStart = -1f;
    float q, mach, gNow, mass, altitude, speed, vUp;
    float maxQ, maxAlt, maxSpeed, maxG, liftoffTWR;
    int warpIndex;
    string stageLabel = "";

    // ───────────── runtime: world ─────────────
    Transform root;
    Vector3 basePos, downDir;
    readonly List<GameObject> debris = new List<GameObject>();
    Camera flightCam, playerCam;
    bool groundView;
    float camYaw, camPitch = 8f, camDist;
    float shake;
    Material skyCopy, skyOriginal;
    float skyExposure = 1f, skyAtmo = 1f;
    CursorLockMode savedLock; bool savedVisible;

    // ───────────── UI ─────────────
    TextMeshProUGUI promptText, bigText, telemetryText, logText, altLabel, endTitle, endBody, warpText;
    GameObject hudRoot, endCard;
    RectTransform altFill, qFill;
    Image qFillImg;
    CanvasGroup fade;
    readonly List<(Grp g, RectTransform fill, TextMeshProUGUI label)> fuelRows = new List<(Grp, RectTransform, TextMeshProUGUI)>();
    readonly List<string> log = new List<string>();
    float bigUntil;
    Coroutine flashRoutine;

    // ═════════════════════════════════════════════════════════════════════════
    //  UNITY
    // ═════════════════════════════════════════════════════════════════════════
    void Start()
    {
        BuildUI();
        hudRoot.SetActive(false);
        endCard.SetActive(false);
        bigText.text = "";
    }

    void Update()
    {
        switch (phase)
        {
            case Phase.Idle: IdleUpdate(); break;
            case Phase.Flight: FlightUpdate(); break;
        }
        if (bigText && Time.unscaledTime > bigUntil && phase != Phase.Countdown) bigText.text = "";
    }

    void LateUpdate()
    {
        if (phase == Phase.Flight || phase == Phase.Countdown || (phase == Phase.Ending && flightCam)) UpdateCamera();
    }

    void OnDestroy()
    {
        if (skyOriginal) RenderSettings.skybox = skyOriginal;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  LAUNCH CUBE INTERACTION
    // ═════════════════════════════════════════════════════════════════════════
    void IdleUpdate()
    {
        bool looking = LookingAtMe();
        if (flashRoutine == null)
            promptText.text = looking ? (AscendLaunchSite.Current ? KeyName(launchKey) + " to LAUNCH the rocket" : KeyName(launchKey) + " to check the launch pad") : "";
        if (!looking || !KeyDown(launchKey)) return;

        if (!AscendLaunchSite.Current)
        {
            Flash("No rocket on the launch pad yet. Get your design APPROVED in AscendCAD first.");
            return;
        }
        StartCoroutine(LaunchSequence());
    }

    bool LookingAtMe()
    {
        Camera cam = Camera.main;
        if (!cam) return false;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance, ~0, QueryTriggerInteraction.Collide)) return false;
        return hit.transform == transform || hit.transform.IsChildOf(transform);
    }

    void Flash(string msg)
    {
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashR(msg));
    }

    IEnumerator FlashR(string msg)
    {
        promptText.text = msg;
        yield return new WaitForSecondsRealtime(3f);
        promptText.text = "";
        flashRoutine = null;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  PREPARE: read the real rocket (parts, stages, boosters, tanks, engines)
    // ═════════════════════════════════════════════════════════════════════════
    bool Prepare()
    {
        var site = AscendLaunchSite.Current;
        info = site ? site.GetComponent<AscendRocket>() : null;
        if (!info || info.rocket == null || info.rocket.IsEmpty) return false;
        data = ascendCADData ? AscendCADData.Resolve(ascendCADData) : (AscendCADData.Active ? AscendCADData.Active : AscendCADData.Resolve(null));
        design = info.rocket;

        int n = design.parts.Count;
        P = new CADPart[n]; partGo = new Transform[n]; attached = new bool[n]; stageOf = new int[n]; grpOf = new Grp[n];
        groups.Clear(); engines.Clear(); fuel.Clear(); fuelStart.Clear(); fairingParts.Clear();

        var core = new bool[n]; var side = new int[n];
        for (int i = 0; i < n; i++)
        {
            P[i] = data.Get(design.parts[i].partId);
            core[i] = CADStats.IsCore(design, i);
            side[i] = SideRoot(design, i);
            attached[i] = P[i] != null;
        }
        for (int i = 0; i < n; i++) if (side[i] >= 0 && P[side[i]] != null && P[side[i]].crossfeed) side[i] = -1;

        // Stages are split by decouplers in the core stack (bottom = stage 1), same rule as AscendCAD
        var dec = new List<float>();
        for (int i = 0; i < n; i++) if (core[i] && P[i] != null && P[i].role == CADRole.Decoupler) dec.Add(design.parts[i].pos.y);
        dec.Sort();
        stageCount = dec.Count + 1;
        for (int i = 0; i < n; i++)
        {
            int a = i, guard = 0;
            while (!core[a] && design.parts[a].parent >= 0 && guard++ < 10000) a = design.parts[a].parent;
            float y = design.parts[a].pos.y; int st = 0;
            foreach (var v in dec) if (v < y - 1e-3f) st++;
            stageOf[i] = st;
        }

        // Groups: one per core stage, one per side booster
        var byKey = new Dictionary<string, Grp>();
        for (int i = 0; i < n; i++)
        {
            if (P[i] == null) continue;
            string key = side[i] >= 0 ? "side" + side[i] : "core" + stageOf[i];
            if (!byKey.TryGetValue(key, out var g))
            {
                g = new Grp { key = key, side = side[i] >= 0, stage = stageOf[i] };
                g.label = g.side ? "Booster" : "Stage " + (stageOf[i] + 1);
                byKey[key] = g; groups.Add(g);
            }
            g.parts.Add(i); grpOf[i] = g;
            if (P[i].IsBody && P[i].role != CADRole.Fairing) g.dia = Mathf.Max(g.dia, P[i].diameter);
            if (P[i].role == CADRole.Fairing) fairingParts.Add(i);

            if (P[i].role == CADRole.Tank && P[i].propellantKg > 0f) AddFuel(g, key + "|" + P[i].propellant, P[i].propellantKg);
            if (P[i].role == CADRole.SolidMotor && P[i].propellantKg > 0f) AddFuel(g, "solid" + i, P[i].propellantKg);
        }
        for (int k = 0; k < stageCount; k++)
            if (!byKey.ContainsKey("core" + k)) { var g = new Grp { key = "core" + k, stage = k, label = "Stage " + (k + 1) }; byKey[g.key] = g; groups.Add(g); }

        // Engines
        for (int i = 0; i < n; i++)
        {
            var p = P[i];
            if (p == null || (p.role != CADRole.Engine && p.role != CADRole.SolidMotor)) continue;
            var g = grpOf[i];
            var e = new Eng { p = p, part = i, grp = g, solid = p.role == CADRole.SolidMotor };
            e.pool = e.solid ? "solid" + i : g.key + "|" + p.propellant;
            if (!fuel.ContainsKey(e.pool)) e.dead = true;   // no tank of the right propellant feeding it
            engines.Add(e); g.engines.Add(e);
        }

        // Payload mass
        payloadKg = 0f;
        if (info.payload != null)
            foreach (var pl in info.payload.parts) { var pp = data.Get(pl.partId); if (pp != null) payloadKg += pp.WetMassKg; }

        // Map design parts to their 3D objects (AscendLaunchSite.BuildDesign makes them in order, skipping internal parts)
        Transform body = site.transform.childCount > 0 ? site.transform.GetChild(0) : null;
        int child = 0;
        if (body)
        {
            for (int i = 0; i < n; i++)
            {
                if (P[i] == null || P[i].attach == CADAttach.Internal) continue;
                if (child < body.childCount) partGo[i] = body.GetChild(child);
                child++;
            }
            for (int c = 0; c < body.childCount; c++)
                if (body.GetChild(c).name.StartsWith("Payload - ")) payloadGo = body.GetChild(c);
        }

        // Core width for drag
        coreDia = 0f;
        foreach (var g in groups) if (!g.side) coreDia = Mathf.Max(coreDia, g.dia);
        foreach (int i in fairingParts) coreDia = Mathf.Max(coreDia, P[i].diameter);
        if (coreDia <= 0f) coreDia = 1f;

        // Guidance + nose shape
        var rs = info.stats ?? CADStats.Rocket(design, data, payloadKg);
        guided = rs.Has(CADRole.FlightComputer) && rs.Has(CADRole.IMU);
        passive = !guided && rs.Has(CADRole.Aero);           // fins keep a small rocket pointing straight up
        cansat = AscendMissionGuide.Mission == AscendSimMission.SkySensor;
        goalKm = cansat ? (AscendTrajectoryControl.ConfirmedPlan != null ? AscendTrajectoryControl.ConfirmedPlan.targetKm : 1f) : karmanLineKm;
        railDeg = cansat && AscendTrajectoryControl.ConfirmedPlan != null ? AscendTrajectoryControl.ConfirmedPlan.kickDeg : 0f;
        aeroNose = TopIsAero();
        return true;
    }

    void AddFuel(Grp g, string pool, float kg)
    {
        fuel.TryGetValue(pool, out var v); fuel[pool] = v + kg;
        fuelStart.TryGetValue(pool, out var s); fuelStart[pool] = s + kg;
        if (!g.pools.Contains(pool)) g.pools.Add(pool);
    }

    static int SideRoot(CADDesign d, int i)
    {
        int guard = 0;
        while (i >= 0 && guard++ < 10000) { if (d.parts[i].link == CADLink.RadialUpright) return i; i = d.parts[i].parent; }
        return -1;
    }

    bool TopIsAero()
    {
        int top = -1; float topY = float.MinValue;
        for (int i = 0; i < design.parts.Count; i++)
        {
            var p = P[i];
            if (p == null || !CADStats.IsCore(design, i)) continue;
            float y = design.parts[i].pos.y + p.StackHeight * 0.5f;
            if (y > topY) { topY = y; top = i; }
        }
        var tp = top >= 0 ? P[top] : null;
        return tp != null && (tp.role == CADRole.NoseCone || tp.role == CADRole.Fairing || tp.shape == CADShape.OgiveTank);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  LAUNCH SEQUENCE
    // ═════════════════════════════════════════════════════════════════════════
    IEnumerator LaunchSequence()
    {
        if (!Prepare()) { Flash("This rocket can't be read. Rebuild it in AscendCAD."); yield break; }

        phase = Phase.Countdown;
        promptText.text = "";
        TakeOverPlayer(true);
        SetupWorld();
        SetupCamera();
        SetupSky();
        BuildFuelRows();
        log.Clear();
        hudRoot.SetActive(true);
        AddLog("Rocket on the pad: " + design.name + "  (" + AscendReport.Mass(TotalMass()) + ")");
        if (passive) AddLog("Fins keep the rocket flying straight up.");
        else if (!guided) AddLog("<color=#F85149>Warning: no flight computer + IMU. The rocket can't steer!</color>");
        if (cansat) AddLog($"Mission: release the CanSat at about {goalKm:0.#} km.");

        // Countdown (Space skips)
        float t = countdownSeconds;
        while (t > 0f)
        {
            int s = Mathf.CeilToInt(t);
            bigText.text = "<size=60%>LAUNCH IN</size>\n" + s;
            if (KeyDown(KeyCode.Space)) break;
            t -= Time.deltaTime;
            yield return null;
        }

        // Ignition
        IgniteStage(0, 0f);
        ShowBig("IGNITION!", 1.5f);
        AddLog("Ignition! Engines start up.");
        phase = Phase.Flight;
    }

    void SetupWorld()
    {
        var site = AscendLaunchSite.Current.transform;
        var rends = site.GetComponentsInChildren<Renderer>();
        Bounds b = rends.Length > 0 ? rends[0].bounds : new Bounds(site.position, Vector3.one);
        foreach (var r in rends) b.Encapsulate(r.bounds);
        rocketHeight = Mathf.Max(5f, b.size.y);
        basePos = new Vector3(b.center.x, b.min.y, b.center.z);

        root = new GameObject("Ascend Flight").transform;
        root.position = basePos;
        site.SetParent(root, true);
        foreach (var c in site.GetComponentsInChildren<Collider>()) c.enabled = false;  // moved by our own physics

        downDir = Vector3.ProjectOnPlane(downrangeDirection, Vector3.up);
        if (downDir.sqrMagnitude < 1e-4f) downDir = Vector3.right;
        downDir.Normalize();

        // Physics state
        px = 0; py = EarthR; vx = 0; vy = 0;
        tFlight = 0f; tilt = 0f; throttle = 1f; tumbleRate = 0f; curStage = 0;
        liftedOff = stageBurnout = pendingIgnite = fairingGone = maxQSaid = machSaid = throttleSaid = gThrottleSaid = fallingSaid = ended = false;
        kickStart = -1f; maxQ = maxAlt = maxSpeed = maxG = 0f; warpIndex = 0; engineCut = false;
        mass = TotalMass();
        liftoffTWR = 0f;

        // Exhaust effects on every engine (big clusters share effects to stay fast)
        var perStage = new Dictionary<Grp, int>();
        foreach (var e in engines)
        {
            if (e.dead || partGo[e.part] == null) continue;
            perStage.TryGetValue(e.grp, out var c);
            int clusterSize = e.grp.engines.Count;
            bool show = clusterSize <= 9 || c % Mathf.CeilToInt(clusterSize / 9f) == 0;
            perStage[e.grp] = c + 1;
            if (!show) continue;
            e.t = partGo[e.part];
            e.size = Mathf.Max(0.3f, e.p.diameter) * (clusterSize > 9 ? 1.8f : 1f);
            Vector3 nozzle = e.t.position - e.t.up * (e.p.length * 0.5f);
            e.flame = AscendFX.Flame(e.t, nozzle, e.size, e.solid);
            e.smoke = AscendFX.Smoke(e.t, nozzle, e.size);
            var lg = new GameObject("Engine Glow");
            lg.transform.SetParent(e.t, false);
            lg.transform.position = nozzle - e.t.up * e.size;
            e.glow = lg.AddComponent<Light>();
            e.glow.type = LightType.Point; e.glow.color = new Color(1f, 0.6f, 0.25f); e.glow.range = e.size * 30f; e.glow.intensity = 0f;
            e.glow.shadows = LightShadows.None;
        }
    }

    float TotalMass()
    {
        float m = payloadKg;
        for (int i = 0; i < P.Length; i++) if (attached[i] && P[i] != null) m += P[i].dryMassKg;
        foreach (var g in groups) if (g.attached) foreach (var pool in g.pools) m += fuel[pool];
        return Mathf.Max(1f, m);
    }

    void IgniteStage(int k, float at)
    {
        foreach (var e in engines) if (e.grp.stage == k && e.grp.attached) e.igniteTime = at;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  FLIGHT LOOP
    // ═════════════════════════════════════════════════════════════════════════
    void FlightUpdate()
    {
        for (int i = 0; i < Warps.Length; i++) if (KeyDown(KeyCode.Alpha1 + i)) warpIndex = i;
        if (KeyDown(KeyCode.C)) groundView = !groundView;

        float sim = Mathf.Min(Time.deltaTime, 0.1f) * Warps[warpIndex];
        while (sim > 0f && phase == Phase.Flight)
        {
            float dt = Mathf.Min(StepDt, sim);
            Step(dt);
            sim -= dt;
        }
        if (phase != Phase.Flight) return;
        PlaceRocket();
        UpdateEffects();
        UpdateSky();
        UpdateHud();
    }

    void Step(float dt)
    {
        tFlight += dt;
        double r = Math.Sqrt(px * px + py * py);
        double h = r - EarthR;
        double ux = px / r, uy = py / r;          // local up
        double ex = uy, ey = -ux;                 // local east (downrange)
        vUp = (float)(vx * ux + vy * uy);
        float vE = (float)(vx * ex + vy * ey);
        speed = (float)Math.Sqrt(vx * vx + vy * vy);
        altitude = (float)Math.Max(0.0, h);

        // Atmosphere
        float pr = Mathf.Exp(-altitude / 8000f);
        float rho = 1.225f * Mathf.Exp(-altitude / 8500f);
        float sound = Mathf.Sqrt(1.4f * 287f * AirTemp(altitude));
        mach = speed / sound;
        q = 0.5f * rho * speed * speed;

        // Throttle: ease off for Max Q and high g (liquid engines only, like real rockets)
        float qLimit = QLimit;
        if (guided && q > qLimit * 0.75f) { throttle = Mathf.Max(0.6f, throttle - 0.6f * dt); if (!throttleSaid && throttle < 0.95f) { throttleSaid = true; AddLog("Easing the engines for Max Q. <color=#9AA0A6>(throttle down)</color>"); } }
        else if (guided && gNow > 4.5f) { throttle = Mathf.Max(0.4f, throttle - 1f * dt); if (!gThrottleSaid) { gThrottleSaid = true; AddLog("Engines easing off to keep the g-force safe. <color=#9AA0A6>(g-limit throttle)</color>"); } }
        else throttle = Mathf.Min(1f, throttle + 0.3f * dt);

        // Thrust: each engine asks its tank for fuel; tanks share between engines
        need.Clear();
        foreach (var e in engines)
        {
            e.lit = EngineCanRun(e);
            if (!e.lit) { e.thrustN = 0f; e.mdot = 0f; continue; }
            e.thrustN = EngineThrust(e, pr, out e.mdot);
            need.TryGetValue(e.pool, out var nd); need[e.pool] = nd + e.mdot * dt;
        }
        float F = 0f;
        foreach (var e in engines)
        {
            if (!e.lit) continue;
            float scale = 1f;
            if (need.TryGetValue(e.pool, out var nd) && nd > 0f) scale = Mathf.Clamp01(fuel[e.pool] / nd);
            F += e.thrustN * scale;
            fuel[e.pool] = Mathf.Max(0f, fuel[e.pool] - e.mdot * dt * scale);
        }
        mass = TotalMass();

        // Guidance: straight up, small pitch kick, then follow the flight path (gravity turn)
        float flightPathDeg = speed > 20f ? Mathf.Atan2(vE, vUp) * Mathf.Rad2Deg : 0f;
        float want = tilt;
        if (cansat && (guided || passive)) want = speed > 20f ? Mathf.Clamp(flightPathDeg, 0f, 90f) : railDeg;   // launch rail tilt, then the nose follows the airflow
        else if (!guided)
        {
            if (passive) want = 0f;
            else if (liftedOff && altitude > 50f) { tumbleRate = Mathf.Min(25f, tumbleRate + 3f * dt); want = tilt + tumbleRate * dt; }
        }
        else if (!liftedOff || speed < 60f) want = 0f;
        else
        {
            if (kickStart < 0f) kickStart = tFlight;
            float maxTilt = Mathf.Lerp(12f, 72f, altitude / 80000f);
            want = tFlight - kickStart < 6f ? Mathf.Lerp(0f, 4f, (tFlight - kickStart) / 6f) : Mathf.Clamp(flightPathDeg, 0f, maxTilt);
        }
        tilt = guided ? Mathf.MoveTowards(tilt, want, 3f * dt) : want;
        float tr = tilt * Mathf.Deg2Rad;
        double tx = ux * Math.Cos(tr) + ex * Math.Sin(tr), ty = uy * Math.Cos(tr) + ey * Math.Sin(tr);

        // Forces
        double g = Mu / (r * r);
        double ax = tx * F / mass - ux * g, ay = ty * F / mass - uy * g;
        float area = DragArea();
        float drag = q * Cd(mach) * area;
        if (speed > 0.1f) { ax -= drag * vx / speed / mass; ay -= drag * vy / speed / mass; }
        gNow = (F + drag) / mass / G0;

        // Hold-down clamps until the engines are at full thrust and can lift the rocket
        if (!liftedOff)
        {
            float upAcc = F / mass - (float)g;
            if (tFlight >= 1.2f && upAcc > 0f)
            {
                liftedOff = true;
                liftoffTWR = F / (mass * G0);
                ShowBig("LIFTOFF!", 2f);
                AddLog($"LIFTOFF! <color=#9AA0A6>(thrust-to-weight {liftoffTWR:0.00})</color>");
            }
            else
            {
                vx = vy = 0;
                if (tFlight > 6f)
                {
                    float twr = F / (mass * G0);
                    Fail("TOO HEAVY!",
                        "The engines roared, but the rocket was too heavy to leave the pad.",
                        $"Liftoff thrust-to-weight {twr:0.00} (needs more than 1.0, ideally 1.2-1.5). Add engines/boosters or make it lighter.", false);
                }
                return;
            }
        }

        vx += ax * dt; vy += ay * dt;
        px += vx * dt; py += vy * dt;

        // Records
        maxQ = Mathf.Max(maxQ, q); maxAlt = Mathf.Max(maxAlt, altitude); maxSpeed = Mathf.Max(maxSpeed, speed); maxG = Mathf.Max(maxG, gNow);

        // Fun events
        if (!machSaid && mach >= 1f) { machSaid = true; AddLog("Faster than sound! <color=#9AA0A6>(Mach 1)</color>"); }
        if (!maxQSaid && maxQ > 3000f && q < maxQ * 0.97f) { maxQSaid = true; ShowBig("MAX Q", 1.5f); AddLog($"Max Q: the air pushed hardest here. <color=#9AA0A6>({maxQ / 1000f:0.0} kPa at {altitude / 1000f:0.0} km)</color>"); }

        // Failures
        float aoa = speed > 30f ? Mathf.Abs(Mathf.DeltaAngle(tilt, flightPathDeg)) : 0f;
        if (q > qLimit)
        { Fail("RIPPED APART!", "The air pushed too hard and the rocket broke into pieces.", $"Dynamic pressure {q / 1000f:0.0} kPa exceeded the {qLimit / 1000f:0} kPa structural limit" + (aeroNose ? "." : " (blunt nose: add a nose cone or fairing).") + " Climb more steeply or throttle down through Max Q.", true); return; }
        if (q * Mathf.Sin(aoa * Mathf.Deg2Rad) > 6000f)
        { Fail("TURNED SIDEWAYS!", "The rocket couldn't steer, turned sideways and the air ripped it apart.", $"Angle of attack {aoa:0}° at {q / 1000f:0.0} kPa. A rocket needs a flight computer AND an IMU to steer.", true); return; }
        if (gNow > GLimit)
        { Fail("CRUSHED!", "The rocket sped up so hard that it was crushed by its own push.", $"Axial load {gNow:0.0} g exceeded {GLimit:0} g. Use throttleable liquid engines or a heavier upper stage.", true); return; }

        // Staging
        if (liftedOff) Staging();

        // Fairing
        if (!fairingGone && altitude >= fairingJettisonKm * 1000f && fairingParts.Count > 0)
        {
            fairingGone = true;
            foreach (int i in fairingParts) Detach(i, 4f);
            AddLog("Fairing open! The nose cover falls away. <color=#9AA0A6>(fairing jettison)</color>");
        }

        // Sky Sensor: cut the engine for the target height, release the CanSat at the top
        if (cansat && liftedOff)
        {
            float ballistic = altitude + Mathf.Max(0f, vUp) * Mathf.Max(0f, vUp) / (2f * 9.81f);
            if (!engineCut && ballistic >= goalKm * 1000f && engines.Any(e => e.lit && !e.solid))
            { engineCut = true; AddLog("Engine cut-off: coasting up to the target height."); }
            if (vUp < 0f && altitude > 20f && maxAlt >= goalKm * 600f) { CanSatSuccess(); return; }
        }

        // Out of fuel before space
        if (!fallingSaid && vUp < 0f && !AnyFuelLeft())
        {
            fallingSaid = true;
            warpIndex = Mathf.Max(warpIndex, 2);
            ShowBig("OUT OF FUEL", 2f);
            AddLog($"Out of fuel! Highest point {maxAlt / 1000f:0.0} km. Falling back to Earth...");
        }

        // Ground impact
        if (liftedOff && h <= 0.0 && vUp < 0f)
        {
            bool reachedHigh = maxAlt > 2000f;
            Fail("CRASHED!", reachedHigh ? $"The rocket ran out of fuel at {maxAlt / 1000f:0.0} km, fell back and crashed." : "The rocket fell back to the ground and crashed.",
                $"Apogee {maxAlt / 1000f:0.0} km of {goalKm:0.#} km. More delta-v needed: more propellant, more efficient engines (higher Isp), or less mass.", true);
            return;
        }

        // SPACE!
        if (!cansat && altitude >= karmanLineKm * 1000f) ReachSpace();
    }

    // Small rockets are much stronger for their size than big ones (Electron-size = 1x)
    float SizeK => Mathf.Clamp(1.2f / Mathf.Max(0.05f, coreDia), 1f, 20f);
    float QLimit => (aeroNose ? structuralLimitKPa : bluntNoseLimitKPa) * 1000f * SizeK;
    float GLimit => maxGForce * Mathf.Clamp(SizeK, 1f, 4f);

    bool EngineCanRun(Eng e)
    {
        if (engineCut && !e.solid) return false;
        if (e.dead || !e.grp.attached || e.grp.stage != curStage || tFlight < e.igniteTime) return false;
        return fuel.TryGetValue(e.pool, out var f) && f > 0f;
    }

    float EngineThrust(Eng e, float pr, out float mdot)
    {
        var p = e.p;
        if (e.solid)
        {
            float burn = p.burnTimeS > 0f ? p.burnTimeS : 60f;
            mdot = p.propellantKg / burn;
            float fv = p.thrustVackN > 0f ? p.thrustVackN * 1000f : mdot * Mathf.Max(1f, p.ispVac) * G0;
            float fs = p.thrustSLkN > 0f ? p.thrustSLkN * 1000f : (p.ispSL > 0f ? mdot * p.ispSL * G0 : 0f);
            return Mix(fv, fs, pr);
        }
        float ramp = Mathf.Clamp01((tFlight - e.igniteTime) / 1f);
        float fvac = p.thrustVackN * 1000f;
        mdot = fvac / (Mathf.Max(1f, p.ispVac) * G0) * throttle * ramp;
        return Mix(fvac, p.thrustSLkN * 1000f, pr) * throttle * ramp;
    }

    // Thrust goes from the sea-level value to the vacuum value as the air thins.
    // Vacuum-only engines lose most of their thrust in thick air (flow separation).
    static float Mix(float fv, float fs, float pr) => fs > 0f ? Mathf.Lerp(fv, fs, pr) : fv * Mathf.Clamp01(1f - 2.5f * pr);

    static float AirTemp(float h)
    {
        if (h < 11000f) return 288.15f - 0.0065f * h;
        if (h < 20000f) return 216.65f;
        if (h < 47000f) return 216.65f + 0.0018f * (h - 20000f);
        return 265f;
    }

    // Drag coefficient vs Mach: low subsonic, peak in the transonic zone, lower when supersonic
    float Cd(float m)
    {
        float cd = m < 0.8f ? 0.30f : m < 1.2f ? Mathf.Lerp(0.30f, 0.55f, (m - 0.8f) / 0.4f) : Mathf.Lerp(0.55f, 0.25f, Mathf.Clamp01((m - 1.2f) / 3.8f));
        return aeroNose ? cd : cd * 1.8f;
    }

    float DragArea()
    {
        float a = Mathf.PI * 0.25f * coreDia * coreDia;
        foreach (var g in groups) if (g.side && g.attached) a += Mathf.PI * 0.25f * g.dia * g.dia;
        return a;
    }

    bool AnyFuelLeft()
    {
        foreach (var e in engines)
            if (!e.dead && e.grp.attached && e.grp.stage >= curStage && fuel.TryGetValue(e.pool, out var f) && f > 0f) return true;
        return false;
    }

    void Staging()
    {
        // Side boosters drop when empty
        foreach (var g in groups)
        {
            if (!g.side || !g.attached || g.stage != curStage || g.engines.Count == 0) continue;
            bool empty = g.pools.All(pool => fuel[pool] <= 0f);
            if (!empty) continue;
            DetachGroup(g, 6f);
            AddLog("Boosters empty! Dropping them to save weight. <color=#9AA0A6>(booster separation)</color>");
            ShowBig("BOOSTER SEP", 1.5f);
        }

        bool burning = engines.Any(e => e.grp.attached && e.grp.stage == curStage && !e.dead && fuel.TryGetValue(e.pool, out var f) && f > 0f);
        if (!burning && !stageBurnout && !pendingIgnite)
        {
            stageBurnout = true; burnoutTime = tFlight;
            if (curStage < stageCount - 1) AddLog($"Stage {curStage + 1} out of fuel. <color=#9AA0A6>(MECO, main engine cut-off)</color>");
        }
        if (stageBurnout && curStage < stageCount - 1 && tFlight - burnoutTime > 1f)
        {
            foreach (var g in groups) if (g.attached && g.stage == curStage) DetachGroup(g, 3f);
            curStage++;
            stageBurnout = false; pendingIgnite = true; igniteAt = tFlight + 1.2f;
            ShowBig("STAGE SEP", 1.5f);
            AddLog("Stage separation! The empty stage falls away.");
        }
        if (pendingIgnite && tFlight >= igniteAt)
        {
            pendingIgnite = false;
            IgniteStage(curStage, tFlight);
            if (engines.Any(e => e.grp.stage == curStage && !e.dead))
                AddLog($"Stage {curStage + 1} engine on! <color=#9AA0A6>(second-stage ignition)</color>");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  WORLD: move the rocket, drop parts, effects, sky
    // ═════════════════════════════════════════════════════════════════════════
    void PlaceRocket()
    {
        double theta = Math.Atan2(px, py);
        float down = (float)(EarthR * theta);
        root.position = basePos + downDir * down + Vector3.up * altitude;
        Vector3 axis = Vector3.up * Mathf.Cos(tilt * Mathf.Deg2Rad) + downDir * Mathf.Sin(tilt * Mathf.Deg2Rad);
        root.rotation = Quaternion.FromToRotation(Vector3.up, axis);
    }

    Vector3 WorldVelocity()
    {
        double r = Math.Sqrt(px * px + py * py);
        double ux = px / r, uy = py / r, ex = uy, ey = -ux;
        return Vector3.up * (float)(vx * ux + vy * uy) + downDir * (float)(vx * ex + vy * ey);
    }

    void DetachGroup(Grp g, float push)
    {
        if (!g.attached) return;
        g.attached = false;
        foreach (int i in g.parts) if (!fairingParts.Contains(i) || g.side) Detach(i, push);
        foreach (var e in g.engines) StopEffects(e);
    }

    void Detach(int i, float push)
    {
        attached[i] = false;
        var t = partGo[i];
        if (!t) return;
        Vector3 center = root.position + root.up * rocketHeight * 0.5f;
        Vector3 outward = Vector3.ProjectOnPlane(t.position - center, root.up);
        Vector3 kick = outward.sqrMagnitude > 0.01f ? outward.normalized * push : -root.up * push * 0.5f;
        MakeDebris(t, WorldVelocity() + kick);
    }

    void MakeDebris(Transform t, Vector3 vel)
    {
        t.SetParent(null, true);
        var d = t.gameObject.AddComponent<AscendDebris>();
        d.velocity = vel;
        d.spin = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(10f, 60f);
        d.groundY = basePos.y - 1f;
        debris.Add(t.gameObject);
    }

    void StopEffects(Eng e)
    {
        if (e.flame) { var em = e.flame.emission; em.rateOverTime = 0f; }
        if (e.smoke) { var em = e.smoke.emission; em.rateOverTime = 0f; }
        if (e.glow) e.glow.intensity = 0f;
    }

    void UpdateEffects()
    {
        float pr = Mathf.Exp(-altitude / 8000f);
        foreach (var e in engines)
        {
            if (!e.flame) continue;
            bool on = e.lit && e.grp.attached;
            float power = on ? Mathf.Clamp01(e.thrustN / Mathf.Max(1f, e.p.thrustVackN * 1000f)) : 0f;
            var em = e.flame.emission; em.rateOverTime = on ? 160f * Mathf.Max(0.3f, power) : 0f;
            var sh = e.flame.shape; sh.angle = Mathf.Lerp(30f, 6f, pr);          // plume spreads out in thin air
            var main = e.flame.main; main.startSize = e.size * Mathf.Lerp(3.2f, 1.3f, pr);
            if (e.smoke) { var sem = e.smoke.emission; sem.rateOverTime = on ? 45f * pr : 0f; }
            if (e.glow) e.glow.intensity = on ? (3f + UnityEngine.Random.Range(-0.6f, 0.6f)) * Mathf.Max(0.3f, power) : 0f;
        }
        if (liftedOff && altitude < 3000f) shake = Mathf.Max(shake, 0.15f * (1f - altitude / 3000f));
    }

    void SetupSky()
    {
        skyOriginal = RenderSettings.skybox;
        if (!skyOriginal) return;
        skyCopy = new Material(skyOriginal);   // edit a copy so the real skybox asset never changes
        RenderSettings.skybox = skyCopy;
        if (skyCopy.HasProperty("_Exposure")) skyExposure = skyCopy.GetFloat("_Exposure");
        if (skyCopy.HasProperty("_AtmosphereThickness")) skyAtmo = skyCopy.GetFloat("_AtmosphereThickness");
    }

    void UpdateSky()
    {
        if (!skyCopy) return;
        float k = Mathf.Clamp01(altitude / 70000f);   // the sky turns black as the air runs out
        if (skyCopy.HasProperty("_Exposure")) skyCopy.SetFloat("_Exposure", Mathf.Lerp(skyExposure, skyExposure * 0.08f, k));
        if (skyCopy.HasProperty("_AtmosphereThickness")) skyCopy.SetFloat("_AtmosphereThickness", Mathf.Lerp(skyAtmo, 0.05f, k));
        if (flightCam && flightCam.clearFlags == CameraClearFlags.SolidColor)
            flightCam.backgroundColor = Color.Lerp(new Color(0.45f, 0.65f, 0.9f), Color.black, k);
    }

    void RestoreSky()
    {
        if (skyOriginal) RenderSettings.skybox = skyOriginal;
        if (skyCopy) Destroy(skyCopy);
        skyCopy = null;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  CAMERA
    // ═════════════════════════════════════════════════════════════════════════
    void SetupCamera()
    {
        playerCam = Camera.main;
        var go = new GameObject("Ascend Launch Camera");
        flightCam = go.AddComponent<Camera>();
        if (playerCam)
        {
            flightCam.CopyFrom(playerCam);
            flightCam.depth = playerCam.depth + 5f;
            playerCam.enabled = false;
        }
        flightCam.nearClipPlane = 0.5f;
        flightCam.farClipPlane = 400000f;
        flightCam.fieldOfView = 60f;
        camDist = Mathf.Max(35f, rocketHeight * 1.8f);
        camYaw = Quaternion.LookRotation(-Vector3.Cross(downDir, Vector3.up)).eulerAngles.y;
        camPitch = 6f;
        groundView = false;
    }

    void UpdateCamera()
    {
        if (!flightCam || !root) return;
        Vector3 target = root.position + root.up * rocketHeight * 0.5f;

        if (phase == Phase.Flight || phase == Phase.Countdown)
        {
            Vector2 md = MouseDelta();
            camYaw += md.x * 3f; camPitch = Mathf.Clamp(camPitch - md.y * 3f, -60f, 80f);
            camDist = Mathf.Clamp(camDist * (1f - Scroll() * 0.1f), rocketHeight * 0.8f, rocketHeight * 20f + 200f);
        }

        if (groundView)
        {
            Vector3 side = Vector3.Cross(downDir, Vector3.up);
            Vector3 pos = basePos - downDir * 450f + side * 150f + Vector3.up * 3f;
            flightCam.transform.position = pos;
            flightCam.transform.LookAt(target);
            float dist = Vector3.Distance(pos, target);
            flightCam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(rocketHeight * 1.5f / dist) * Mathf.Rad2Deg, 0.3f, 60f);
        }
        else
        {
            flightCam.fieldOfView = 60f;
            Vector3 pos = target + Quaternion.Euler(camPitch, camYaw, 0f) * new Vector3(0f, 0f, -camDist);
            if (pos.y < basePos.y + 1f) pos.y = basePos.y + 1f;
            flightCam.transform.position = pos;
            flightCam.transform.LookAt(target);
        }

        if (shake > 0f)
        {
            flightCam.transform.position += UnityEngine.Random.insideUnitSphere * shake;
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 0.6f);
        }
    }

    void TakeOverPlayer(bool flying)
    {
        if (flying) { savedLock = Cursor.lockState; savedVisible = Cursor.visible; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        else { Cursor.lockState = savedLock; Cursor.visible = savedVisible; }
        if (disableWhileFlying != null) foreach (var b in disableWhileFlying) if (b) b.enabled = !flying;
        if (hideWhileFlying != null) foreach (var g in hideWhileFlying) if (g) g.SetActive(!flying);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  ENDINGS
    // ═════════════════════════════════════════════════════════════════════════
    void Fail(string title, string kid, string expert, bool explode)
    {
        if (ended) return;
        ended = true;
        phase = Phase.Ending;
        foreach (var e in engines) StopEffects(e);
        if (explode && root)
        {
            Vector3 c = root.position + root.up * rocketHeight * 0.5f;
            AscendFX.Boom(c, Mathf.Clamp(rocketHeight / 15f, 1f, 6f));
            shake = 1.2f;
            Vector3 v = WorldVelocity();
            for (int i = 0; i < partGo.Length; i++)
                if (attached[i] && partGo[i]) { attached[i] = false; MakeDebris(partGo[i], v + UnityEngine.Random.insideUnitSphere * 25f); }
            if (payloadGo) MakeDebris(payloadGo, v + UnityEngine.Random.insideUnitSphere * 15f);
        }
        AddLog("<color=#F85149>" + title + "</color> " + kid);
        SendReport(false, title, kid, expert);
        onFlightFailed?.Invoke();
        StartCoroutine(EndRoutine(false, title, kid, expert));
    }

    void CanSatSuccess()
    {
        if (ended) return;
        ended = true;
        phase = Phase.Ending;
        successBig = "CANSAT RELEASED!";
        AddLog("CanSat released! The parachute opens.");
        string kid = $"Your CanSat was released at {maxAlt / 1000f:0.00} km and is floating down on its parachute, measuring the air all the way!";
        string expert = $"Apogee {maxAlt:N0} m at T+{FormatTime(tFlight)}, max Q {maxQ / 1000f:0.0} kPa, max {maxG:0.0} g. Descent ~8 m/s, about {maxAlt / 8f / 60f:0.#} min to land.";
        SendReport(true, "CANSAT RELEASED", kid, expert);
        onReachedSpace?.Invoke();
        StartCoroutine(EndRoutine(true, "MISSION SUCCESS!", kid, expert));
    }

    void ReachSpace()
    {
        if (ended) return;
        successBig = "100 KM - SPACE!";
        ended = true;
        phase = Phase.Ending;
        AddLog("WELCOME TO SPACE! You crossed the Karman line.");
        string kid = $"Your rocket {design.name} flew past {karmanLineKm:0} km, where space begins!";
        string expert = $"Karman line at T+{FormatTime(tFlight)}, {speed / 1000f:0.00} km/s, max Q {maxQ / 1000f:0.0} kPa, max {maxG:0.0} g, stage {curStage + 1} of {stageCount}.";
        SendReport(true, "REACHED SPACE", kid, expert);
        onReachedSpace?.Invoke();
        StartCoroutine(EndRoutine(true, "WELCOME TO SPACE!", kid, expert));
    }

    IEnumerator EndRoutine(bool success, string title, string kid, string expert)
    {
        if (success)
        {
            ShowBig(successBig, 2f);
            yield return Fade(0f, 1f, 1f);
            if (root) Destroy(root.gameObject);      // the rocket flies on to the next scene (later)
        }
        else
        {
            // Watch the result for a moment
            float t = 0f;
            while (t < 4f)
            {
                t += Time.deltaTime;
                UpdateSky();
                yield return null;
            }
        }

        endTitle.text = title;
        endTitle.color = success ? new Color(0.25f, 0.9f, 0.4f) : new Color(1f, 0.35f, 0.3f);
        string fix = success ? "" : AscendMissionSim.FixFor(title);
        endBody.text = kid + "\n\n<size=80%><color=#9AA0A6>Engineer's note: " + expert + "</color></size>\n\n" +
                       (string.IsNullOrEmpty(fix) ? "" : "<size=85%><b><color=#FFC850>How to prevent it next time</color></b>\n" + fix + "</size>\n\n") +
                       $"<size=85%>Highest point <b>{maxAlt / 1000f:0.0} km</b>   Top speed <b>{maxSpeed * 3.6f:N0} km/h</b>   Max Q <b>{maxQ / 1000f:0.0} kPa</b>   Max <b>{maxG:0.0} g</b></size>\n\n" +
                       "<size=70%><color=#9AA0A6>A flight report was sent to PhMail.</color></size>\n\n" +
                       "<color=#FFC850>" + KeyName(launchKey) + " to continue</color>";
        endCard.SetActive(true);
        while (!KeyDown(launchKey)) yield return null;
        yield return null;

        endCard.SetActive(false);
        if (!success) yield return Fade(0f, 1f, 0.6f);
        CleanUp();
        yield return Fade(1f, 0f, 0.6f);
        phase = Phase.Idle;
    }

    void CleanUp()
    {
        hudRoot.SetActive(false);
        bigText.text = "";
        foreach (var d in debris) if (d) Destroy(d);
        debris.Clear();
        if (root) Destroy(root.gameObject);
        root = null;
        if (flightCam) Destroy(flightCam.gameObject);
        if (playerCam) playerCam.enabled = true;
        RestoreSky();
        TakeOverPlayer(false);
        foreach (var row in fuelRows) if (row.fill) Destroy(row.fill.parent.parent.gameObject);
        fuelRows.Clear();
    }

    void SendReport(bool success, string title, string kid, string expert)
    {
        if (data == null) return;
        var sb = new StringBuilder();
        string mission = MissionBriefing.SelectedMission != null ? MissionBriefing.SelectedMission.title : "Test flight";
        sb.Append("Hello Design Team,\n\nHere is the flight report for <b>").Append(design.name).Append("</b>.\n\n");
        sb.Append("<b>Mission:</b> ").Append(mission).Append('\n');
        sb.Append("<b>Result:</b> ").Append(success ? "<color=#3FB950>SUCCESS - reached space</color>" : "<color=#F85149>" + title + "</color>").Append("\n\n");
        sb.Append(kid).Append("\n\n");
        sb.Append("<b>FLIGHT DATA</b>\n");
        sb.Append($"- Liftoff thrust-to-weight: {liftoffTWR:0.00}\n");
        sb.Append(cansat ? $"- Highest point: {maxAlt:N0} m (target {goalKm * 1000f:N0} m)\n" : $"- Highest point: {maxAlt / 1000f:0.0} km (space starts at {karmanLineKm:0} km)\n");
        sb.Append($"- Top speed: {maxSpeed * 3.6f:N0} km/h ({maxSpeed:N0} m/s)\n");
        sb.Append($"- Max Q (biggest air push): {maxQ / 1000f:0.0} kPa\n");
        sb.Append($"- Max g-force: {maxG:0.0} g\n");
        sb.Append($"- Flight time: {FormatTime(tFlight)}\n");
        sb.Append($"- Stages used: {curStage + 1} of {stageCount}\n\n");
        sb.Append("<b>ENGINEER'S NOTE</b>\n").Append(expert).Append("\n\n");
        if (!success) { string fx = AscendMissionSim.FixFor(title); if (!string.IsNullOrEmpty(fx)) sb.Append("<b>HOW TO PREVENT IT NEXT TIME</b>\n").Append(fx).Append("\n\n"); }
        sb.Append("- Ascend Philippines Flight Dynamics");
        AscendMail.Deliver(data.agencyEmail, data.playerEmail, (success ? "FLIGHT SUCCESS: " : "FLIGHT FAILED: ") + design.name, sb.ToString(), success);
    }

    IEnumerator Fade(float from, float to, float seconds)
    {
        float t = 0f;
        fade.alpha = from;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            fade.alpha = Mathf.Lerp(from, to, t / seconds);
            yield return null;
        }
        fade.alpha = to;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  HUD
    // ═════════════════════════════════════════════════════════════════════════
    void ShowBig(string s, float seconds) { bigText.text = s; bigUntil = Time.unscaledTime + seconds; }

    void AddLog(string s)
    {
        log.Add("<color=#FFC850>T+" + FormatTime(tFlight) + "</color>  " + s);
        while (log.Count > 7) log.RemoveAt(0);
        if (logText) logText.text = string.Join("\n", log);
    }

    static string FormatTime(float t) { int s = Mathf.Max(0, (int)t); return (s / 60).ToString("00") + ":" + (s % 60).ToString("00"); }

    void UpdateHud()
    {
        int boosters = groups.Count(g => g.side && g.attached);
        stageLabel = $"Stage {curStage + 1} of {stageCount}" + (boosters > 0 ? $"  +  {boosters} booster{(boosters > 1 ? "s" : "")}" : "");
        float qLimit = QLimit;
        telemetryText.text =
            $"<color=#9AA0A6>TIME</color>      T+{FormatTime(tFlight)}\n" +
            $"<color=#9AA0A6>HEIGHT</color>    <b>{altitude / 1000f:0.00} km</b>\n" +
            $"<color=#9AA0A6>SPEED</color>     <b>{speed * 3.6f:N0} km/h</b>  <size=80%><color=#9AA0A6>Mach {mach:0.0}</color></size>\n" +
            $"<color=#9AA0A6>G-FORCE</color>   {gNow:0.0} g\n" +
            $"<color=#9AA0A6>MASS</color>      {AscendReport.Mass(mass)}\n" +
            $"<color=#9AA0A6>STAGE</color>     {stageLabel}\n" +
            $"<color=#9AA0A6>AIR PUSH</color>  {q / 1000f:0.0} kPa <size=75%><color=#9AA0A6>(Q, limit {qLimit / 1000f:0})</color></size>";

        float qf = Mathf.Clamp01(q / qLimit);
        qFill.anchorMax = new Vector2(qf, 1f);
        qFillImg.color = qf < 0.6f ? new Color(0.25f, 0.8f, 0.4f) : qf < 0.85f ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 0.3f, 0.25f);

        foreach (var row in fuelRows)
        {
            float start = row.g.pools.Sum(p => fuelStart[p]);
            float left = row.g.attached ? row.g.pools.Sum(p => fuel[p]) : 0f;
            float f = start > 0f ? left / start : 0f;
            row.fill.anchorMax = new Vector2(f, 1f);
            row.label.text = row.g.label + (row.g.attached ? $"  {f * 100f:0}%" : "  <color=#9AA0A6>dropped</color>");
        }

        float af = Mathf.Clamp01(altitude / (goalKm * 1000f));
        altFill.anchorMax = new Vector2(af, 1f);
        altLabel.text = cansat ? $"{altitude:N0} m of {goalKm * 1000f:N0} m  <color=#9AA0A6>(CanSat release height)</color>"
                               : $"{altitude / 1000f:0.0} km of {karmanLineKm:0} km  <color=#9AA0A6>(space starts at the Karman line)</color>";
        warpText.text = $"Time speed {Warps[warpIndex]:0}x   |   1-4 time speed   C camera   Mouse look   Scroll zoom";
    }

    void BuildFuelRows()
    {
        fuelRows.Clear();
        var list = groups.Where(g => g.pools.Count > 0).OrderBy(g => g.side ? -1 : g.stage).ToList();
        int i = 0;
        foreach (var g in list)
        {
            if (i >= 6) break;
            if (g.side) g.label = "Booster " + (list.Where(x => x.side).ToList().IndexOf(g) + 1);
            var row = Gui.Rect("Fuel " + g.label, fuelPanel);
            row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f); row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(-32f, 40f); row.anchoredPosition = new Vector2(0f, -48f - i * 44f);
            var label = Gui.Text(row, g.label, 18, Color.white, font, FontStyles.Bold);
            label.rectTransform.anchorMin = new Vector2(0f, 0.5f); label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            var track = Gui.Img(row, new Color(1f, 1f, 1f, 0.12f), 4f);
            var trt = track.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = new Vector2(1f, 0.42f); trt.offsetMin = trt.offsetMax = Vector2.zero;
            var fill = Gui.Img(trt, g.side ? new Color(1f, 0.6f, 0.2f) : new Color(0.3f, 0.6f, 1f), 4f);
            var frt = fill.rectTransform; frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
            fuelRows.Add((g, frt, label));
            i++;
        }
        fuelPanel.sizeDelta = new Vector2(fuelPanel.sizeDelta.x, 56f + i * 44f);
    }

    RectTransform fuelPanel;

    void BuildUI()
    {
        var canvasGO = new GameObject("Ascend Flight UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var canvasRoot = (RectTransform)canvasGO.transform;

        // Prompt (small, bottom)
        promptText = Gui.Text(canvasRoot, "", 24, Color.white, font, FontStyles.Bold);
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.outlineWidth = 0.2f; promptText.outlineColor = new Color32(0, 0, 0, 220);
        var prt = promptText.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0f);
        prt.sizeDelta = new Vector2(1400, 44); prt.anchoredPosition = new Vector2(0, 50);

        // HUD
        var hud = Gui.Rect("HUD", root); Gui.Stretch(hud);
        hudRoot = hud.gameObject;

        var tele = Gui.Img(hud, new Color(0f, 0f, 0f, 0.85f), 18f).rectTransform;
        tele.anchorMin = tele.anchorMax = tele.pivot = new Vector2(0f, 1f);
        tele.sizeDelta = new Vector2(470, 330); tele.anchoredPosition = new Vector2(24, -24);
        var title = Gui.Text(tele, "FLIGHT DATA", 18, new Color(1f, 0.78f, 0.31f), font, FontStyles.Bold);
        Gui.Stretch(title.rectTransform, 20, 0, 20, 14); title.alignment = TextAlignmentOptions.TopLeft;
        telemetryText = Gui.Text(tele, "", 22, Color.white, font);
        Gui.Stretch(telemetryText.rectTransform, 20, 46, 20, 44); telemetryText.alignment = TextAlignmentOptions.TopLeft;
        telemetryText.lineSpacing = 6f;
        var qTrack = Gui.Img(tele, new Color(1f, 1f, 1f, 0.12f), 4f).rectTransform;
        qTrack.anchorMin = new Vector2(0f, 0f); qTrack.anchorMax = new Vector2(1f, 0f); qTrack.pivot = new Vector2(0.5f, 0f);
        qTrack.sizeDelta = new Vector2(-40f, 12f); qTrack.anchoredPosition = new Vector2(0f, 22f);
        qFillImg = Gui.Img(qTrack, new Color(0.25f, 0.8f, 0.4f), 4f);
        qFill = qFillImg.rectTransform; qFill.anchorMin = Vector2.zero; qFill.anchorMax = new Vector2(0f, 1f); qFill.offsetMin = qFill.offsetMax = Vector2.zero;

        fuelPanel = Gui.Img(hud, new Color(0f, 0f, 0f, 0.85f), 18f).rectTransform;
        fuelPanel.anchorMin = fuelPanel.anchorMax = fuelPanel.pivot = new Vector2(0f, 1f);
        fuelPanel.sizeDelta = new Vector2(470, 100); fuelPanel.anchoredPosition = new Vector2(24, -370);
        var ft = Gui.Text(fuelPanel, "FUEL", 18, new Color(1f, 0.78f, 0.31f), font, FontStyles.Bold);
        Gui.Stretch(ft.rectTransform, 20, 0, 20, 14); ft.alignment = TextAlignmentOptions.TopLeft;

        var logBox = Gui.Img(hud, new Color(0f, 0f, 0f, 0.85f), 18f).rectTransform;
        logBox.anchorMin = logBox.anchorMax = logBox.pivot = new Vector2(1f, 1f);
        logBox.sizeDelta = new Vector2(620, 300); logBox.anchoredPosition = new Vector2(-24, -24);
        var lt = Gui.Text(logBox, "MISSION LOG", 18, new Color(1f, 0.78f, 0.31f), font, FontStyles.Bold);
        Gui.Stretch(lt.rectTransform, 20, 0, 20, 14); lt.alignment = TextAlignmentOptions.TopLeft;
        logText = Gui.Text(logBox, "", 19, Color.white, font);
        Gui.Stretch(logText.rectTransform, 20, 14, 20, 46); logText.alignment = TextAlignmentOptions.TopLeft;
        logText.lineSpacing = 4f;

        // Height bar to space
        var altBox = Gui.Img(hud, new Color(0f, 0f, 0f, 0.85f), 18f).rectTransform;
        altBox.anchorMin = altBox.anchorMax = altBox.pivot = new Vector2(0.5f, 0f);
        altBox.sizeDelta = new Vector2(1000, 96); altBox.anchoredPosition = new Vector2(0, 24);
        altLabel = Gui.Text(altBox, "", 20, Color.white, font, FontStyles.Bold);
        Gui.Stretch(altLabel.rectTransform, 24, 40, 24, 12); altLabel.alignment = TextAlignmentOptions.MidlineLeft;
        var altTrack = Gui.Img(altBox, new Color(1f, 1f, 1f, 0.12f), 6f).rectTransform;
        altTrack.anchorMin = new Vector2(0f, 0f); altTrack.anchorMax = new Vector2(1f, 0f); altTrack.pivot = new Vector2(0.5f, 0f);
        altTrack.sizeDelta = new Vector2(-48f, 14f); altTrack.anchoredPosition = new Vector2(0f, 20f);
        altFill = Gui.Img(altTrack, new Color(0.55f, 0.75f, 1f), 6f).rectTransform;
        altFill.anchorMin = Vector2.zero; altFill.anchorMax = new Vector2(0f, 1f); altFill.offsetMin = altFill.offsetMax = Vector2.zero;

        warpText = Gui.Text(hud, "", 16, new Color(1f, 1f, 1f, 0.7f), font);
        var wrt = warpText.rectTransform;
        wrt.anchorMin = wrt.anchorMax = wrt.pivot = new Vector2(0.5f, 0f);
        wrt.sizeDelta = new Vector2(1000, 24); wrt.anchoredPosition = new Vector2(0, 124);
        warpText.alignment = TextAlignmentOptions.Center;

        // Big center messages
        bigText = Gui.Text(canvasRoot, "", 110, Color.white, font, FontStyles.Bold);
        Gui.Stretch(bigText.rectTransform, 0, 200, 0, 200);
        bigText.alignment = TextAlignmentOptions.Center;
        bigText.outlineWidth = 0.15f; bigText.outlineColor = new Color32(0, 0, 0, 230);

        // Fade (on top)
        var f = Gui.Img(canvasRoot, Color.black, 0f);
        Gui.Stretch(f.rectTransform);
        fade = f.gameObject.AddComponent<CanvasGroup>();
        fade.alpha = 0f; fade.blocksRaycasts = false; fade.interactable = false;

        // End card (above the fade, so it shows on a black screen too)
        var card = Gui.Img(canvasRoot, new Color(0f, 0f, 0f, 0.92f), 26f).rectTransform;
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(1200, 860);
        endCard = card.gameObject;
        endTitle = Gui.Text(card, "", 64, Color.white, font, FontStyles.Bold);
        Gui.Stretch(endTitle.rectTransform, 40, 740, 40, 30); endTitle.alignment = TextAlignmentOptions.Center;
        endBody = Gui.Text(card, "", 25, Color.white, font);
        Gui.Stretch(endBody.rectTransform, 60, 30, 60, 140); endBody.alignment = TextAlignmentOptions.Top;

    }

    // ═════════════════════════════════════════════════════════════════════════
    //  INPUT (works with the old Input Manager and the new Input System)
    // ═════════════════════════════════════════════════════════════════════════
    static string KeyName(KeyCode k) => k == KeyCode.Mouse0 ? "Click" : "Press " + k;

    static bool KeyDown(KeyCode k)
    {
#if ENABLE_INPUT_SYSTEM
        if (k == KeyCode.Mouse0) return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        var kb = Keyboard.current;
        if (kb == null) return false;
        string n = k.ToString();
        if (n.StartsWith("Alpha")) n = "Digit" + n.Substring(5);
        else if (n == "Return") n = "Enter";
        if (Enum.TryParse(n, out Key key) && key != Key.None) return kb[key].wasPressedThisFrame;
        return false;
#else
        return Input.GetKeyDown(k);
#endif
    }

    static Vector2 MouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.05f : Vector2.zero;
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 0.5f;
#endif
    }

    static float Scroll()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
        return Input.mouseScrollDelta.y;
#endif
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Small UI helpers
    // ═════════════════════════════════════════════════════════════════════════
    static class Gui
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }

        public static Image Img(Transform parent, Color c, float radius)
        {
            var img = Rect("Image", parent).gameObject.AddComponent<Image>();
            img.color = c; img.raycastTarget = false;
            if (radius > 0f) { img.sprite = AscendFX.RoundSprite; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 64f / radius; }
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string s, float size, Color c, TMP_FontAsset font, FontStyles st = FontStyles.Normal)
        {
            var t = Rect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (font) t.font = font;
            t.text = s; t.fontSize = size; t.color = c; t.fontStyle = st; t.richText = true; t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            return t;
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  DEBRIS: dropped stages, boosters, fairings and pieces after an explosion
// ═════════════════════════════════════════════════════════════════════════════
public class AscendDebris : MonoBehaviour
{
    public Vector3 velocity, spin;
    public float groundY;
    float life;

    void Update()
    {
        float dt = Time.deltaTime;
        float h = Mathf.Max(0f, transform.position.y - groundY);
        float airDrag = 0.15f * Mathf.Exp(-h / 8500f);      // thin air up high, thick air low down
        velocity += Vector3.down * 9.81f * dt;
        velocity *= Mathf.Max(0f, 1f - airDrag * dt);
        transform.position += velocity * dt;
        transform.Rotate(spin * dt, Space.World);
        life += dt;
        if (transform.position.y <= groundY)
        {
            AscendFX.Boom(transform.position, 0.8f);
            Destroy(gameObject);
        }
        else if (life > 180f) Destroy(gameObject);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  EFFECTS: engine flames, smoke, explosions (made from code, no assets needed)
// ═════════════════════════════════════════════════════════════════════════════
public static class AscendFX
{
    static Material additive, alpha;
    static Texture2D soft;
    static Sprite round;

    public static Sprite RoundSprite
    {
        get
        {
            if (round) return round;
            const int size = 160; const float r = 64f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dx = Mathf.Max(r - fx, 0f, fx - (size - r)), dy = Mathf.Max(r - fy, 0f, fy - (size - r));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f)));
            }
            tex.SetPixels32(px); tex.Apply();
            return round = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        }
    }

    static Texture2D Soft()
    {
        if (soft) return soft;
        const int s = 64;
        soft = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s * 0.5f, s * 0.5f)) / (s * 0.5f);
            float a = Mathf.Clamp01(1f - d); a *= a;
            px[y * s + x] = new Color32(255, 255, 255, (byte)(255 * a));
        }
        soft.SetPixels32(px); soft.Apply();
        return soft;
    }

    static Material Make(bool add)
    {
        string[] names = add
            ? new[] { "Universal Render Pipeline/Particles/Unlit", "Particles/Standard Unlit", "Legacy Shaders/Particles/Additive", "Sprites/Default" }
            : new[] { "Universal Render Pipeline/Particles/Unlit", "Particles/Standard Unlit", "Legacy Shaders/Particles/Alpha Blended", "Sprites/Default" };
        Shader sh = null;
        foreach (var n in names) { sh = Shader.Find(n); if (sh) break; }
        var m = new Material(sh);
        var tex = Soft();
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", add ? 2f : 0f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)(add ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.SrcAlpha));
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)(add ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = 3000;
        return m;
    }

    static Material Additive => additive ? additive : (additive = Make(true));
    static Material Alpha => alpha ? alpha : (alpha = Make(false));

    static ParticleSystem New(string name, Transform parent, Vector3 worldPos, Material mat, bool worldSpace)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, true);
        go.transform.position = worldPos;
        go.transform.rotation = parent ? parent.rotation * Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;   // +Z points down the nozzle
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        var main = ps.main;
        main.playOnAwake = false;
        main.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        return ps;
    }

    public static ParticleSystem Flame(Transform engine, Vector3 nozzle, float size, bool solid)
    {
        var ps = New("Flame", engine, nozzle, Additive, false);
        var main = ps.main;
        main.loop = true; main.duration = 1f; main.maxParticles = 400;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(size * 25f, size * 40f);
        main.startSize = size * 1.3f;
        main.startColor = solid ? new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.8f), new Color(1f, 0.7f, 0.35f))
                                : new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.55f), new Color(1f, 0.55f, 0.2f));
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6f; sh.radius = size * 0.3f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var gr = new Gradient();
        gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.6f, 0.2f), 0.4f), new GradientColorKey(new Color(0.9f, 0.25f, 0.1f), 1f) },
                   new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = gr;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
        ps.Play();
        return ps;
    }

    public static ParticleSystem Smoke(Transform engine, Vector3 nozzle, float size)
    {
        var ps = New("Smoke", engine, nozzle, Alpha, true);
        var main = ps.main;
        main.loop = true; main.duration = 1f; main.maxParticles = 1500;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(size * 8f, size * 14f);
        main.startSize = size * 3f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.85f, 0.85f, 0.55f), new Color(0.7f, 0.7f, 0.72f, 0.45f));
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 15f; sh.radius = size * 0.5f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var gr = new Gradient();
        gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                   new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = gr;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 5f));
        ps.Play();
        return ps;
    }

    public static void Boom(Vector3 pos, float scale)
    {
        var holder = new GameObject("Explosion");
        holder.transform.position = pos;

        var fire = New("Fire", holder.transform, pos, Additive, true);
        var m = fire.main;
        m.loop = false; m.duration = 0.5f; m.maxParticles = 200;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(10f * scale, 35f * scale);
        m.startSize = new ParticleSystem.MinMaxCurve(6f * scale, 14f * scale);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.6f), new Color(1f, 0.45f, 0.1f));
        var fe = fire.emission; fe.rateOverTime = 0f; fe.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(80 * Mathf.Min(scale, 2f))) });
        var fs = fire.shape; fs.shapeType = ParticleSystemShapeType.Sphere; fs.radius = 2f * scale;
        var fc = fire.colorOverLifetime; fc.enabled = true;
        var fg = new Gradient();
        fg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 0.5f), new GradientColorKey(new Color(0.3f, 0.1f, 0.05f), 1f) },
                   new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fc.color = fg;
        fire.Play();

        var smoke = New("Smoke", holder.transform, pos, Alpha, true);
        var s = smoke.main;
        s.loop = false; s.duration = 1f; s.maxParticles = 150;
        s.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
        s.startSpeed = new ParticleSystem.MinMaxCurve(4f * scale, 14f * scale);
        s.startSize = new ParticleSystem.MinMaxCurve(10f * scale, 22f * scale);
        s.startColor = new ParticleSystem.MinMaxGradient(new Color(0.15f, 0.15f, 0.15f, 0.8f), new Color(0.35f, 0.33f, 0.3f, 0.7f));
        var se = smoke.emission; se.rateOverTime = 0f; se.SetBursts(new[] { new ParticleSystem.Burst(0.1f, (short)(50 * Mathf.Min(scale, 2f))) });
        var ss = smoke.shape; ss.shapeType = ParticleSystemShapeType.Sphere; ss.radius = 3f * scale;
        var sc = smoke.colorOverLifetime; sc.enabled = true;
        var sg = new Gradient();
        sg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                   new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        sc.color = sg;
        smoke.Play();

        var light = new GameObject("Flash").AddComponent<Light>();
        light.transform.SetParent(holder.transform, false);
        light.type = LightType.Point; light.color = new Color(1f, 0.6f, 0.3f); light.range = 120f * scale; light.intensity = 8f;
        holder.AddComponent<AscendFlashFade>().flash = light;

        Object.Destroy(holder, 10f);
    }
}

// Fades an explosion's flash of light
public class AscendFlashFade : MonoBehaviour
{
    public Light flash;
    void Update() { if (flash) flash.intensity = Mathf.MoveTowards(flash.intensity, 0f, Time.deltaTime * 6f); }
}
